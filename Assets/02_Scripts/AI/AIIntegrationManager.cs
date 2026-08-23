using UnityEngine;

public class AIIntegrationManager : MonoBehaviour
{
    [Header("API")]
    [SerializeField]
    private EvcApiClient apiClient;

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

    public bool HasActiveSession =>
        !string.IsNullOrWhiteSpace(sessionId) &&
        !string.IsNullOrWhiteSpace(sessionToken);

    public string SessionId => sessionId;
    public int CurrentStep => currentStep;

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

    public SmartStartAudience FindAudience(
        string agentId)
    {
        if (audiences == null)
            return null;

        foreach (SmartStartAudience audience
                 in audiences)
        {
            if (audience != null &&
                audience.agent_id == agentId)
            {
                return audience;
            }
        }

        return null;
    }

    [ContextMenu("Test - End AI Session")]
public void EndAiSession()
{
    if (apiClient == null)
    {
        Debug.LogError(
            "[AI] EvcApiClient가 연결되지 않았습니다."
        );

        return;
    }

    if (!HasActiveSession)
    {
        Debug.LogWarning(
            "[AI] 종료할 EVC 세션이 없습니다."
        );

        return;
    }

    if (isEndingSession)
    {
        Debug.LogWarning(
            "[AI] EVC 세션을 이미 종료 중입니다."
        );

        return;
    }

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

    isEndingSession = false;
    ClearAiSession();
}

private void HandleDeleteError(
    string error)
{
    isEndingSession = false;

    Debug.LogError(
        "[AI] EVC 세션 종료 실패\n" + error
    );
}

    private void ClearAiSession()
    {
        sessionId = "";
        sessionToken = "";
        currentStep = 0;
        audiences = null;
        isEndingSession = false;
    }
}