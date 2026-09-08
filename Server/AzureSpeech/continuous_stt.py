"""Server-only Speech SDK, compatible with the resource's F0 tier."""
import io
import re
import threading
import wave

import azure.cognitiveservices.speech as sdk
from fastapi import HTTPException


def recognize(data, key, region):
    if not re.fullmatch(r"[a-z0-9]+", region):
        raise HTTPException(503, "Invalid Azure region")
    with wave.open(io.BytesIO(data), "rb") as wav:
        rate = wav.getframerate()
        duration = wav.getnframes() / rate
        pcm = wav.readframes(wav.getnframes())
    config = sdk.SpeechConfig(subscription=key, region=region)
    config.speech_recognition_language = "ko-KR"
    stream = sdk.audio.PushAudioInputStream(
        stream_format=sdk.audio.AudioStreamFormat(samples_per_second=rate, bits_per_sample=16, channels=1))
    audio = sdk.audio.AudioConfig(stream=stream)
    recognizer = sdk.SpeechRecognizer(speech_config=config, audio_config=audio)
    done = threading.Event()
    phrases, failures = [], []

    def recognized(event):
        if event.result.reason == sdk.ResultReason.RecognizedSpeech and event.result.text:
            phrases.append(event.result.text.strip())

    def canceled(event):
        if event.reason == sdk.CancellationReason.Error:
            # Raw SDK errors can contain URLs and credentials. Return the enum only.
            failures.append(str(event.error_code))
        done.set()

    recognizer.recognized.connect(recognized)
    recognizer.canceled.connect(canceled)
    recognizer.session_stopped.connect(lambda _: done.set())
    started = False
    try:
        recognizer.start_continuous_recognition_async().get()
        started = True
        for offset in range(0, len(pcm), 8192):
            stream.write(pcm[offset:offset + 8192])
        stream.close()
        if not done.wait(max(60, duration + 60)):
            raise HTTPException(504, "Azure STT timed out")
        if failures:
            raise HTTPException(502, "Azure STT failed: " + failures[0])
        return " ".join(phrases).strip()
    except HTTPException:
        raise
    except Exception:
        raise HTTPException(502, "Azure STT SDK unavailable or recognition failed") from None
    finally:
        stream.close()
        if started:
            try:
                recognizer.stop_continuous_recognition_async().get()
            except Exception:
                pass
