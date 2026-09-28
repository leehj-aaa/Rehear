# PC 촬영용 실제 AI 서버

APK 빌드 없이 Unity Editor에서 사용합니다. 정해진 질문이나 테스트용 AI를 사용하지 않습니다.

- Azure: 실제 발표·답변 음성 인식과 청중 질문 음성 합성
- Qwen API: 실제 발표 기반 질문, 답변 기반 다음 질문, 피드백
- 기존 PIN 화면: 운영 서버에서 발표 자료를 읽습니다. 따라서 인터넷은 필요합니다.
- 이후 EVC 세션은 PC 메모리에만 존재하며 운영 PIN을 사용 완료로 바꾸지 않습니다.
- 피드백은 Unity에서 볼 수 있지만 운영 웹사이트에는 저장하지 않습니다. 서버를 끄면 세션은 사라집니다.

## 준비

`.env`에 `QWEN_API_KEY`, `QWEN_BASE_URL`, `QWEN_MODEL`을 입력합니다. 키 값은 Git과 APK에 포함하지 않습니다.
API 주소는 키를 발급한 서비스·지역과 일치해야 합니다. Alibaba Cloud Model Studio 또는 OpenRouter의 OpenAI 호환 API 주소를 사용합니다.
질문·평가·피드백 모두 Qwen을 호출하며 OpenAI API 키는 필요하지 않습니다.
Azure 설정은 기존 `Server/AzureSpeech/.env`를 읽습니다. 실제 Azure/Qwen 사용량이 발생합니다.

프로젝트 폴더에서 서버 시작:

```powershell
& Server/AzureSpeech/.venv/Scripts/python.exe Server/LocalRecording/server.py
```

`http://127.0.0.1:8093/health`에서 `ready: true`, `mock: false`를 확인합니다.
Unity에서 **Rehear → PC Recording → Use Local AI Server**를 누릅니다.
`Scene_00`부터 Play하여 PIN 입력 → 발표 → 질문 → 답변을 직접 진행합니다.
자동으로 씬이나 버튼을 실행하지 않습니다. 질문 생성에는 충분한 실제 발표 음성이 필요합니다.

## 종료

Unity Play를 멈춘 뒤 **Rehear → PC Recording → Restore Production Server**를 누릅니다.
서버 터미널에서 Ctrl+C로 종료합니다. 로컬 주소는 Editor 전용이며 일반 APK에 포함되지 않습니다.
