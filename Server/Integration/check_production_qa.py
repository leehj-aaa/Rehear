"""Explicit live smoke check using synthetic speech and a disposable, PIN-free session.

Credentials remain in the ignored local Azure .env; never written to the report.
"""
import json
from pathlib import Path
import sys
import uuid
import httpx

ROOT = Path(__file__).resolve().parents[2]
BASE = "https://rehear.chobab.app/odi/xreal_rehear/evc"
OUT = ROOT / "Builds/ProductionQA"


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    report = {}
    session = None
    headers = {}
    stage = "synthetic_audio"
    def save():
        (OUT / "check-report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    def checked(response):
        if response.status_code >= 400:
            # Server error codes/messages only; never request headers or tokens.
            raise RuntimeError(f"HTTP {response.status_code}: {response.text[:1200]}")
        return response
    with httpx.Client(timeout=180, follow_redirects=False) as client:
        try:
            env = {}
            for line in (ROOT / "Server/AzureSpeech/.env").read_text(encoding="utf-8-sig").splitlines():
                name, sep, value = line.partition("=")
                if sep:
                    env[name.strip()] = value.strip().strip('"').strip("'")
            text = "오늘은 가상현실 발표 연습 서비스의 효과를 검증하는 방법을 설명하겠습니다. 참여자 삼십 명을 실험군과 대조군으로 나누고 같은 주제로 세 번 발표하도록 합니다. 실험군은 가상 청중 앞에서 연습하고 대조군은 혼자 연습합니다. 발표 전후의 불안 점수와 말하기 속도, 청중과의 시선 접촉을 비교합니다. 평가자는 집단 정보를 모르는 상태에서 동일한 기준으로 채점합니다. 개인 음성 자료는 동의를 받고 수집하며 평가가 끝나면 삭제합니다."
            wav = checked(client.post(f"https://{env['AZURE_SPEECH_REGION']}.tts.speech.microsoft.com/cognitiveservices/v1", headers={"Ocp-Apim-Subscription-Key": env["AZURE_SPEECH_KEY"], "Content-Type": "application/ssml+xml", "X-Microsoft-OutputFormat": "riff-16khz-16bit-mono-pcm"}, content=f'<speak version="1.0" xml:lang="ko-KR"><voice name="ko-KR-InJoonNeural">{text}</voice></speak>'.encode())).content
            assert wav[:4] == b"RIFF"
            report[stage] = "PASS"
            stage = "production_session"
            data = checked(client.post(BASE + "/smart-start", data={"presentation_title": "Synthetic APK integration check", "seed": "17", "independent_reactions": "true"})).json()
            session = data["session_id"]
            headers = {"X-EVC-Session-Token": data["session_token"]}
            audiences = [a["agent_id"] for a in data["audiences"]]
            report[stage] = "PASS"
            stage = "production_stt_and_evaluation"
            data = checked(client.post(BASE + "/update", headers=headers, data={"session_id": session, "request_id": str(uuid.uuid4()), "expected_step": "0", "client_time_s": "45", "current_slide_index": "0", "language": "ko-KR"}, files={"audio": ("synthetic.wav", wav, "audio/wav")})).json()
            assert data.get("latest_speech"), "Empty presentation transcript"
            report[stage] = {"status": "PASS", "transcript": data["latest_speech"]}
            stage = "production_question_generation"
            data = checked(client.post(f"{BASE}/sessions/{session}/questions/generate", headers=headers, json={"request_id": str(uuid.uuid4()), "question_count": 2})).json()
            assert len(data["questions"]) == 2
            report[stage] = {"status": "PASS", "questions": data["questions"]}
            stage = "production_question_tts"
            audio = checked(client.post(f"{BASE}/sessions/{session}/questions/0/speech", headers={**headers, "X-Speech-Voice": "ko-KR-SunHiNeural", "X-Audience-Id": audiences[0]})).content
            assert audio[:4] == b"RIFF"
            report[stage] = {"status": "PASS", "bytes": len(audio)}
            stage = "production_answer_stt_and_followup"
            request_id = str(uuid.uuid4())
            result = checked(client.post(f"{BASE}/sessions/{session}/questions/0/answer", headers={**headers, "Content-Type": "audio/wav", "X-Request-Id": request_id, "X-Next-Audience-Id": audiences[1]}, content=wav)).json()
            assert result["saved"] and result["transcript"] and result["request_id"] == request_id
            assert result["total"] == 2 and result.get("next_question")
            report[stage] = {"status": "PASS", "result": result}
            report["overall"] = "PASS"
        except Exception as error:
            report[stage] = {"status": "FAIL", "error": str(error)}
            report["overall"] = "FAIL"
        finally:
            if session:
                try:
                    checked(client.delete(f"{BASE}/sessions/{session}", headers=headers))
                    report["test_session_cleanup"] = "PASS"
                except Exception as error:
                    report["test_session_cleanup"] = str(error)
            save()
            print(json.dumps(report, ensure_ascii=False, indent=2), flush=True)
    return 0 if report.get("overall") == "PASS" and report.get("test_session_cleanup") == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
