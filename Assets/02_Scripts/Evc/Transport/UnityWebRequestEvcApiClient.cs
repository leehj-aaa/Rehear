using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Rehear.Evc.Config;
using Rehear.Evc.Contracts;
using UnityEngine;
using UnityEngine.Networking;

namespace Rehear.Evc.Transport
{
    public sealed class UnityWebRequestEvcApiClient : IEvcApiClient
    {
        private const string SessionTokenHeader = "X-EVC-Session-Token";
        private readonly EvcEnvironmentConfig config;

        public UnityWebRequestEvcApiClient(EvcEnvironmentConfig config)
        {
            this.config = config != null ? config : throw new ArgumentNullException(nameof(config));
            if (!config.TryValidate(out var error))
                throw new ArgumentException(error, nameof(config));
        }

        public async Task<SmartStartResponse> StartSessionAsync(
            StartSessionRequest request,
            CancellationToken cancellationToken)
        {
            ValidateStartRequest(request);
            var sections = new List<IMultipartFormSection>
            {
                Field("presentation_title", request.presentation_title)
            };

            AddOptionalField(sections, "topic_interest", request.topic_interest);
            AddOptionalField(sections, "prior_knowledge", request.prior_knowledge);
            AddOptionalField(sections, "pre_session_pin", request.pre_session_pin);
            if (request.seed.HasValue)
                sections.Add(Field("seed", request.seed.Value.ToString(CultureInfo.InvariantCulture)));
            if (request.slide_file != null && request.slide_file.IsValid)
            {
                sections.Add(new MultipartFormFileSection(
                    "slide_file",
                    request.slide_file.bytes,
                    request.slide_file.file_name,
                    request.slide_file.mime_type));
            }

            using (var webRequest = UnityWebRequest.Post(config.BuildApiUrl("/smart-start"), sections))
            {
                Configure(webRequest, null);
                return await SendAndReadAsync<SmartStartResponse>(webRequest, cancellationToken);
            }
        }

        public async Task<EvcUpdateResponse> SendSegmentAsync(
            EvcSegmentRequest request,
            CancellationToken cancellationToken)
        {
            ValidateSegmentRequest(request);
            var sections = new List<IMultipartFormSection>
            {
                Field("session_id", request.session_id),
                Field("request_id", request.request_id),
                Field("expected_step", request.expected_step.ToString(CultureInfo.InvariantCulture)),
                Field("client_time_s", request.client_time_s.ToString("R", CultureInfo.InvariantCulture)),
                Field("current_slide_index", Math.Max(0, request.current_slide_index).ToString(CultureInfo.InvariantCulture)),
                Field("language", string.IsNullOrWhiteSpace(request.language) ? "ko-KR" : request.language),
                new MultipartFormFileSection(
                    "audio",
                    request.audio.bytes,
                    request.audio.file_name,
                    request.audio.mime_type)
            };

            AddOptionalField(sections, "utterance_position", request.utterance_position);

            using (var webRequest = UnityWebRequest.Post(config.BuildApiUrl("/update"), sections))
            {
                Configure(webRequest, request.session_token);
                var response = await SendAndReadAsync<EvcUpdateResponse>(webRequest, cancellationToken);
                if (response.commands == null)
                    response.commands = Array.Empty<UnityCommandDto>();
                return response;
            }
        }

        public async Task<SessionResponse> GetSessionAsync(
            string sessionId,
            string token,
            CancellationToken cancellationToken)
        {
            ValidateProtectedRequest(sessionId, token);
            using (var webRequest = UnityWebRequest.Get(
                       config.BuildApiUrl("/sessions/" + UnityWebRequest.EscapeURL(sessionId))))
            {
                Configure(webRequest, token);
                return await SendAndReadAsync<SessionResponse>(webRequest, cancellationToken);
            }
        }

        public async Task<QuestionListResponse> GenerateQuestionsAsync(
            string sessionId,
            string token,
            string requestId,
            int count,
            CancellationToken cancellationToken)
        {
            ValidateProtectedRequest(sessionId, token);
            if (string.IsNullOrWhiteSpace(requestId))
                throw new ArgumentException("Question request id is required.", nameof(requestId));
            if (count < 1 || count > 5)
                throw new ArgumentOutOfRangeException(nameof(count), "Question count must be between 1 and 5.");

            var body = JsonUtility.ToJson(new QuestionGenerationRequest
            {
                request_id = requestId,
                question_count = count
            });

            using (var webRequest = CreateJsonPost(
                       config.BuildApiUrl("/sessions/" + UnityWebRequest.EscapeURL(sessionId) + "/questions/generate"),
                       body))
            {
                Configure(webRequest, token);
                return await SendAndReadAsync<QuestionListResponse>(webRequest, cancellationToken);
            }
        }

        public async Task<QuestionListResponse> GetQuestionsAsync(
            string sessionId,
            string token,
            CancellationToken cancellationToken)
        {
            ValidateProtectedRequest(sessionId, token);
            using (var webRequest = UnityWebRequest.Get(
                       config.BuildApiUrl("/sessions/" + UnityWebRequest.EscapeURL(sessionId) + "/questions")))
            {
                Configure(webRequest, token);
                return await SendAndReadAsync<QuestionListResponse>(webRequest, cancellationToken);
            }
        }

        public async Task<ReportFinishResponse> FinishSessionAsync(
            string sessionId,
            string token,
            string requestId,
            int plannedSeconds,
            int qaSeconds,
            CancellationToken cancellationToken)
        {
            ValidateProtectedRequest(sessionId, token);
            if (!Guid.TryParseExact(requestId, "D", out _))
                throw new ArgumentException("Finish request id must be a UUID.", nameof(requestId));
            ValidateReportSeconds(plannedSeconds, nameof(plannedSeconds));
            ValidateReportSeconds(qaSeconds, nameof(qaSeconds));

            var body = JsonUtility.ToJson(new ReportFinishRequest
            {
                request_id = requestId,
                planned_seconds = plannedSeconds,
                qa_seconds = qaSeconds
            });
            using (var webRequest = CreateJsonPost(
                       config.BuildApiUrl("/sessions/" + UnityWebRequest.EscapeURL(sessionId) + "/finish"),
                       body))
            {
                Configure(webRequest, token);
                var response = await SendAndReadAsync<ReportFinishResponse>(webRequest, cancellationToken);
                ValidateFinishResponse(response, sessionId);
                return response;
            }
        }

        public async Task<ReportStatusResponse> GetReportAsync(
            string sessionId,
            string token,
            CancellationToken cancellationToken)
        {
            ValidateProtectedRequest(sessionId, token);
            using (var webRequest = UnityWebRequest.Get(
                       config.BuildApiUrl("/sessions/" + UnityWebRequest.EscapeURL(sessionId) + "/report")))
            {
                Configure(webRequest, token);
                var response = await SendAndReadAsync<ReportStatusResponse>(webRequest, cancellationToken);
                ValidateReportStatusResponse(response, sessionId);
                return response;
            }
        }

        private static UnityWebRequest CreateJsonPost(string url, string body)
        {
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", "application/json");
            return request;
        }

        private void Configure(UnityWebRequest request, string token)
        {
            request.timeout = config.TimeoutSeconds;
            if (request.downloadHandler == null)
                request.downloadHandler = new DownloadHandlerBuffer();
            if (!string.IsNullOrWhiteSpace(token))
                request.SetRequestHeader(SessionTokenHeader, token);
        }

        private static async Task<T> SendAndReadAsync<T>(
            UnityWebRequest request,
            CancellationToken cancellationToken)
            where T : class
        {
            using (cancellationToken.Register(request.Abort))
            {
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (request.result != UnityWebRequest.Result.Success)
                throw CreateException(request);

            var body = request.downloadHandler?.text;
            if (string.IsNullOrWhiteSpace(body))
            {
                throw new EvcApiException(
                    EvcErrorKind.InvalidResponse,
                    "empty_response",
                    request.responseCode,
                    "EVC 서버가 빈 응답을 반환했습니다.");
            }

            try
            {
                var result = JsonUtility.FromJson<T>(body);
                if (result == null)
                    throw new InvalidOperationException("JSON deserializer returned null.");
                return result;
            }
            catch (Exception exception)
            {
                throw new EvcApiException(
                    EvcErrorKind.InvalidResponse,
                    "invalid_json",
                    request.responseCode,
                    "EVC 서버 응답 형식이 올바르지 않습니다.",
                    exception);
            }
        }

        private static EvcApiException CreateException(UnityWebRequest request)
        {
            var code = ReadSafeErrorCode(request.downloadHandler?.text);
            var status = request.responseCode;
            var timedOut = !string.IsNullOrWhiteSpace(request.error) &&
                           request.error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0;
            if (timedOut)
                code = "timeout";
            var kind = EvcErrorMapper.Map(
                status,
                request.result == UnityWebRequest.Result.ConnectionError,
                timedOut);
            var safeMessage = kind == EvcErrorKind.Timeout
                ? "EVC 요청 시간이 초과되었습니다."
                : "EVC 요청을 처리하지 못했습니다.";
            return new EvcApiException(kind, code, status, safeMessage);
        }

        private static string ReadSafeErrorCode(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return string.Empty;

            try
            {
                var envelope = JsonUtility.FromJson<ErrorEnvelope>(body);
                if (!string.IsNullOrWhiteSpace(envelope?.detail?.code))
                    return envelope.detail.code;
                if (!string.IsNullOrWhiteSpace(envelope?.detail?.error_code))
                    return envelope.detail.error_code;
                if (!string.IsNullOrWhiteSpace(envelope?.code))
                    return envelope.code;
            }
            catch
            {
                // The response body can contain user content. Never log or surface it.
            }

            return string.Empty;
        }

        private static MultipartFormDataSection Field(string name, string value)
        {
            return new MultipartFormDataSection(name, value ?? string.Empty);
        }

        private static void AddOptionalField(List<IMultipartFormSection> sections, string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                sections.Add(Field(name, value));
        }

        private static void ValidateStartRequest(StartSessionRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.presentation_title) || request.presentation_title.Length > 200)
                throw new ArgumentException("Presentation title must contain 1-200 characters.", nameof(request));
            if (!string.IsNullOrWhiteSpace(request.pre_session_pin) &&
                !Rehear.Evc.Data.PresentationDataValidator.IsFourDigitPin(request.pre_session_pin))
                throw new ArgumentException("Pre-session PIN must contain four characters.", nameof(request));
        }

        private static void ValidateSegmentRequest(EvcSegmentRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            ValidateProtectedRequest(request.session_id, request.session_token);
            if (string.IsNullOrWhiteSpace(request.request_id))
                throw new ArgumentException("Segment request id is required.", nameof(request));
            if (request.expected_step < 0 || request.client_time_s < 0d)
                throw new ArgumentOutOfRangeException(nameof(request), "Step and client time cannot be negative.");
            if (request.audio == null || !request.audio.IsValid)
                throw new ArgumentException("A non-empty audio file is required.", nameof(request));
            if (!string.IsNullOrWhiteSpace(request.utterance_position) &&
                !EvcContractRules.IsUtterancePosition(request.utterance_position))
            {
                throw new ArgumentException("Unsupported utterance position.", nameof(request));
            }
        }

        private static void ValidateProtectedRequest(string sessionId, string token)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentException("Session id is required.", nameof(sessionId));
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("Session token is required.", nameof(token));
        }

        private static void ValidateReportSeconds(int value, string parameterName)
        {
            if (value < 0 || value > 86400)
                throw new ArgumentOutOfRangeException(parameterName, "Report seconds must be between 0 and 86400.");
        }

        private static void ValidateFinishResponse(ReportFinishResponse response, string expectedSessionId)
        {
            if (response == null || response.session_id != expectedSessionId || response.status != "ready" ||
                string.IsNullOrWhiteSpace(response.generated_at) || !IsValidReport(response.report))
            {
                throw InvalidReportResponse();
            }
        }

        private static void ValidateReportStatusResponse(ReportStatusResponse response, string expectedSessionId)
        {
            if (response == null || response.session_id != expectedSessionId ||
                (response.status != "not_started" && response.status != "generating" &&
                 response.status != "ready" && response.status != "failed") ||
                (response.status == "ready" && !IsValidReport(response.report)))
            {
                throw InvalidReportResponse();
            }
        }

        internal static bool IsValidReport(ReportFeedback report)
        {
            return report != null &&
                   report.generation != null &&
                   !string.IsNullOrWhiteSpace(report.generation.generated_at) &&
                   !string.IsNullOrWhiteSpace(report.generation.generator) &&
                   report.score != null &&
                   !string.IsNullOrWhiteSpace(report.score.grade) &&
                   report.duration != null &&
                   report.score_card?.scores != null &&
                   report.score_card.descriptions != null &&
                   report.score_card.descriptions.engagement != null &&
                   report.score_card.descriptions.clarity != null &&
                   report.score_card.descriptions.credibility != null &&
                   report.detail_analysis?.content_analysis != null &&
                   report.detail_analysis.delivery_analysis != null &&
                   report.audience_analysis != null &&
                   report.ai_insight != null &&
                   report.ai_insight.title != null &&
                   report.ai_insight.description != null;
        }

        private static EvcApiException InvalidReportResponse()
        {
            return new EvcApiException(
                EvcErrorKind.InvalidResponse,
                "invalid_report",
                200,
                "EVC 보고서 응답의 필수값이 올바르지 않습니다.");
        }

        [Serializable]
        private sealed class ErrorEnvelope
        {
            public string code;
            public ErrorDetail detail;
        }

        [Serializable]
        private sealed class ErrorDetail
        {
            public string code;
            public string error_code;
        }
    }
}
