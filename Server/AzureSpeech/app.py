"""Azure speech gateway. Run behind HTTPS; credentials stay on this server."""
import asyncio
import hashlib
import io
import json
import os
import re
import sqlite3
import uuid
import wave
from contextlib import contextmanager
from pathlib import Path
from urllib.parse import quote
from xml.sax.saxutils import escape

import httpx
from fastapi import FastAPI, Header, HTTPException, Request
from fastapi.responses import Response

app = FastAPI(title="ReHear Azure Speech", docs_url=None, redoc_url=None)
slots = asyncio.Semaphore(4)
answer_lock = asyncio.Lock()
MAX_WAV_BYTES = 20_000_000


def setting(name):
    value = os.environ.get(name, "").strip()
    if not value:
        raise HTTPException(503, f"Server configuration missing: {name}")
    return value


def azure_headers():
    return {"Ocp-Apim-Subscription-Key": setting("AZURE_SPEECH_KEY")}


async def checked_request(method, url, **kwargs):
    try:
        async with httpx.AsyncClient(timeout=150, follow_redirects=False) as client:
            response = await client.request(method, url, **kwargs)
    except httpx.HTTPError:
        raise HTTPException(502, "Speech dependency unavailable") from None
    if response.status_code in (401, 403) and "X-EVC-Session-Token" in kwargs.get("headers", {}):
        raise HTTPException(403, "Invalid session credentials")
    if not 200 <= response.status_code < 300:
        # Do not expose upstream bodies, keys or session tokens.
        raise HTTPException(502, f"Speech dependency returned HTTP {response.status_code}")
    return response


async def questions_for(session_id, token):
    if not token or len(token) > 4096:
        raise HTTPException(401, "Session token required")
    if not re.fullmatch(r"[A-Za-z0-9_-]{1,128}", session_id):
        raise HTTPException(400, "Invalid session id")
    base = setting("EVC_BASE_URL").rstrip("/")
    if not base.startswith("https://"):
        raise HTTPException(503, "EVC_BASE_URL must use HTTPS")
    response = await checked_request("GET", base + "/sessions/" + quote(session_id, safe="") + "/questions",
                                     headers={"X-EVC-Session-Token": token})
    data = response.json()
    if data.get("session_id") != session_id or not isinstance(data.get("questions"), list):
        raise HTTPException(502, "Invalid question response")
    return data["questions"]


async def question_for(session_id, index, token):
    questions = await questions_for(session_id, token)
    if index < 0 or index >= len(questions):
        raise HTTPException(404, "Question not found")
    question = questions[index]
    if not question.get("id") or not question.get("question") or len(question["question"]) > 5000:
        raise HTTPException(502, "Invalid question")
    return question


def validate_wav(data):
    try:
        with wave.open(io.BytesIO(data), "rb") as audio:
            frames = audio.getnframes()
            rate = audio.getframerate()
            if audio.getnchannels() != 1 or audio.getsampwidth() != 2 or not 8000 <= rate <= 48000:
                raise ValueError()
            if not 0 < frames / rate <= 601 or len(audio.readframes(frames)) != frames * 2:
                raise ValueError()
    except (wave.Error, EOFError, ValueError):
        raise HTTPException(422, "Expected mono PCM16 WAV up to ten minutes") from None


async def transcribe(data):
    mode = os.environ.get("AZURE_STT_MODE", "continuous").strip().lower()
    if mode == "continuous":
        from continuous_stt import recognize
        return await asyncio.to_thread(recognize, data, setting("AZURE_SPEECH_KEY"), setting("AZURE_SPEECH_REGION"))
    if mode != "fast":
        raise HTTPException(503, "AZURE_STT_MODE must be continuous or fast")
    endpoint = setting("AZURE_SPEECH_ENDPOINT").rstrip("/")
    if not re.fullmatch(r"https://[a-zA-Z0-9-]+\.cognitiveservices\.azure\.com", endpoint):
        raise HTTPException(503, "Use the Azure resource HTTPS custom endpoint")
    response = await checked_request("POST", endpoint + "/speechtotext/transcriptions:transcribe?api-version=2025-10-15",
        headers=azure_headers(), files={"audio": ("answer.wav", data, "audio/wav")},
        data={"definition": json.dumps({"locales": ["ko-KR"]})})
    result = response.json()
    return " ".join(p.get("text", "").strip() for p in result.get("combinedPhrases", [])).strip()


VOICES = ("ko-KR-InJoonNeural", "ko-KR-BongJinNeural", "ko-KR-GookMinNeural",
          "ko-KR-SunHiNeural", "ko-KR-JiMinNeural", "ko-KR-SeoHyeonNeural", "ko-KR-SoonBokNeural", "ko-KR-YuJinNeural")


async def synthesize(text, voice):
    region = setting("AZURE_SPEECH_REGION")
    if not re.fullmatch(r"[a-z0-9]+", region):
        raise HTTPException(503, "Invalid Azure region")
    if voice not in VOICES:
        raise HTTPException(422, "Unsupported Korean voice")
    ssml = f'<speak version="1.0" xml:lang="ko-KR"><voice name="{voice}">{escape(text)}</voice></speak>'
    headers = {**azure_headers(), "Content-Type": "application/ssml+xml",
               "X-Microsoft-OutputFormat": "riff-24khz-16bit-mono-pcm", "User-Agent": "ReHearSpeech"}
    response = await checked_request("POST", f"https://{region}.tts.speech.microsoft.com/cognitiveservices/v1",
                                      headers=headers, content=ssml.encode("utf-8"))
    if not response.content.startswith(b"RIFF"):
        raise HTTPException(502, "Invalid speech audio")
    return response.content


@contextmanager
def database():
    # Mount this directory on durable private storage in deployment.
    path = Path(setting("SPEECH_DATA_DIR"))
    path.mkdir(parents=True, exist_ok=True)
    connection = sqlite3.connect(path / "answers.sqlite3", timeout=30)
    connection.row_factory = sqlite3.Row
    try:
        connection.execute("""CREATE TABLE IF NOT EXISTS answers (
            session_id TEXT, question_id TEXT, question_index INTEGER, request_id TEXT,
            audio_hash TEXT, question TEXT, transcript TEXT,
            PRIMARY KEY (session_id, question_id), UNIQUE (session_id, request_id))""")
        yield connection
        connection.commit()
    finally:
        connection.close()


@app.post("/sessions/{session_id}/questions/{index}/speech")
async def question_speech(session_id: str, index: int, x_evc_session_token: str = Header(default=""),
                          x_speech_voice: str = Header(default=""), x_audience_id: str = Header(default="")):
    question = await question_for(session_id, index, x_evc_session_token)
    if x_speech_voice not in VOICES or not re.fullmatch(r"[A-Za-z0-9_-]{1,128}", x_audience_id):
        raise HTTPException(422, "Valid audience and voice profile required")
    async with slots:
        audio = await synthesize(question["question"], x_speech_voice)
    return Response(audio, media_type="audio/wav", headers={"Cache-Control": "no-store"})


@app.post("/sessions/{session_id}/questions/{index}/answer")
async def answer(session_id: str, index: int, request: Request,
                 x_evc_session_token: str = Header(default=""), x_request_id: str = Header(default="")):
    question = await question_for(session_id, index, x_evc_session_token)
    try:
        uuid.UUID(x_request_id)
    except ValueError:
        raise HTTPException(400, "X-Request-Id must be a UUID") from None
    data = bytearray()
    async for chunk in request.stream():
        if len(data) + len(chunk) > MAX_WAV_BYTES:
            raise HTTPException(413, "Answer too large")
        data.extend(chunk)
    validate_wav(data)
    digest = hashlib.sha256(data).hexdigest()
    async with answer_lock:
        with database() as db:
            existing = db.execute("SELECT * FROM answers WHERE session_id=? AND (question_id=? OR request_id=?)",
                                  (session_id, question["id"], x_request_id)).fetchone()
        if existing:
            if existing["request_id"] != x_request_id or existing["audio_hash"] != digest or existing["question_id"] != question["id"]:
                raise HTTPException(409, "Answer already saved or request id reused")
            transcript = existing["transcript"]
        else:
            async with slots:
                transcript = await transcribe(bytes(data))
            if not transcript:
                return {"question_index": index, "request_id": x_request_id, "transcript": "", "saved": False}
            with database() as db:
                db.execute("INSERT INTO answers VALUES (?,?,?,?,?,?,?)",
                           (session_id, question["id"], index, x_request_id, digest, question["question"], transcript))
    return {"question_index": index, "request_id": x_request_id, "transcript": transcript, "saved": True}


@app.get("/sessions/{session_id}/answers")
async def get_answers(session_id: str, x_evc_session_token: str = Header(default="")):
    await questions_for(session_id, x_evc_session_token)
    with database() as db:
        rows = db.execute("SELECT question_id,question_index,question,transcript FROM answers WHERE session_id=? ORDER BY question_index",
                          (session_id,)).fetchall()
    return {"session_id": session_id, "answers": [dict(row) for row in rows]}
