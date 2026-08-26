# Rehear Unity EVC pipeline

이 폴더는 화면 코드에서 네트워크를 분리한 EVC 런타임 계층이다. 구현 기준은
`Assets/codex_md/unity-ai-pipeline-integration-spec.md`이며, 계약이 없는 값은 임의로
채우지 않는다.

## 현재 연결 방법

1. `Create > Rehear > EVC Environment Config`로 환경별 설정 에셋을 만든다. 개발 외
   환경은 HTTPS URL만 허용한다. URL에는 `/odi/xreal_rehear/evc` 앞의 서버 기준 주소만
   입력한다.
2. `Create > Rehear > Audio Capture Policy`로 녹음 정책 에셋을 만든다. 자동 분할 주기는
   제품 결정 전이므로 구현하지 않았고, pause·slide transition·종료 경계에서만 명시적으로
   flush한다.
3. 권위 있는 `clip_pool.json`을 프로젝트에 넣고 `AudienceActionRegistry`를 만든 뒤 각
   `action_id`를 Animator state/clip에 매핑한다. JSON TextAsset을 선택하고
   `Rehear > EVC > Validate Selected Clip Pool`을 실행하면 누락을 검사할 수 있다.
4. Presentation 씬에 `AudienceReactionCoordinator`, `AudioSegmentCapture`,
   `PresentationFlowController`가 `EVC Pipeline` 오브젝트로 배치되어 있다. 위 에셋과 agent별
   action registry/player를 연결한 뒤 `PresentationController.useEvcPipeline`을 켠다. 서버
   URL·오디오 정책·action registry가 없는 현재 저장소에서는 이 게이트가 꺼져 있어 기존
   로컬 발표 흐름을 유지한다.

## 청중 ID 배치

| Scene object | agent ID |
| --- | --- |
| `Aud_M_01` | `audience_01` |
| `Aud_M_02` | `audience_02` |
| `Aud_M_03` | `audience_03` |
| `Aud_W_01` | `audience_04` |
| `Aud_W_02` | `audience_05` |
| `Aud_W_03` | `audience_06` |

두 개의 빈 중복 `RandomAudienceAnimator`는 제거했다. `Aud_M_02`의 기존 랜덤 컴포넌트는
idle clip이 없어 명시적으로 비활성화했다. 서버 모드에서는 나머지 랜덤 컴포넌트도
`AudienceAgent.SetServerMode(true)`에 의해 비활성화된다.

## 테스트와 검증

Unity Test Runner에서 `Rehear.Evc.Tests.EditMode`와 `Rehear.Evc.Tests.PlayMode`를 실행한다.
메뉴 `Rehear > EVC > Validate Project`는 Presentation 씬의 Missing Script와 여섯 agent ID,
파이프라인 컴포넌트 연결, 레지스트리 기본 무결성을 검사한다.

CLI 실행 예시는 다음과 같다.

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.9f1\Editor\Unity.exe' `
  -batchmode -nographics -projectPath $PWD `
  -runTests -testPlatform EditMode -assemblyNames Rehear.Evc.Tests.EditMode `
  -testResults "$PWD\Logs\EvcEditModeResults.xml" `
  -logFile "$PWD\Logs\EvcEditMode.log"
```

PlayMode는 `-testPlatform PlayMode -assemblyNames Rehear.Evc.Tests.PlayMode`와 별도 결과/로그
파일명을 사용한다. Unity 6000.3에서는 Test Framework가 완료 후 종료하므로 `-quit`을 함께
지정하지 않는다. `-quit`이 테스트 콜백보다 먼저 처리되면 결과 XML이 생성되지 않을 수 있다.

같은 프로젝트를 다른 Editor가 열고 있으면 Test Runner는 실행되지 않는다. 먼저 해당 Editor를
닫거나 별도 작업 복제본에서 실행한다.

## 일시정지와 앱 복귀 정책

- pause 요청 시 현재 마이크 링버퍼를 먼저 stop/flush하고 발표 시계를 멈춘 뒤 해당 구간을
  직렬 update 큐에 넣는다.
- 발표 시계가 pause 상태인 동안 아직 시작하지 않은 청중 명령은 실행하지 않는다. resume 뒤
  같은 절대 발표 시각을 기준으로 계속 예약한다.
- Android/Quest 마이크 권한은 허용/거부 상태를 노출하며 재시도할 수 있다. 설정 앱에서 권한을
  바꾸고 복귀하면 포커스 이벤트에서 상태를 다시 읽는다.
- 앱 background/scene unload에서는 마이크, 예약 명령, update 수명 토큰을 취소한다.

## 오류와 복구

| 분류 | 사용자 안내/복구 | 자동 재시도 |
| --- | --- | --- |
| 취소 | 현재 비동기 작업을 종료하고 파괴된 UI에 접근하지 않음 | 없음 |
| timeout/offline | 네트워크 확인 또는 다시 시도 안내; 같은 request ID 유지 | 제한된 backoff |
| 401 | 세션 만료 및 발표 재시작 안내 | 없음 |
| 409 `step_conflict` | session GET으로 서버 step을 우선 복구하고 오디오는 자동 재전송하지 않음 | 없음 |
| 404 questions not generated | 종료 의도가 확인된 경우에만 generate | 1회 흐름 전환 |
| 409 questions generating | 짧은 backoff 뒤 GET | 제한됨 |
| 413/415/422 | 녹음 크기·형식·발표 데이터 안내 | 없음 |
| 429/502/기타 5xx | 잠시 후 다시 시도 안내 | 제한된 backoff |

로그는 request ID, step, 오류 분류, queue 길이, 처리 시간, command drop 사유만 남긴다. 서버가
보낸 임의 오류 문자열은 허용 목록 밖이면 `unrecognized`로 기록하며 token, 전사 원문,
오디오 byte는 기록하지 않는다.

`Tests/Fixtures`는 명세에서 만든 비밀 제거 fixture이며 실제 서버 캡처가 아니다. 실제 서버
fixture/OpenAPI가 제공되면 동일 계약 테스트 자산을 교체 또는 추가해야 한다.

## 의도적으로 남은 게이트

- Firebase 발표 제목은 루트의 명시적 `presentation_title`만 읽는다. 원본 필드 결정 전에는
  PIN이나 임의 문자열로 추정하지 않는다.
- Firebase 이미지 URL은 표시용 슬라이드이며 smart-start `slide_file`로 전송하지 않는다.
- session token은 메모리에만 두며 `PlayerPrefs`에 저장하지 않는다.
- `clip_pool.json` 원본과 실제 Animator/BlendShape/IK 매핑이 없으므로 명령 자산 연결은 미완료다.
- `/finish` 요청/응답 계약이 없어 Feedback 리포트 연동은 구현하지 않았다.
- 개발/스테이징/운영 URL과 TLS 운영 정책이 없어 환경 설정 에셋은 아직 만들지 않았다.
- 오디오 자동 분할 길이는 제품 정책이 없어 하드코딩하지 않았고 pause, slide transition, 종료
  경계에서만 flush한다.
- `ScriptScroller.pageIndicatorText`에 대응하는 Presentation UI가 없어 null-safe 미사용 상태를
  유지한다.
- `MainThreadDispatcher`는 Intro 씬의 기존 참조 보존을 위해 Legacy로 유지한다.
