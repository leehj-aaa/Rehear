using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Rehear.Evc.Contracts
{
    [Serializable]
    public sealed class BinaryFileDto
    {
        public byte[] bytes;
        public string file_name;
        public string mime_type;

        public bool IsValid => bytes != null && bytes.Length > 0 &&
                               !string.IsNullOrWhiteSpace(file_name) &&
                               !string.IsNullOrWhiteSpace(mime_type);
    }

    [Serializable]
    public sealed class StartSessionRequest
    {
        public string presentation_title;
        public string topic_interest;
        public string prior_knowledge;
        public int? seed;
        public string pre_session_pin;
        public BinaryFileDto slide_file;
    }

    [Serializable]
    public sealed class AudienceDto
    {
        public string agent_id;
        public string id;
        public AudienceSeatingProfile profile;

        public string AgentId => !string.IsNullOrWhiteSpace(agent_id) ? agent_id : id;
    }

    [Serializable]
    public sealed class AudienceSeatingProfile
    {
        public string row;
        public string seat;
        public bool has_laptop;
    }

    [Serializable]
    public sealed class SmartStartResponse
    {
        public string session_id;
        public string session_token;
        public int seed;
        public int step;
        public int slide_count;
        public string status;
        public AudienceDto[] audiences;
    }

    [Serializable]
    public sealed class EvcSegmentRequest
    {
        public string session_id;
        public string session_token;
        public string request_id;
        public int expected_step;
        public double client_time_s;
        public int current_slide_index;
        public string utterance_position;
        public string language = "ko-KR";
        public BinaryFileDto audio;
    }

    [Serializable]
    public sealed class UnityCommandDto
    {
        public string agent_id;
        public float start_time;
        public string layer;
        public string action_id;
        public float duration;
        public string sync_group;
        public string selected_behavior_id;
        public string selected_variation_id;
        public int priority;
        public string blend_mode;
        public float intensity;
    }

    [Serializable]
    public sealed class EvcUpdateResponse
    {
        public string session_id;
        public string request_id;
        public int step;
        public string status;
        public UnityCommandDto[] commands;
    }

    [Serializable]
    public sealed class SessionResponse
    {
        public string session_id;
        public int seed;
        public int step;
        public int slide_count;
        public string status;
        public AudienceDto[] audiences;
    }

    [Serializable]
    public sealed class QuestionGenerationRequest
    {
        public string request_id;
        public int question_count;
    }

    [Serializable]
    public sealed class GeneratedQuestion
    {
        public string id;
        public int order;
        public string question;
        public string intent;
        public int[] source_steps;
    }

    [Serializable]
    public sealed class QuestionListResponse
    {
        public string session_id;
        public string status;
        public string generated_at;
        public GeneratedQuestion[] questions;
    }

    [Serializable]
    public sealed class ReportFinishRequest
    {
        public string request_id;
        public int planned_seconds;
        public int qa_seconds;
    }

    [Serializable]
    public sealed class ReportGenerationMetadata
    {
        public string generated_at;
        public string generator;
        public int source_segment_count;
        public int transcript_word_count;
        public string[] warnings;
    }

    [Serializable]
    public sealed class ReportScore
    {
        public int overall_score;
        public int percentile;
        public string grade;
    }

    [Serializable]
    public sealed class ReportDuration
    {
        public int planned_seconds;
        public int actual_seconds;
        public int qa_seconds;
    }

    [Serializable]
    public sealed class ReportScoreCardValues
    {
        public int engagement;
        public int clarity;
        public int credibility;
    }

    [Serializable]
    public sealed class ReportScoreCardDescriptions
    {
        public string engagement;
        public string clarity;
        public string credibility;
    }

    [Serializable]
    public sealed class ReportScoreCard
    {
        public ReportScoreCardValues scores;
        public ReportScoreCardDescriptions descriptions;
    }

    [Serializable]
    public sealed class HighlightMetric
    {
        public string name;
        public int score;
    }

    // The API defines these two objects as arbitrary string-to-integer maps.
    // JsonUtility preserves their presence; named keys remain server-extensible.
    [Serializable]
    public sealed class ReportIntegerMap
    {
    }

    [Serializable]
    public sealed class DetailAnalysis
    {
        public HighlightMetric[] highlight_metrics;
        public ReportIntegerMap content_analysis;
        public ReportIntegerMap delivery_analysis;
    }

    [Serializable]
    public sealed class TimelineItem
    {
        public int time_sec;
        public string title;
        public string description;
        public string type;
        public int slide;
        public int source_step;
    }

    [Serializable]
    public sealed class AudienceGraphPoint
    {
        public int time_sec;
        public float E;
        public float V;
        public float C;
    }

    [Serializable]
    public sealed class AudienceEvent
    {
        public int time_sec;
        public string label;
        public string type;
        public int source_step;
    }

    [Serializable]
    public sealed class AudienceAnalysis
    {
        public AudienceGraphPoint[] graph;
        public AudienceEvent[] events;
    }

    [Serializable]
    public sealed class AIInsight
    {
        public string title;
        public string description;
    }

    [Serializable]
    public sealed class ReportFeedback
    {
        public string version;
        public ReportGenerationMetadata generation;
        public ReportScore score;
        public ReportDuration duration;
        public ReportScoreCard score_card;
        public DetailAnalysis detail_analysis;
        public TimelineItem[] timeline;
        public AudienceAnalysis audience_analysis;
        public AIInsight ai_insight;
    }

    [Serializable]
    public sealed class ReportFinishResponse
    {
        public string session_id;
        public string status;
        public string generated_at;
        public string persistent_session_id;
        public ReportFeedback report;
    }

    [Serializable]
    public sealed class ReportStatusResponse
    {
        public string session_id;
        public string status;
        public string persistent_session_id;
        public string error;
        public ReportFeedback report;
    }

    public interface IEvcApiClient
    {
        Task<SmartStartResponse> StartSessionAsync(StartSessionRequest request, CancellationToken cancellationToken);
        Task<EvcUpdateResponse> SendSegmentAsync(EvcSegmentRequest request, CancellationToken cancellationToken);
        Task<SessionResponse> GetSessionAsync(string sessionId, string token, CancellationToken cancellationToken);
        Task<QuestionListResponse> GenerateQuestionsAsync(
            string sessionId,
            string token,
            string requestId,
            int count,
            CancellationToken cancellationToken);
        Task<QuestionListResponse> GetQuestionsAsync(string sessionId, string token, CancellationToken cancellationToken);
        Task<ReportFinishResponse> FinishSessionAsync(
            string sessionId,
            string token,
            string requestId,
            int plannedSeconds,
            int qaSeconds,
            CancellationToken cancellationToken);
        Task<ReportStatusResponse> GetReportAsync(string sessionId, string token, CancellationToken cancellationToken);
    }

    public enum EvcErrorKind
    {
        Unknown,
        Cancelled,
        Timeout,
        Unauthorized,
        NotFound,
        Conflict,
        PayloadTooLarge,
        UnsupportedMediaType,
        Validation,
        RateLimited,
        ProviderUnavailable,
        ServerError,
        Network,
        InvalidResponse
    }

    public sealed class EvcApiException : Exception
    {
        public EvcApiException(
            EvcErrorKind kind,
            string errorCode,
            long httpStatus,
            string safeMessage,
            Exception innerException = null)
            : base(safeMessage, innerException)
        {
            Kind = kind;
            ErrorCode = errorCode ?? string.Empty;
            HttpStatus = httpStatus;
        }

        public EvcErrorKind Kind { get; }
        public string ErrorCode { get; }
        public long HttpStatus { get; }

        public bool IsTransient => Kind == EvcErrorKind.Timeout ||
                                   Kind == EvcErrorKind.RateLimited ||
                                   Kind == EvcErrorKind.ProviderUnavailable ||
                                   Kind == EvcErrorKind.ServerError ||
                                   Kind == EvcErrorKind.Network;
    }

    public static class EvcContractRules
    {
        public const int AudienceCount = 6;

        public static readonly string[] RequiredAudienceIds =
        {
            "audience_01",
            "audience_02",
            "audience_03",
            "audience_04",
            "audience_05",
            "audience_06"
        };

        public static bool IsUtterancePosition(string value)
        {
            return value == "during_speech" ||
                   value == "utterance_boundary" ||
                   value == "silence_or_pause" ||
                   value == "slide_transition";
        }

        public static bool IsLayer(string value)
        {
            return value == "Face" || value == "Body" || value == "GazeHead";
        }

        public static bool IsBlendMode(string value)
        {
            return value == "override" || value == "additive";
        }

        public static IReadOnlyList<string> ValidateAudienceSet(AudienceDto[] audiences)
        {
            var errors = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (audiences == null || audiences.Length != AudienceCount)
            {
                errors.Add("audiences must contain exactly six entries.");
                return errors;
            }

            for (var i = 0; i < audiences.Length; i++)
            {
                var id = audiences[i]?.AgentId;
                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                    errors.Add("audiences contains a missing or duplicate agent id.");
            }

            for (var i = 0; i < RequiredAudienceIds.Length; i++)
            {
                if (!seen.Contains(RequiredAudienceIds[i]))
                    errors.Add("audiences is missing " + RequiredAudienceIds[i] + ".");
            }

            return errors;
        }
    }
}
