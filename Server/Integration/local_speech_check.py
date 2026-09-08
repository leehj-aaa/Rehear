"""Loopback-only Unity smoke server using actual EVC routes and Azure speech.

Uses synthetic fixtures, an in-memory session and a labelled mock LLM; no production
DB, PIN or microphone. Never deploy this harness. Azure credentials stay in Python.
"""
import argparse
import asyncio
import importlib
import json
import os
from pathlib import Path
import sys
import types


def run():
    parser = argparse.ArgumentParser()
    parser.add_argument("--backend", type=Path, required=True)
    parser.add_argument("--env", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    for line in args.env.read_text(encoding="utf-8-sig").splitlines():
        name, sep, value = line.partition("=")
        if sep and name.strip().startswith("AZURE_"):
            os.environ[name.strip()] = value.strip().strip('"').strip("'")
    sys.path.insert(0, str(args.backend.resolve()))
    # Sparse checkout intentionally excludes production database configuration.
    database = types.ModuleType("odi.db")
    class NoDatabase:
        def __getattr__(self, name):
            raise RuntimeError("Production database access is forbidden in this smoke harness")
    database.odidb = NoDatabase()
    sys.modules["odi.db"] = database
    from fastapi import FastAPI
    from fastapi.responses import Response
    import uvicorn
    from odi.EVC.azure_speech import synthesize
    from odi.EVC.schema import GeneratedQuestion, GeneratedQuestionSet, SmartStartOptions, TranscriptSegment
    from odi.EVC.session_store import session_store
    routes = importlib.import_module("odi.EVC.router")
    original_submit = routes.submit_answer
    calls = {"tts": 0, "mock_llm": 0, "answers_saved": 0}
    cache = {}
    def write_report():
        (args.output / "server_report.json").write_text(json.dumps(calls, indent=2), encoding="utf-8")
    class MockLLM:
        def generate(self, payload):
            assert payload["qa_history"][-1]["answer"]
            assert payload["next_audience"]["agent_id"]
            assert payload["total_question_count"] == 2
            calls["mock_llm"] += 1
            write_report()
            return GeneratedQuestionSet(questions=[GeneratedQuestion(id="q1", order=1,
                question="사용자 만족도는 어떤 방법으로 측정하셨나요?", intent="답변을 반영한 검증 질문", source_steps=[1])])
    async def speech(text, voice):
        key = (text, voice)
        if key not in cache:
            cache[key] = await synthesize(text, voice)
            calls["tts"] += 1
            write_report()
        return cache[key]
    async def submit(**kwargs):
        result = await original_submit(**kwargs, provider=MockLLM())
        if result["saved"]:
            calls["answers_saved"] += 1
            write_report()
        return result
    routes.synthesize = speech
    routes.submit_answer = submit
    app = FastAPI()
    app.include_router(routes.router)
    @app.get("/test/session")
    async def fixture_session():
        return json.loads((args.output / "session.json").read_text(encoding="utf-8"))
    @app.get("/test/answer")
    async def fixture_answer():
        return Response((args.output / "synthetic_answer.wav").read_bytes(), media_type="audio/wav")
    @app.on_event("startup")
    async def initialize():
        record, token = await session_store.create_session(SmartStartOptions(presentation_title="로컬 음성 연결 검증", seed=7))
        record.transcript_segments = [TranscriptSegment(step=1, client_time_s=1, slide_index=0,
            text="대조군과 비교하여 추천 결과의 다양성을 검증했습니다.", word_count=7)]
        record.generated_questions = [GeneratedQuestion(id="q1", order=1, question="발표 내용을 어떻게 검증하셨나요?", intent="검증", source_steps=[1]),
            GeneratedQuestion(id="q2", order=2, question="교체 전 두 번째 질문입니다.", intent="검증", source_steps=[1])]
        record.question_generation_status = "ready"
        answer = await speech("대조군과 비교하고 사용자 만족도를 측정했습니다.", "ko-KR-InJoonNeural")
        (args.output / "synthetic_answer.wav").write_bytes(answer)
        (args.output / "session.json").write_text(json.dumps({"session_id":str(record.session_id), "session_token":token,
            "seed":7, "step":1, "slide_count":1, "status":"active"}), encoding="utf-8")
        write_report()
        print("READY loopback Azure speech smoke server; LLM is MOCK; no production database.", flush=True)
    uvicorn.run(app, host="127.0.0.1", port=8092, access_log=False, log_level="warning")

if __name__ == "__main__":
    run()
