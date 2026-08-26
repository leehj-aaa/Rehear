using Rehear.Evc.Contracts;
using UnityEngine;
using System.Text;

namespace Rehear.Evc.Transport
{
    public static class EvcSafeDiagnostics
    {
        public static void RequestFailed(string operation, string requestId, int step, EvcApiException exception)
        {
            var kind = exception != null ? exception.Kind.ToString() : EvcErrorKind.Unknown.ToString();
            var code = exception != null ? SafeErrorCode(exception.ErrorCode) : string.Empty;
            Debug.LogWarning(
                "EVC request failed" +
                " operation=" + Sanitize(operation) +
                " request_id=" + Sanitize(requestId) +
                " step=" + step +
                " kind=" + kind +
                " code=" + Sanitize(code));
        }

        public static void CommandDropped(string requestId, string agentId, string layer, string reason)
        {
            Debug.LogWarning(
                "EVC command dropped" +
                " request_id=" + Sanitize(requestId) +
                " agent=" + Sanitize(agentId) +
                " layer=" + Sanitize(layer) +
                " reason=" + Sanitize(reason));
        }

        public static void SegmentProcessed(
            string requestId,
            int step,
            int pendingCount,
            double elapsedMilliseconds)
        {
            Debug.Log(
                "EVC segment processed" +
                " request_id=" + Sanitize(requestId) +
                " step=" + step +
                " queue_length=" + Mathf.Max(0, pendingCount) +
                " elapsed_ms=" + Mathf.Max(0f, (float)elapsedMilliseconds).ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "-";

            var length = Mathf.Min(value.Length, 80);
            var safe = new StringBuilder(length);
            for (var index = 0; index < length; index++)
            {
                var character = value[index];
                safe.Append(
                    character >= 'a' && character <= 'z' ||
                    character >= 'A' && character <= 'Z' ||
                    character >= '0' && character <= '9' ||
                    character == '-' || character == '_' || character == '.' || character == ':'
                        ? character
                        : '_');
            }
            return safe.ToString();
        }

        private static string SafeErrorCode(string value)
        {
            switch (value)
            {
                case "timeout":
                case "step_conflict":
                case "invalid_session_token":
                case "session_not_found":
                case "questions_not_generated":
                case "questions_generating":
                case "presentation_finished":
                case "transcript_too_short":
                case "question_generation_provider_error":
                case "payload_too_large":
                case "unsupported_media_type":
                    return value;
                default:
                    return string.IsNullOrWhiteSpace(value) ? "-" : "unrecognized";
            }
        }
    }
}
