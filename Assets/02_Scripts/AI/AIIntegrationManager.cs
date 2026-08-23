using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AIIntegrationManager : MonoBehaviour
{
    [Header("API")]
    [SerializeField]
    private EvcApiClient apiClient;

    [Header("마이크")]
    [SerializeField]    
    private PresentationMicrophoneRecorder
        microphoneRecorder;

    [Header("청중 명령")]
    [SerializeField]
    private AudienceCommandDispatcher
        audienceCommandDispatcher;

    [Header("실행 설정")]
    [SerializeField]
    private bool startAutomatically = true;

    [Header("Scene 02 단독 테스트용")]
    [SerializeField]
    private string fallbackTitle = "Re:hear 테스트 발표";

    [SerializeField]
    private string fallbackInterest = "중간";

    [SerializeField]
    private string fallbackExpertise = "중간";

    private string sessionId;
    private string sessionToken;
    private int currentStep;
    private SmartStartAudience[] audiences;
    private bool isEndingSession;
    private Action pendingEndCallback;

    public bool HasActiveSession =>
        !string.IsNullOrWhiteSpace(sessionId) &&
        !string.IsNullOrWhiteSpace(sessionToken);

    public string SessionId => sessionId;
    public int CurrentStep => currentStep;

    private readonly Queue<AudioChunk>
    pendingAudioChunks =
        new Queue<AudioChunk>();

    private bool isSendingUpdate;
    private float aiSessionStartedAt;

    private const int MaxUpdateRetries = 2;

    private class AudioChunk
    {
        public byte[] wavData;
        public float duration;
        public float clientTime;
        public float utteranceStart;
        public float utteranceEnd;
        public string requestId;
        public int retryCount;
    }

    private void Awake()
    {
        if (microphoneRecorder != null)
        {
            microphoneRecorder.ChunkRecorded +=
                HandleAudioChunkRecorded;

        }
    }

    private void OnDestroy()
    {
        if (microphoneRecorder != null)
        {
            microphoneRecorder.ChunkRecorded -=
                HandleAudioChunkRecorded;

            microphoneRecorder.StopRecording();
        }
    }

    private void Start()
    {
        if (startAutomatically)
            StartAiSession();
    }

    public void StartAiSession()
    {
        if (apiClient == null)
        {
            Debug.LogError(
                "[AI] EvcApiClient가 연결되지 않았습니다."
            );

            return;
        }

        if (HasActiveSession)
        {
            Debug.LogWarning(
                "[AI] 이미 실행 중인 EVC 세션이 있습니다."
            );

            return;
        }

        string title;
        string interest;
        string expertise;

        if (RuntimeSessionData.IsLoaded)
        {
            title =
                RuntimeSessionData.PresentationTitle;

            interest =
                RuntimeSessionData.AudienceInterest;

            expertise =
                RuntimeSessionData.AudienceExpertise;
        }
        else
        {
            title = fallbackTitle;
            interest = fallbackInterest;
            expertise = fallbackExpertise;

            Debug.LogWarning(
                "[AI] RuntimeSessionData가 없어 " +
                "Scene 02 테스트용 기본값을 사용합니다."
            );
        }

        Debug.Log(
            "[AI] Smart Start 요청" +
            "\n발표 제목: " + title +
            "\n관심도: " + interest +
            "\n전문성: " + expertise
        );

        StartCoroutine(
            apiClient.StartSmartSession(
                title,
                interest,
                expertise,
                HandleSmartStartSuccess,
                HandleSmartStartError
            )
        );
    }

    private void HandleSmartStartSuccess(
        SmartStartResponse response)
    {
        sessionId = response.session_id;
        sessionToken = response.session_token;
        currentStep = response.step;
        audiences = response.audiences;
        aiSessionStartedAt =
            Time.realtimeSinceStartup;

        pendingAudioChunks.Clear();
        isSendingUpdate = false;

        if (audienceCommandDispatcher != null)
        {
            audienceCommandDispatcher
                .ClearSessionHistory();
        }

        if (microphoneRecorder != null)
        {
            microphoneRecorder.StartRecording();
        }
        else
        {
            Debug.LogError(
                "[AI] PresentationMicrophoneRecorder가 " +
                "연결되지 않았습니다."
            );
        }

        int audienceCount =
            audiences != null
                ? audiences.Length
                : 0;

        Debug.Log(
            "[AI] Smart Start 성공" +
            "\nAPI 버전: " + response.api_version +
            "\nSession ID: " + sessionId +
            "\nStep: " + currentStep +
            "\n청중 수: " + audienceCount
        );

        // sessionToken은 로그에 출력하면 안 된다.

        if (audienceCount != 6)
        {
            Debug.LogWarning(
                "[AI] 응답의 청중 수가 6명이 아닙니다."
            );
        }
    }

    private void HandleSmartStartError(
        string error)
    {
        ClearAiSession();

        Debug.LogError(
            "[AI] Smart Start 실패\n" + error
        );
    }


    private void HandleAudioChunkRecorded(
    byte[] wavData,
    float duration)
{
    if (!HasActiveSession)
    {
        Debug.LogWarning(
            "[AI] 활성 세션이 없어 음성을 전송하지 않습니다."
        );

        return;
    }

    float endTime =
        Time.realtimeSinceStartup -
        aiSessionStartedAt;

    AudioChunk chunk =
        new AudioChunk
        {
            wavData = wavData,
            duration = duration,
            clientTime = endTime,
            utteranceStart =
                Mathf.Max(0f, endTime - duration),
            utteranceEnd = endTime,
            requestId =
                Guid.NewGuid().ToString(),
            retryCount = 0
        };

    pendingAudioChunks.Enqueue(chunk);

    Debug.Log(
        "[AI] 음성 조각 대기열 추가" +
        "\n현재 대기 수: " +
        pendingAudioChunks.Count
    );

    TrySendNextAudioChunk();
}

private void TrySendNextAudioChunk()
{
    if (isSendingUpdate ||
        !HasActiveSession ||
        pendingAudioChunks.Count == 0)
    {
        return;
    }

    AudioChunk chunk =
        pendingAudioChunks.Peek();

    isSendingUpdate = true;

    Debug.Log(
        "[AI] Update 요청" +
        "\nExpected Step: " + currentStep +
        "\n음성 구간: " +
        chunk.utteranceStart.ToString("F1") +
        " ~ " +
        chunk.utteranceEnd.ToString("F1") +
        "초"
    );

    StartCoroutine(
        apiClient.UpdateSession(
            sessionId,
            sessionToken,
            chunk.requestId,
            currentStep,
            chunk.clientTime,
            chunk.utteranceStart,
            chunk.utteranceEnd,
            chunk.wavData,
            GetServerLanguage(),
            HandleUpdateSuccess,
            HandleUpdateError
        )
    );
}

private void HandleUpdateSuccess(
    EvcUpdateResponse response)
{
    if (pendingAudioChunks.Count > 0)
        pendingAudioChunks.Dequeue();

    isSendingUpdate = false;
    currentStep = response.step;

    int commandCount =
        response.commands != null
            ? response.commands.Length
            : 0;

    Debug.Log(
    "[AI] Update 성공" +
    "\nRequest ID: " + response.request_id +
    "\nStep: " + currentStep +
    "\n인식 문장: " + response.latest_speech +
    "\nNo-op 이유: " + response.no_op_reason +
    "\n청중 명령 수: " + commandCount +
    "\n남은 음성 조각: " +
    pendingAudioChunks.Count
);    

    if (response.warnings != null)
    {
        foreach (string warning
                 in response.warnings)
        {
            Debug.LogWarning(
                "[AI 서버 경고] " + warning
            );
        }
    }

    if (response.commands == null ||
    response.commands.Length == 0)
{
    Debug.Log(
        "[AI] 이번 Update에는 청중 명령이 없습니다." +
        "\nNo-op 이유: " +
        response.no_op_reason
    );
}
else
{
    foreach (
        UnityAudienceCommand command
        in response.commands)
    {
        if (command == null)
            continue;

        Debug.Log(
            "[AI 청중 명령]" +
            "\nAgent ID: " +
            command.agent_id +
            "\nAction ID: " +
            command.action_id +
            "\nVariation ID: " +
            command.selected_variation_id +
            "\nLayer: " +
            command.layer +
            "\n시작 지연: " +
            command.start_time.ToString("F2") +
            "초" +
            "\n재생 시간: " +
            command.duration.ToString("F2") +
            "초" +
            "\n강도: " +
            command.intensity.ToString("F2") +
            "\nBlend Mode: " +
            command.blend_mode
        );
    }
}
        if (audienceCommandDispatcher != null &&
        response.commands != null &&
        response.commands.Length > 0)
    {
        float currentSessionTime =
            Time.realtimeSinceStartup -
            aiSessionStartedAt;

        audienceCommandDispatcher
            .DispatchCommands(
                response.request_id,
                response.commands,
                currentSessionTime
            );
    }

    if (response.audiences != null)
{
    foreach (
        UpdateAudience audience
        in response.audiences)
    {
        if (audience == null)
            continue;

        string coreVariation =
            audience.core_behavior != null
                ? audience
                    .core_behavior
                    .variation_id
                : "없음";

        string overlayVariation =
            audience.action_overlay != null
                ? audience
                    .action_overlay
                    .variation_id
                : "없음";

        Debug.Log(
            "[AI 청중 상태]" +
            "\nAgent: " +
            audience.agent_id +
            "\nE: " +
            audience.state?.E.ToString("F3") +
            "\nV: " +
            audience.state?.V.ToString("F3") +
            "\nC: " +
            audience.state?.C.ToString("F3") +
            "\n방향: " +
            audience.direction +
            "\nCore: " +
            coreVariation +
            "\nOverlay: " +
            overlayVariation
        );
    }
}

    TrySendNextAudioChunk();
}

private void HandleUpdateError(
    string error)
{
    isSendingUpdate = false;

    if (pendingAudioChunks.Count == 0)
    {
        Debug.LogError(error);
        return;
    }

    AudioChunk chunk =
        pendingAudioChunks.Peek();

    chunk.retryCount++;

    if (chunk.retryCount <= MaxUpdateRetries)
    {
        Debug.LogWarning(
            "[AI] Update 전송 실패. 같은 요청으로 재시도합니다." +
            "\n재시도: " +
            chunk.retryCount +
            "/" +
            MaxUpdateRetries +
            "\n" +
            error
        );

        StartCoroutine(
            RetryUpdateAfterDelay()
        );

        return;
    }

    pendingAudioChunks.Dequeue();

    Debug.LogError(
        "[AI] Update 최종 실패. 해당 음성 조각을 건너뜁니다." +
        "\n" + error
    );

    TrySendNextAudioChunk();
}

private IEnumerator RetryUpdateAfterDelay()
{
    yield return new WaitForSecondsRealtime(1f);
    TrySendNextAudioChunk();
}

private string GetServerLanguage()
{
    if (!RuntimeSessionData.IsLoaded)
        return "ko-KR";

    string language =
        RuntimeSessionData.UsedLanguage;

    if (string.IsNullOrWhiteSpace(language))
        return "ko-KR";

    if (language.Contains("한국") ||
        language.Equals(
            "ko",
            StringComparison.OrdinalIgnoreCase))
    {
        return "ko-KR";
    }

    if (language.Contains("영어") ||
        language.Equals(
            "en",
            StringComparison.OrdinalIgnoreCase))
    {
        return "en-US";
    }

    return language;
}

[ContextMenu("Test - End AI Session")]
public void EndAiSession()
{
    EndAiSessionAndThen(null);
}

public void EndAiSessionAndThen(
    Action onComplete)
{
    if (apiClient == null)
    {
        Debug.LogError(
            "[AI] EvcApiClient가 연결되지 않았습니다."
        );

        onComplete?.Invoke();
        return;
    }

    if (!HasActiveSession)
    {
        Debug.LogWarning(
            "[AI] 종료할 EVC 세션이 없습니다."
        );

        onComplete?.Invoke();
        return;
    }

    if (isEndingSession)
    {
        Debug.LogWarning(
            "[AI] EVC 세션을 이미 종료 중입니다."
        );

        return;
    }

    pendingEndCallback = onComplete;

    if (microphoneRecorder != null)
        microphoneRecorder.StopRecording();

    isEndingSession = true;

    StartCoroutine(
        apiClient.DeleteSession(
            sessionId,
            sessionToken,
            HandleDeleteSuccess,
            HandleDeleteError
        )
    );
}

private void HandleDeleteSuccess()
{
    Debug.Log(
        "[AI] EVC 세션 종료 성공"
    );

    Action callback =
        pendingEndCallback;

    pendingEndCallback = null;

    ClearAiSession();

    callback?.Invoke();
}

private void HandleDeleteError(
    string error)
{
    Debug.LogError(
        "[AI] EVC 세션 종료 실패\n" +
        error
    );

    Action callback =
        pendingEndCallback;

    pendingEndCallback = null;

    // 서버 종료가 실패하더라도 사용자가
    // 피드백 화면으로 이동하지 못하게 막지는 않는다.
    ClearAiSession();

    callback?.Invoke();
}

    private void ClearAiSession()
    {
        if (microphoneRecorder != null)
            microphoneRecorder.StopRecording();

        sessionId = "";
        sessionToken = "";
        currentStep = 0;
        audiences = null;
        isEndingSession = false;
        pendingEndCallback = null;

        pendingAudioChunks.Clear();
        isSendingUpdate = false;
        aiSessionStartedAt = 0f;
    }
}