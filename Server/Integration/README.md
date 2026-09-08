# 기존 ReHear 서버에 Azure·답변 기반 질문 연결

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
