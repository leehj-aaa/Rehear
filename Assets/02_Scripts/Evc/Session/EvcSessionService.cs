using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rehear.Evc.Contracts;
using Rehear.Evc.Presentation;

namespace Rehear.Evc.Session
{
    public sealed class EvcSessionService
    {
        private readonly IEvcApiClient apiClient;
        private readonly PresentationSessionContext context;

        public EvcSessionService(IEvcApiClient apiClient, PresentationSessionContext context)
        {
            this.apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            this.context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<SmartStartResponse> StartAsync(
            BinaryFileDto optionalSlideFile,
            int? seed,
            CancellationToken cancellationToken)
        {
            var presentation = context.Presentation;
            if (presentation == null)
                throw new InvalidOperationException("Presentation context is not ready.");

            var request = new StartSessionRequest
            {
                presentation_title = presentation.presentation_title,
                topic_interest = NormalizeProfileLevel(presentation.page_3?.audience_interest),
                prior_knowledge = NormalizeProfileLevel(presentation.page_3?.audience_expertise),
                pre_session_pin = presentation.pin,
                seed = seed,
                slide_file = optionalSlideFile
            };

            var response = await apiClient.StartSessionAsync(request, cancellationToken);
            ValidateResponse(response);
            context.ApplySmartStart(response);
            return response;
        }

        public async Task<SessionResponse> RestoreAsync(CancellationToken cancellationToken)
        {
            if (!context.HasEvcSession)
                throw new InvalidOperationException("There is no EVC session to restore.");

            var response = await apiClient.GetSessionAsync(
                context.SessionId,
                context.SessionToken,
                cancellationToken);
            var errors = EvcContractRules.ValidateAudienceSet(response?.audiences);
            if (response == null ||
                string.IsNullOrWhiteSpace(response.session_id) ||
                response.step < 0 ||
                response.slide_count < 0 ||
                errors.Count > 0)
                throw new EvcApiException(EvcErrorKind.InvalidResponse, "invalid_session", 200, JoinErrors(errors));

            context.ApplyServerState(response);
            return response;
        }

        private static void ValidateResponse(SmartStartResponse response)
        {
            if (response == null ||
                string.IsNullOrWhiteSpace(response.session_id) ||
                string.IsNullOrWhiteSpace(response.session_token) ||
                response.step != 0 ||
                response.slide_count < 0)
            {
                throw new EvcApiException(
                    EvcErrorKind.InvalidResponse,
                    "invalid_smart_start",
                    200,
                    "EVC smart-start 응답에 필수 세션 정보가 없습니다.");
            }

            var errors = EvcContractRules.ValidateAudienceSet(response.audiences);
            if (errors.Count > 0)
            {
                throw new EvcApiException(
                    EvcErrorKind.InvalidResponse,
                    "invalid_audiences",
                    200,
                    JoinErrors(errors));
            }
        }

        private static string NormalizeProfileLevel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            switch (value.Trim().ToLowerInvariant())
            {
                case "low":
                case "낮음":
                case "0.25":
                    return "low";
                case "middle":
                case "medium":
                case "보통":
                case "0.5":
                    return "middle";
                case "high":
                case "높음":
                case "0.75":
                    return "high";
                default:
                    return null;
            }
        }

        private static string JoinErrors(IReadOnlyList<string> errors)
        {
            return errors == null || errors.Count == 0
                ? "EVC session response is invalid."
                : string.Join(" ", errors);
        }
    }
}
