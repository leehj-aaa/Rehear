"""PC recording server: real EVC pipeline, Azure speech and Qwen, loopback only.

Uses unlinked in-memory sessions: no production DB, PIN claims or web report writes.
The Editor loads the presentation through its usual PIN screen, then starts a new
local EVC session. No canned questions or mock providers are installed.
"""
import argparse
import importlib
import os
from pathlib import Path
import sys
import types

ROOT = Path(__file__).resolve().parents[2]


def load_settings():
    for path, allowed in (
        (ROOT / 'Server/AzureSpeech/.env', {'AZURE_SPEECH_KEY', 'AZURE_SPEECH_REGION', 'AZURE_STT_MODE'}),
        (Path(__file__).with_name('.env'), {'QWEN_API_KEY', 'QWEN_BASE_URL', 'QWEN_MODEL'}),
    ):
        if not path.exists():
            continue
        for line in path.read_text(encoding='utf-8-sig').splitlines():
            name, sep, value = line.partition('=')
            if sep and name.strip() in allowed:
                os.environ[name.strip()] = value.strip().strip('"').strip("'")
    missing = [name for name in ('AZURE_SPEECH_KEY', 'AZURE_SPEECH_REGION', 'QWEN_API_KEY', 'QWEN_BASE_URL', 'QWEN_MODEL')
               if not os.environ.get(name, '').strip()]
    if missing:
        raise RuntimeError('Missing local settings: ' + ', '.join(missing))
    from qwen_provider import endpoint
    endpoint()
    os.environ['EVC_STT_PROVIDER'] = 'azure'
    os.environ['EVC_STT_TIMEOUT_S'] = '120'
    os.environ['EVC_LLM_TIMEOUT_S'] = '90'
    os.environ['EVC_QUESTION_TIMEOUT_S'] = '90'
    os.environ['EVC_DEBUG_LOG'] = 'false'
    os.environ['EVC_UPLOAD_DIR'] = str(ROOT / 'Temp/LocalRecording/uploads')
    os.environ['EVC_DEBUG_LOG_DIR'] = str(ROOT / 'Temp/LocalRecording/logs')


def create_app(backend):
    load_settings()
    sys.path.insert(0, str(backend.resolve()))
    database = types.ModuleType('odi.db')

    class NoProductionDatabase:
        def __getattr__(self, name):
            raise RuntimeError('Local recording does not access the production database')

    database.odidb = NoProductionDatabase()
    sys.modules['odi.db'] = database
    from fastapi import FastAPI
    routes = importlib.import_module('odi.EVC.router')
    from qwen_provider import install
    install(routes)
    app = FastAPI(title='Rehear Local Recording', docs_url=None, redoc_url=None)
    app.include_router(routes.router, prefix='/odi')

    @app.get('/health')
    async def health():
        return {'service': 'rehear-local-recording', 'ready': True,
                'speech': 'azure', 'questions': 'qwen', 'mock': False,
                'web_report_saved': False}

    return app


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--check', action='store_true')
    parser.add_argument('--backend', type=Path,
                        default=ROOT / 'Builds/ServerReview/sushisite/sushi-fast')
    args = parser.parse_args()
    try:
        app = create_app(args.backend)
        if args.check:
            print('PASS: real local EVC routes loaded; credentials present (values hidden).')
            return
        import uvicorn
        print('Local recording: http://127.0.0.1:8093; real Azure/Qwen; no web report writes.', flush=True)
        uvicorn.run(app, host='127.0.0.1', port=8093, access_log=False, log_level='warning')
    except RuntimeError as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)


if __name__ == '__main__':
    main()
