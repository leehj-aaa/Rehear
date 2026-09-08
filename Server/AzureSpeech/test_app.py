import io
import os
import tempfile
import unittest
import uuid
import wave
from unittest.mock import AsyncMock, patch

import httpx
from fastapi.testclient import TestClient
import app as speech


def wav():
    output = io.BytesIO()
    with wave.open(output, "wb") as audio:
        audio.setnchannels(1)
        audio.setsampwidth(2)
        audio.setframerate(16000)
        audio.writeframes(b"\x01\x00" * 1600)
    return output.getvalue()


class SpeechGatewayTests(unittest.TestCase):
    def setUp(self):
        self.storage = tempfile.TemporaryDirectory()
        self.env = patch.dict(os.environ, {"EVC_BASE_URL": "https://evc.example.test/evc",
                                          "SPEECH_DATA_DIR": self.storage.name})
        self.env.start()
        self.client = TestClient(speech.app)
        self.headers = {"X-EVC-Session-Token": "test-session-token", "X-Request-Id": str(uuid.uuid4()),
                        "X-Speech-Voice": "ko-KR-InJoonNeural", "X-Audience-Id": "A01"}
        self.upstream = patch.object(speech, "checked_request", new=AsyncMock(return_value=httpx.Response(200, json={
            "session_id": "test-session", "questions": [{"id": "q1", "question": "연구의 한계는?"}]})))
        self.check = self.upstream.start()
        self.stt = patch.object(speech, "transcribe", new=AsyncMock(return_value="연구의 한계는 표본 규모입니다."))
        self.transcribe = self.stt.start()

    def tearDown(self):
        self.client.close()
        self.stt.stop(); self.upstream.stop(); self.env.stop(); self.storage.cleanup()

    def submit(self, data=None, headers=None):
        return self.client.post("/sessions/test-session/questions/0/answer", content=wav() if data is None else data,
                                headers=self.headers if headers is None else headers)

    def test_retries_save_once_and_remain_authenticated(self):
        first = self.submit()
        second = self.submit()
        self.assertEqual(first.status_code, 200)
        self.assertEqual(first.json(), second.json())
        self.assertTrue(first.json()["saved"])
        self.assertEqual(self.transcribe.await_count, 1)
        self.assertEqual(self.check.await_count, 2)
        saved = self.client.get("/sessions/test-session/answers", headers=self.headers).json()
        self.assertEqual(saved["answers"][0]["question_id"], "q1")
        self.assertEqual(saved["answers"][0]["transcript"], first.json()["transcript"])

    def test_missing_credentials_do_not_reach_azure(self):
        self.assertEqual(self.submit(headers={}).status_code, 401)
        self.transcribe.assert_not_awaited()
        self.check.assert_not_awaited()

    def test_wrong_session_or_invalid_index_rejected(self):
        self.assertEqual(self.client.post("/sessions/test-session/questions/5/answer", headers=self.headers, content=wav()).status_code, 404)
        self.assertEqual(self.client.post("/sessions/someone-else/questions/0/answer", headers=self.headers, content=wav()).status_code, 502)
        self.transcribe.assert_not_awaited()

    def test_invalid_or_truncated_audio_not_transcribed(self):
        self.assertEqual(self.submit(b"not audio").status_code, 422)
        self.assertEqual(self.submit(wav()[:-4]).status_code, 422)
        self.transcribe.assert_not_awaited()

    def test_empty_recognition_not_marked_saved(self):
        self.transcribe.return_value = ""
        result = self.submit()
        self.assertEqual(result.status_code, 200)
        self.assertFalse(result.json()["saved"])
        self.transcribe.return_value = "다시 녹음한 응답"
        self.assertTrue(self.submit().json()["saved"])

    def test_changed_audio_cannot_reuse_id(self):
        self.submit()
        changed = bytearray(wav()); changed[-1] = 3
        self.assertEqual(self.submit(bytes(changed)).status_code, 409)
        self.assertEqual(self.transcribe.await_count, 1)

    def test_tts_uses_server_question(self):
        with patch.object(speech, "synthesize", new=AsyncMock(return_value=wav())) as tts:
            response = self.client.post("/sessions/test-session/questions/0/speech", headers=self.headers)
            self.assertEqual(response.status_code, 200)
            self.assertTrue(response.content.startswith(b"RIFF"))
            tts.assert_awaited_once_with("연구의 한계는?", "ko-KR-InJoonNeural")

    def test_six_prefab_voices_and_invalid_voice(self):
        with patch.object(speech, "synthesize", new=AsyncMock(return_value=wav())) as tts:
            for voice in speech.VOICES:
                headers = {**self.headers, "X-Speech-Voice": voice}
                self.assertEqual(self.client.post("/sessions/test-session/questions/0/speech", headers=headers).status_code, 200)
                self.assertEqual(tts.call_args.args[1], voice)
            headers = {**self.headers, "X-Speech-Voice": "unknown-voice"}
            self.assertEqual(self.client.post("/sessions/test-session/questions/0/speech", headers=headers).status_code, 422)
            self.assertEqual(tts.await_count, 6)


class AzureWireTests(unittest.IsolatedAsyncioTestCase):
    async def test_tts_escapes_text_and_returns_lipsync_wav(self):
        with patch.dict(os.environ, {"AZURE_SPEECH_REGION": "koreacentral", "AZURE_SPEECH_KEY": "test-key"}):
            with patch.object(speech, "checked_request", new=AsyncMock(return_value=httpx.Response(200, content=wav()))) as send:
                await speech.synthesize('A < B & C', 'ko-KR-JiMinNeural')
                self.assertEqual(send.call_args.args[1], 'https://koreacentral.tts.speech.microsoft.com/cognitiveservices/v1')
                self.assertIn('A &lt; B &amp; C', send.call_args.kwargs['content'].decode())
                self.assertEqual(send.call_args.kwargs['headers']['X-Microsoft-OutputFormat'], 'riff-24khz-16bit-mono-pcm')

    async def test_stt_uses_korean_locale_and_combines_phrases(self):
        with patch.dict(os.environ, {"AZURE_STT_MODE": "fast", "AZURE_SPEECH_ENDPOINT": "https://test.cognitiveservices.azure.com", "AZURE_SPEECH_KEY": "test-key"}):
            response = httpx.Response(200, json={"combinedPhrases": [{"text": "첫 문장."}, {"text": "둘째 문장."}]})
            with patch.object(speech, "checked_request", new=AsyncMock(return_value=response)) as send:
                self.assertEqual(await speech.transcribe(wav()), "첫 문장. 둘째 문장.")
                self.assertIn('2025-10-15', send.call_args.args[1])
                self.assertEqual(speech.json.loads(send.call_args.kwargs['data']['definition']), {"locales": ["ko-KR"]})


if __name__ == "__main__":
    unittest.main()
