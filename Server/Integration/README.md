# 기존 ReHear 서버에 Azure·답변 기반 질문 연결

## 2026-09-17 운영 API 확인

`https://rehear.chobab.app`에 음성 및 답변 기반 후속 질문 API가 배포되어 있습니다.
`check_production_qa.py`로 합성 발표 음성을 보내 운영 STT·평가, 질문 2개 생성,
질문 TTS, 답변 STT·저장, 답변 기반 다음 질문 교체까지 실제 호출을 통과했습니다.
검증은 PIN 없는 별도 세션을 만들고 마지막에 삭제했습니다.
결과: `Builds/ProductionQA/check-report.json`.

APK의 EVC 설정과 AzureSpeechConfig는 위 HTTPS 운영 서버를 사용합니다.
PC 촬영용 로컬 전환은 Editor에만 적용되며 일반 APK에는 적용되지 않습니다.
API 키는 서버에 유지합니다. 아래 배포 대기 내용은 9월 8일 당시 기록입니다.
이번 검증은 서버 API 검증이며, 새 APK의 실제 Quest 마이크·립싱크 검증은 별도입니다.

재검증(실제 API 사용량 발생):
`Server/AzureSpeech/.venv/Scripts/python.exe Server/Integration/check_production_qa.py`

## 2026-09-08 작업 기록

기존 서버: `https://github.com/4githu/sushisite`

수정 작업 브랜치: `dev/bora`

2026-09-08 서버 커밋 `9e115a6b53442b0a04c3bb68483acd13ec1ac0be`를 푸시했습니다.
검토 PR: https://github.com/4githu/sushisite/pull/10
서버 담당자의 검토·머지·배포가 남아 있습니다. main 브랜치는 직접 변경하지 않았습니다.

기존 `question_generation.py`의 OpenAI 연결을 재사용합니다. 사용자가 응답을 마치면
Azure STT 결과와 실제 문답 이력, 발표 내용, 다음 청중의 평가 상태를 전달해 다음
질문을 교체합니다. 꼬리 질문과 다른 주제의 관련 질문 모두 가능하며 총 질문 수는
추가하지 않습니다. Unity 버튼과 응답 결과 처리도 이 계약에 맞게 수정했습니다.

`azure-adaptive-qa.patch`는 기존 서버 기준 커밋
`42d424071d30efaec8cfc0d873974937787e8014`에 적용 가능한 패치입니다.
서버 브랜치 작업본은 현재 `D:/Github/ReHear/Builds/ServerReview/sushisite`에 있습니다.
서버 상세 설정·API·테스트 방법은 패치 안 `sushi-fast/odi/EVC/AZURE_QA.md`를 참고하세요.

검증: 기존 서버 및 신규 Q&A 테스트 29개, Unity 컴파일·응답 전환 검사 통과.
실제 PSA Azure 리소스로 합성 TTS → 연속 STT·단어 시간 정보 → 테스트용 LLM →
다음 질문 TTS를 확인했습니다. Quest APK에서 기존 6개 목소리의 OVR 입모양 반응과
마이크 캡처를 확인했습니다. 기기 진단 코드의 응답 UUID 형식 비교 오류는 수정했으나
수정 후 기기 응답 왕복은 재검증 전입니다. 실제 운영 LLM 호출과 서버 배포는
아직 검증하지 않았습니다. Unity 서버 주소도 운영 배포 확인 전까지 비워두었습니다.

키는 서버 환경변수로만 넣습니다. 로컬 Azure 진단용 `.env`는 무시된 파일이며 패치와
Git에 포함하지 않습니다. 이전 `Server/AzureSpeech/app.py`는 독립 진단용이므로 운영
질의응답 서버로 사용하지 마세요.
