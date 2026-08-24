using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.Globalization;

public class EvcApiClient : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField]
    private AIIntegrationConfig config;

    public AIIntegrationConfig Config => config;

    public IEnumerator StartSmartSession(
        string presentationTitle,
        string topicInterest,
        string priorKnowledge,
        Action<SmartStartResponse> onSuccess,
        Action<string> onError)
    {
        if (config == null)
        {
            onError?.Invoke("AIIntegrationConfig가 연결되지 않았습니다.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(presentationTitle))
        {
            onError?.Invoke("발표 제목이 비어 있습니다.");
            yield break;
        }

        if (config.UseMockServer)
        {
            yield return new WaitForSeconds(0.5f);

            SmartStartResponse mockResponse = CreateMockSmartStartResponse(
                presentationTitle,
                topicInterest,
                priorKnowledge);

            onSuccess?.Invoke(mockResponse);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(config.BaseUrl))
        {
            onError?.Invoke("AI 서버 Base URL이 비어 있습니다.");
            yield break;
        }

        List<IMultipartFormSection> formData =
            new List<IMultipartFormSection>
            {
                new MultipartFormDataSection(
                    "presentation_title",
                    presentationTitle),

                new MultipartFormDataSection(
                    "topic_interest",
                    NormalizeLevel(topicInterest)),

                new MultipartFormDataSection(
                    "prior_knowledge",
                    NormalizeLevel(priorKnowledge))
            };

        using UnityWebRequest request =
            UnityWebRequest.Post(config.SmartStartUrl, formData);

        request.timeout = config.RequestTimeoutSeconds;

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            string message =
                $"Smart Start 실패\n" +
                $"HTTP: {request.responseCode}\n" +
                $"오류: {request.error}\n" +
                $"응답: {request.downloadHandler.text}";

            onError?.Invoke(message);
            yield break;
        }

        SmartStartResponse response;

        try
        {
            response = JsonUtility.FromJson<SmartStartResponse>(
                request.downloadHandler.text);
        }
        catch (Exception exception)
        {
            onError?.Invoke(
                $"Smart Start 응답 JSON 변환 실패: {exception.Message}");

            yield break;
        }

        if (response == null ||
            string.IsNullOrWhiteSpace(response.session_id))
        {
            onError?.Invoke(
                "Smart Start 응답에 session_id가 없습니다.");

            yield break;
        }

        if (!IsSupportedApiVersion(response.api_version))
        {
            onError?.Invoke(
                $"지원하지 않는 API 버전입니다: {response.api_version}");

            yield break;
        }

        // session_token은 보안 정보이므로 로그로 출력하지 않는다.
        onSuccess?.Invoke(response);
    }
public IEnumerator UpdateSession(
    string sessionId,
    string sessionToken,
    string requestId,
    int expectedStep,
    float clientTimeSeconds,
    float utteranceStartSeconds,
    float utteranceEndSeconds,
    byte[] wavData,
    string language,
    Action<EvcUpdateResponse> onSuccess,
    Action<string> onError)
{
    if (config == null)
    {
        onError?.Invoke(
            "AIIntegrationConfig가 연결되지 않았습니다."
        );

        yield break;
    }

    if (string.IsNullOrWhiteSpace(sessionId) ||
        string.IsNullOrWhiteSpace(sessionToken))
    {
        onError?.Invoke(
            "활성화된 AI 세션 정보가 없습니다."
        );

        yield break;
    }

    if (wavData == null || wavData.Length == 0)
    {
        onError?.Invoke(
            "전송할 WAV 음성 데이터가 없습니다."
        );

        yield break;
    }

    if (config.UseMockServer)
    {
        yield return new WaitForSeconds(0.5f);

        EvcUpdateResponse mockResponse =
            new EvcUpdateResponse
            {
                api_version = "2.0",
                request_id = requestId,
                session_id = sessionId,
                step = expectedStep + 1,
                accepted_client_time_s =
                    clientTimeSeconds,
                latest_speech =
                    "Mock 음성 분석 결과",
                current_slide_index = 0,
                commands =
                    Array.Empty<UnityAudienceCommand>(),
                warnings = Array.Empty<string>()
            };

        onSuccess?.Invoke(mockResponse);
        yield break;
    }

    string FloatText(float value)
    {
        return value.ToString(
            "0.###",
            CultureInfo.InvariantCulture
        );
    }

    List<IMultipartFormSection> formData =
        new List<IMultipartFormSection>
        {
            new MultipartFormDataSection(
                "session_id",
                sessionId
            ),

            new MultipartFormDataSection(
                "request_id",
                requestId
            ),

            new MultipartFormDataSection(
                "expected_step",
                expectedStep.ToString(
                    CultureInfo.InvariantCulture
                )
            ),

            new MultipartFormDataSection(
                "client_time_s",
                FloatText(clientTimeSeconds)
            ),

            new MultipartFormDataSection(
                "utterance_start_s",
                FloatText(utteranceStartSeconds)
            ),

            new MultipartFormDataSection(
                "utterance_end_s",
                FloatText(utteranceEndSeconds)
            ),

            new MultipartFormFileSection(
                "audio",
                wavData,
                $"audio_{expectedStep + 1:000}.wav",
                "audio/wav"
            )
        };

    if (!string.IsNullOrWhiteSpace(language))
    {
        formData.Add(
            new MultipartFormDataSection(
                "language",
                language
            )
        );
    }

    using UnityWebRequest request =
        UnityWebRequest.Post(
            config.UpdateUrl,
            formData
        );

    request.timeout =
        config.RequestTimeoutSeconds;

    request.SetRequestHeader(
        "X-EVC-Session-Token",
        sessionToken
    );

    yield return request.SendWebRequest();

    if (request.result !=
        UnityWebRequest.Result.Success)
    {
        string responseText =
            request.downloadHandler != null
                ? request.downloadHandler.text
                : "";

        onError?.Invoke(
            "AI Update 실패" +
            "\nHTTP: " + request.responseCode +
            "\n오류: " + request.error +
            "\n응답: " + responseText
        );

        yield break;
    }

    EvcUpdateResponse response;

    try
    {
        response =
            JsonUtility.FromJson<EvcUpdateResponse>(
                request.downloadHandler.text
            );
    }
    catch (Exception exception)
    {
        onError?.Invoke(
            "Update 응답 JSON 변환 실패: " +
            exception.Message
        );

        yield break;
    }

    if (response == null)
    {
        onError?.Invoke(
            "Update 응답이 비어 있습니다."
        );

        yield break;
    }

    if (!IsSupportedApiVersion(
        response.api_version))
    {
        onError?.Invoke(
            "지원하지 않는 API 버전입니다: " +
            response.api_version
        );

        yield break;
    }

    onSuccess?.Invoke(response);
}
    public IEnumerator DeleteSession(
    string sessionId,
    string sessionToken,
    Action onSuccess,
    Action<string> onError)
{
    if (config == null)
    {
        onError?.Invoke(
            "AIIntegrationConfig가 연결되지 않았습니다."
        );

        yield break;
    }

    if (string.IsNullOrWhiteSpace(sessionId))
    {
        onError?.Invoke(
            "종료할 session_id가 없습니다."
        );

        yield break;
    }

    if (string.IsNullOrWhiteSpace(sessionToken))
    {
        onError?.Invoke(
            "종료할 session_token이 없습니다."
        );

        yield break;
    }

    if (config.UseMockServer)
    {
        yield return new WaitForSeconds(0.2f);
        onSuccess?.Invoke();
        yield break;
    }

    using UnityWebRequest request =
        UnityWebRequest.Delete(
            config.DeleteSessionUrl(sessionId)
        );

    request.timeout =
        config.RequestTimeoutSeconds;

    request.SetRequestHeader(
        "X-EVC-Session-Token",
        sessionToken
    );

    yield return request.SendWebRequest();

    if (request.result !=
        UnityWebRequest.Result.Success)
    {
        string message =
            $"AI 세션 종료 실패\n" +
            $"HTTP: {request.responseCode}\n" +
            $"오류: {request.error}\n" +
            $"응답: {request.downloadHandler?.text}";

        onError?.Invoke(message);
        yield break;
    }

    // 정상 응답은 HTTP 204 No Content이다.
    onSuccess?.Invoke();
}

    private SmartStartResponse CreateMockSmartStartResponse(
        string presentationTitle,
        string topicInterest,
        string priorKnowledge)
    {
        SmartStartAudience[] audiences =
            new SmartStartAudience[6];

        string[] rows =
        {
            "front", "front",
            "middle", "middle",
            "rear", "rear"
        };

        string[] seats =
        {
            "left", "right",
            "left", "right",
            "left", "right"
        };

        for (int index = 0; index < audiences.Length; index++)
        {
            audiences[index] = new SmartStartAudience
            {
                agent_id = $"audience_{index + 1:00}",

                profile = new AudienceProfile
                {
                    row = rows[index],
                    seat = seats[index],
                    has_laptop = index == 1 || index == 4,

                    responsiveness = 0.45f + index * 0.03f,
                    expressivity = 0.40f + index * 0.025f,
                    critical_bias = 0.35f + index * 0.02f,

                    channel_preference = new ChannelPreference
                    {
                        Face = 0.3f,
                        Body = 0.3f,
                        GazeHead = 0.4f
                    }
                },

                state = new EvcState
                {
                    E = 0.01f,
                    V = 0f,
                    C = 0.49f
                }
            };
        }

        return new SmartStartResponse
        {
            api_version = "2.0",
            session_id = Guid.NewGuid().ToString(),

            // Mock 전용 가짜 토큰이다. 실제 토큰은 로그에 출력하지 않는다.
            session_token = "mock-session-token",

            seed = 2026,
            presentation_title = presentationTitle,

            initial_evc_state = new EvcState
            {
                E = 0.01f,
                V = 0f,
                C = 0.49f
            },

            topic_interest = LevelToFloat(topicInterest),
            prior_knowledge = LevelToFloat(priorKnowledge),

            audiences = audiences,

            step = 0,
            expires_in_s = 7200,
            slide_count = 0
        };
    }

    private string NormalizeLevel(string value)
    {
        switch (value?.Trim())
        {
            case "낮음":
                return "low";

            case "보통":
            case "중간":
                return "middle";

            case "높음":
                return "high";

            case "low":
            case "middle":
            case "high":
                return value.Trim();

            default:
                return "middle";
        }
    }

    private float LevelToFloat(string value)
    {
        switch (NormalizeLevel(value))
        {
            case "low":
                return 0.25f;

            case "high":
                return 0.75f;

            default:
                return 0.5f;
        }
    }

    private bool IsSupportedApiVersion(string apiVersion)
    {
        if (string.IsNullOrWhiteSpace(apiVersion))
        {
            return false;
        }

        string majorVersion = apiVersion.Split('.')[0];
        return majorVersion == "2";
    }
}