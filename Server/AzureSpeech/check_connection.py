"""Check the real Azure resource without recording the user's microphone.

Keys are read from the server environment or a masked prompt, never written.
--live makes six short TTS requests and one STT request (usage is billable).
"""
import argparse
import asyncio
import getpass
import os
import re
import sys

from fastapi import HTTPException
import app as speech


def configure():
    for name, prompt, default in (
        ("AZURE_SPEECH_REGION", "Speech region", "koreacentral"),
    ):
        if not os.environ.get(name):
            os.environ[name] = input(f"{prompt} [{default}]: ").strip() or default
    if os.environ.get("AZURE_STT_MODE") == "fast" and not os.environ.get("AZURE_SPEECH_ENDPOINT"):
        os.environ["AZURE_SPEECH_ENDPOINT"] = input("Speech custom HTTPS endpoint: ").strip()
    if not os.environ.get("AZURE_SPEECH_KEY"):
        os.environ["AZURE_SPEECH_KEY"] = getpass.getpass("Speech key (hidden; not saved): ").strip()


async def check(live):
    region = speech.setting("AZURE_SPEECH_REGION")
    if not re.fullmatch(r"[a-z0-9]+", region):
        raise HTTPException(503, "Invalid Azure region")
    response = await speech.checked_request(
        "GET", f"https://{region}.tts.speech.microsoft.com/cognitiveservices/voices/list",
        headers=speech.azure_headers())
    available = {v.get("ShortName") for v in response.json()}
    missing = set(speech.VOICES) - available
    if missing:
        raise HTTPException(503, "Missing voices: " + ", ".join(sorted(missing)))
    print("PASS: Azure authentication and all six Korean voices.")
    if not live:
        print("STT/TTS not yet tested. Use --live for six short TTS requests and one STT request.")
        return
    first = None
    for voice in speech.VOICES:
        audio = await speech.synthesize("안녕하세요. 발표 내용을 설명해 주세요.", voice)
        speech.validate_wav(audio)
        first = first or audio
        print(f"PASS: {voice} produced valid PCM16 WAV.")
    transcript = await speech.transcribe(first)
    if not transcript:
        raise HTTPException(502, "STT returned an empty transcript for synthesized speech")
    print("PASS: Azure STT recognized the synthetic Korean test sentence.")
    print("Resource check only. Unity, EVC authentication, HTTPS deployment and Quest microphone still require integration testing.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--live", action="store_true", help="Make billable synthetic TTS/STT requests; no microphone recording")
    args = parser.parse_args()
    try:
        configure()
        asyncio.run(check(args.live))
    except HTTPException as error:
        print(f"FAIL: {error.detail}", file=sys.stderr)
        sys.exit(1)
    except (KeyboardInterrupt, EOFError):
        print("Cancelled. No key was saved.", file=sys.stderr)
        sys.exit(2)
