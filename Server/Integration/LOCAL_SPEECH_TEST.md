# Local Azure / Unity / Quest speech validation

This harness is for development only. It binds to `127.0.0.1:8092`, uses synthetic
session data, and substitutes a labelled mock LLM for the existing question provider.
The actual EVC speech/answer routes, Azure TTS/STT, Unity speech client, recorder and
OVR lip sync are exercised. Production database access throws an error.

Azure secrets remain in the ignored `Server/AzureSpeech/.env`. Never package this
file or copy the key into Unity. The harness does not change Azure billing or deploy
anything to the server repository.

## PC

Start `local_speech_check.py` with the local backend checkout, `.env`, and output
directory arguments. The Python environment must have the backend speech/test
dependencies installed. Use a new server process for each run (sessions are in memory).

```powershell
& Server/AzureSpeech/.venv/Scripts/python.exe Server/Integration/local_speech_check.py --backend Temp/RehearBackend/sushi-fast --env Server/AzureSpeech/.env --output Temp/LocalSpeechCheck
Set-Content Temp/RehearLocalSpeech.request run
```

Unity must be idle with one saved scene. The editor harness creates a disposable
play scene and restores the original saved scene and HTTP setting after completion.
`Temp/LocalSpeechCheck/unity_report.txt` records the results. It opens the microphone
briefly and discards those samples. Only the synthetic answer WAV goes to Azure.

The completed PC run on 2026-09-08 verified:

- Six distinct Korean Azure voices move actual audience jaw blendshapes through OVR
  audio analysis (peak weights 27–53), then restore the original face.
- Speech pause, resume and stop.
- Both questions: QS hand gesture, baseline return, speech, answer-start button,
  microphone start, double-click lock, answer-end button, Azure STT, answer save.
- First answer updates question 2 using the mock LLM; total remains two.
- Final answer reaches ready-for-feedback, without entering production report paths.

Fixed during this run: Windows Editor Mono could not resolve `OVRLipSync`; a scoped
editor import mapping fixes the installed DLL path. Q&A now excludes disabled and
unloaded-scene audience objects from both speaker assignment and lookup.

## Quest diagnostic APK

`Temp/RehearDeviceSpeechBuild.request` triggers an Android ARM64/IL2CPP development
build of a disposable diagnostic scene. Its package is
`com.boracles.rehear.speechcheck`, separate from `com.audi.rehear`.
Output: `Builds/SpeechCheck/Rehear-SpeechCheck.apk`.

The extra `REHEAR_SPEECH_SMOKE` define enables loopback test routing only in this
explicit build. Ordinary APKs retain HTTPS-only speech configuration. The build
restores the original package identifier and HTTP setting. It leaves Android as the
active target for subsequent Quest work.

Restart the local server for a fresh session and forward only this test port using
`adb reverse tcp:8092 tcp:8092`. After installing and launching the diagnostic APK,
its private `files/speech-report.txt` contains device results. The test captures and
discards a short microphone sample; Azure receives the synthetic fixture only.
Remove the reverse port when finished.

## Still separate from these tests

Production deployment, real LLM question quality, and recognition of a participant's
actual spoken answer are not demonstrated by synthetic/mock tests. Once the server
owner merges and deploys the branch, configure and validate the production HTTPS
speech URL before distributing the main APK.
