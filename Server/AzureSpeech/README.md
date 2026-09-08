> **현재 연결 대상은 기존 `4githu/sushisite` EVC 서버입니다.** 별도 gateway의 `app.py`는 초기 Azure 진단용이며, 답변 기반 다음 질문 생성을 지원하지 않습니다. Unity를 여기에 연결하면 다음 질문 결과 검증에서 중단됩니다. 운영 반영은 [기존 서버 통합 안내](../Integration/README.md)를 따르세요. `.env`의 실제 키는 로컬 진단용이며 Git에 포함하지 않습니다.

# ReHear Azure Speech 연결 (Quest APK)

이 폴더는 **새로 준비한 음성 게이트웨이**입니다. 기존 `rehear.chobab.app`에 배포된 API가 아닙니다. 2026-09-08 포털에서 PSA와 Azure for Students가 활성인 것을 확인했고, PSA 무료(F0)에서 6개 한국어 TTS와 합성 음성 STT 실호출을 통과했습니다. 기존 ReHear 서버 배포와 최종 평가 연결은 아직 남아 있습니다.

## 사용자 계정에서 확인할 것

1. Azure Portal의 **구독 → Azure for Students → 상태**를 확인합니다. 비활성 구독은 학생 갱신 자격을 재확인하거나 종량제로 전환해야 합니다. 리소스가 목록에 남아 있는 것만으로 호출 가능한 상태는 아닙니다. 결제 변경은 이 작업에서 수행하지 않았습니다.
2. 기존 **PSA → 개요**에서 활성 구독과 요금제를 확인합니다. 지역은 **Korea Central (`koreacentral`)**을 사용할 수 있습니다. 기본 구현은 **무료 F0를 지원하는 서버용 Speech SDK 연속 인식**입니다. 선택 사항인 Fast transcription만 S0가 필요합니다. 포털의 요금제는 변경하지 않았습니다.
3. **PSA → 키 및 엔드포인트**에서 키와 지역을 확인합니다. Fast 모드를 선택할 때만 사용자 지정 `https://…cognitiveservices.azure.com` 엔드포인트가 필요합니다. 실제 값을 서버 비밀 설정에만 입력합니다. 키를 Unity Inspector, Resources, StreamingAssets, Git 또는 채팅에 넣지 않습니다.
4. 리소스를 삭제/다시 만들기 전에 기존 구독과 리소스 상태를 먼저 확인합니다. `MySpeechSTT`와 `PSA`를 동시에 사용할 필요는 없습니다.

## 서버 실행

Python 3.12 기준. 로컬 테스트용 `.venv`에는 의존성을 설치했습니다. 다른 서버에서는 아래와 같이 설치합니다.

```sh
python -m venv .venv
# Linux
.venv/bin/pip install -r requirements.lock.txt
# Windows
.venv/Scripts/python.exe -m pip install -r requirements.lock.txt
```

`.env.example`은 설정 항목 목록이며 자동으로 읽지 않습니다. 배포 환경의 secret/environment 설정에 다음 값을 넣습니다.

| 서버 환경 변수 | 값 |
| --- | --- |
| AZURE_SPEECH_KEY | PSA 리소스 키 |
| AZURE_SPEECH_REGION | koreacentral |
| AZURE_STT_MODE | continuous (기본, F0 지원) 또는 fast (S0 필요) |
| AZURE_SPEECH_ENDPOINT | fast 모드에서만 필요한 사용자 지정 엔드포인트 |
| EVC_BASE_URL | https://rehear.chobab.app/odi/xreal_rehear/evc |
| SPEECH_DATA_DIR | 서버 전용 영구 저장 디렉터리 |

```sh
python -m uvicorn app:app --host 127.0.0.1 --port 8091 --workers 1 --no-access-log
python -m unittest -v test_app.py
```

HTTPS reverse proxy 뒤에 배포합니다. 파일 업로드 한도는 20MB 이상, upstream timeout은 720초 이상으로 설정합니다. 세션 토큰과 본문은 로그로 남기지 않습니다. 위 프로세스는 단일 worker용이며 여러 서버/worker로 확장할 때는 SQLite와 프로세스 내 중복 처리 잠금을 공유 DB/분산 잠금으로 교체해야 합니다. 답변 텍스트는 연구 데이터이므로 영구 저장소 접근권한·보존기간·삭제 정책을 서버 운영에 맞춰 설정합니다. 원본 응답 음성은 서버 DB에 보관하지 않습니다.

## Unity 설정과 동작

`Assets/Resources/AzureSpeechConfig.asset`의 **Bridge Base Url**에 배포한 음성 서버의 HTTPS 주소를 입력합니다. Azure 리소스 주소를 넣는 필드가 아닙니다. APK에서는 PC의 localhost에 접속할 수 없습니다.

- 질문은 서버의 기존 질문 목록에서 인덱스로 조회합니다. 기존 `X-EVC-Session-Token` 인증을 확인한 후 Azure TTS를 호출합니다.
- 웹에서 지정한 질문 수에 맞춰 청중을 미리 배정합니다. 청중 수 이내에서는 중복 없이, 한 세션 내에서는 동일한 배정을 유지합니다.
- 질문 음성 준비 → 선택된 청중 QS 손들기/내리기 한 번 → 기본 자세 블렌딩 완료 → 해당 청중 AudioSource에서 TTS 및 OVRLipSync → 응답 시작하기.
- 응답 시작하기에서 Android 마이크 권한 확인 → 녹음 시작. 1초간 마치기 연타 방지 → 응답 마치기에서 녹음 종료 → Azure STT 및 질문별 저장 확인 → 다음 질문/피드백.
- 일시정지 중 음성은 답변에서 제외합니다. 답변은 최대 10분 후 자동 제출합니다. 네트워크 실패 시 현재 메모리의 동일 WAV·request ID로 재시도하므로 중복 저장하지 않습니다. 앱 종료까지의 영구 오프라인 녹음 보관은 구현하지 않았습니다.
- 무음 인식 결과면 다시 녹음하기로 돌아갑니다. 실패를 성공으로 처리해서 다음 질문을 넘기지 않습니다.

**기존 피드백 생성 서버는 아직 이 저장소의 답변을 읽지 않습니다.** `/sessions/{session_id}/answers`를 기존 `/finish` 리포트 생성에 연결하거나, 이 모듈을 기존 서버 안으로 옮겨 답변 저장소를 공유해야 Q&A 응답이 최종 평가에 반영됩니다. 기존 서버 저장소에 접근하기 전까지 이 부분은 미완료입니다. 발표 본문 STT도 기존 EVC `/update` 서버가 담당하므로, 그 서버 내부의 음성 제공자는 별도로 Azure로 전환해야 합니다. F0는 STT 동시 요청 1개이므로 발표 STT와 답변 STT가 같은 리소스를 공유하면 서버에서 직렬화해야 합니다.

## 청중별 목소리 변경

Unity Project 창에서 `Assets/03_Prefabs/Audience/Presentation`의 프리팹을 선택하고, 루트의 **Audience Voice Profile → Voice** 드롭다운을 변경합니다. 좌석 ID가 아닌 프리팹에 저장되므로 자리가 바뀌어도 같은 목소리를 유지합니다. 목소리 파일을 APK에 설치하거나 Azure 리소스를 6개 만들 필요는 없습니다.

| 프리팹 | Azure 음성 |
| --- | --- |
| Aud_M_01 | ko-KR-InJoonNeural |
| Aud_M_02 | ko-KR-BongJinNeural |
| Aud_M_03 | ko-KR-GookMinNeural |
| Aud_W_01 | ko-KR-SunHiNeural |
| Aud_W_02 (베이지색 정장) | ko-KR-SoonBokNeural |
| Aud_W_03 | ko-KR-YuJinNeural |

Aud_W_03의 SeoHyeon은 사용자 청음 피드백에 따라 YuJin으로 교체했습니다. 여섯 청중은 각각 다른 음성을 사용합니다. 서버는 이전 클라이언트 호환을 위해 JiMin·SeoHyeon도 허용하지만 위 프리팹에는 배정하지 않습니다. 다른 음성을 추가하려면 Unity enum과 서버 `VOICES`를 함께 확장합니다. 재생속도·피치는 기본값입니다. 음성별 실제 제공 여부는 활성 리소스의 `/cognitiveservices/voices/list` 또는 Speech Studio에서 최종 확인합니다. [공식 한국어 음성 목록](https://learn.microsoft.com/ko-kr/azure/ai-services/speech-service/language-support).

## APK

- Android ARM64 + IL2CPP, Internet/RECORD_AUDIO 권한 사용. 실제 권한은 응답 시작 때 요청합니다.
- Speech SDK 1.51.2는 Python 서버의 가상환경에만 설치했습니다. Azure 키/네이티브 Speech DLL/SO를 APK에 넣지 않습니다. UnityWebRequest로 HTTPS/WAV를 처리합니다.
- 공식 Unity SDK는 1.44 이후 배포가 중단되어, 다운로드한 구형 SDK는 Assets에서 제외했습니다. 기존 Oculus LipSync는 유지합니다.
- 에디터 컴파일과 모의 테스트는 실제 Quest 마이크 권한·오디오 출력·립싱크 테스트를 대체하지 않습니다. 활성 계정과 HTTPS 서버 배포 후 개발 APK로 실기기 확인이 필요합니다.

## 공식 문서

- [학생 구독 비활성화 복구](https://learn.microsoft.com/en-us/azure/cost-management-billing/manage/azurestudents-subscription-disabled)
- [Speech 지역](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/regions)
- [Fast transcription](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/fast-transcription-create)
- [Speech 제한/F0 및 S0](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/speech-services-quotas-and-limits)
- [TTS REST](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/rest-text-to-speech)
- [Speech SDK 릴리스 노트](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/releasenotes)

## 실제 리소스 점검과 배포 준비

로컬 `.env`는 Git에서 제외했고 PSA 키를 출력하지 않고 저장했습니다. Python이 자동으로 읽지는 않습니다. PowerShell에서 아래 명령으로 현재 프로세스에만 적용합니다.

```powershell
Get-Content .env | ForEach-Object {
    if ($_ -match '^([A-Z_]+)=(.*)$') {
        [Environment]::SetEnvironmentVariable($matches[1], $matches[2], 'Process')
    }
}
./.venv/Scripts/python.exe check_connection.py --live
```

`--live`는 짧은 합성 문장으로 6개 TTS와 1개 STT를 호출합니다. 사용자 마이크는 사용하지 않고 결과 오디오도 저장하지 않습니다. F0는 무료 할당량을 사용하며, S0에서는 사용량 과금됩니다. 실제 확인 결과: 인증, 여섯 목소리의 PCM16 WAV, 한국어 STT 모두 PASS. 이는 Quest 실기기 및 기존 서버 평가 통합 테스트는 아닙니다.

컨테이너 배포 파일도 준비했습니다. 기존 서버의 HTTPS 프록시에 연결한 뒤 Unity의 Bridge Base Url을 해당 주소로 설정합니다. 로컬 PC에서 `docker compose up -d --build`만 실행해도 APK에 공개되는 것은 아닙니다. 현재 PC에는 Docker가 없어 이미지 빌드는 검증하지 못했습니다.

- `compose.yaml`: 서버 `.env` 사용, localhost:8091 바인딩, 답변 DB 영구 볼륨.
- `.dockerignore`: 키·테스트·연구 데이터를 빌드 컨텍스트에서 제외.
- 연속 STT는 오디오 길이에 따라 처리 시간이 길어질 수 있어 답변 저장 타임아웃은 720초입니다.
