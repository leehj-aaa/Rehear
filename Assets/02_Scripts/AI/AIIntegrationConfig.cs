using UnityEngine;

[CreateAssetMenu(
    fileName = "AIIntegrationConfig",
    menuName = "Rehear/AI Integration Config")]
public class AIIntegrationConfig : ScriptableObject
{
    [Header("Server")]
    [Tooltip("AI 담당자에게 받을 HTTPS 서버 주소")]
    [SerializeField]
    private string baseUrl = "";

    [Header("Development")]
    [Tooltip("켜져 있으면 실제 서버를 호출하지 않고 가짜 응답을 사용합니다.")]
    [SerializeField]
    private bool useMockServer = true;

    [Tooltip("서버 응답을 기다릴 최대 시간(초)")]
    [SerializeField]
    private int requestTimeoutSeconds = 20;

    [Tooltip("마이크 음성을 나누어 전송할 간격(초)")]
    [SerializeField]
    private float audioChunkSeconds = 5f;

    public string BaseUrl => baseUrl.Trim().TrimEnd('/');
    public bool UseMockServer => useMockServer;
    public int RequestTimeoutSeconds => requestTimeoutSeconds;
    public float AudioChunkSeconds => audioChunkSeconds;

    public string SmartStartUrl => $"{BaseUrl}/smart-start";
    public string UpdateUrl => $"{BaseUrl}/update";

    public string GetSessionUrl(string sessionId)
    {
        return $"{BaseUrl}/sessions/{sessionId}";
    }

    public string DeleteSessionUrl(string sessionId)
    {
        return $"{BaseUrl}/sessions/{sessionId}";
    }

    private void OnValidate()
    {
        requestTimeoutSeconds = Mathf.Max(1, requestTimeoutSeconds);
        audioChunkSeconds = Mathf.Max(1f, audioChunkSeconds);
    }
}