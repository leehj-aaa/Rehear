using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Rehear.Evc.Data
{
    [Serializable]
    public sealed class PresentationDataDto
    {
        public string pin;
        public string presentation_title;
        public PresentationPage1Dto page_1;
        public PresentationPage2Dto page_2;
        public PresentationPage3Dto page_3;
        public bool is_demo_fallback;
    }

    [Serializable]
    public sealed class PresentationPage1Dto
    {
        public int duration_minutes;
        public string environment_type;
        public int qa_count;
    }

    [Serializable]
    public sealed class PresentationPage2Dto
    {
        public string presentation_script_content;
        public SlideImageDto slide_image;
    }

    [Serializable]
    public sealed class SlideImageDto
    {
        public string[] image_urls;
    }

    [Serializable]
    public sealed class PresentationPage3Dto
    {
        public string audience_expertise;
        public string audience_interest;
        public int audience_scale;
    }

    public sealed class PresentationValidationResult
    {
        public PresentationValidationResult(IReadOnlyList<string> errors)
        {
            Errors = errors ?? Array.Empty<string>();
        }

        public IReadOnlyList<string> Errors { get; }
        public bool IsValid => Errors.Count == 0;
    }

    public static class PresentationDataValidator
    {
        public static PresentationValidationResult Validate(PresentationDataDto data)
        {
            var errors = new List<string>();
            if (data == null)
            {
                errors.Add("세션 데이터가 없습니다.");
                return new PresentationValidationResult(errors);
            }

            if (!IsFourDigitPin(data.pin))
                errors.Add("PIN은 4자리여야 합니다.");
            if (string.IsNullOrWhiteSpace(data.presentation_title) || data.presentation_title.Length > 200)
                errors.Add("발표 제목(presentation_title)이 필요합니다.");
            if (data.page_1 == null)
                errors.Add("page_1이 없습니다.");
            else
            {
                if (data.page_1.duration_minutes <= 0)
                    errors.Add("duration_minutes는 1 이상이어야 합니다.");
                if (string.IsNullOrWhiteSpace(data.page_1.environment_type))
                    errors.Add("environment_type이 없습니다.");
                if (data.page_1.qa_count < 1 || data.page_1.qa_count > 5)
                    errors.Add("qa_count는 1~5여야 합니다.");
            }

            if (data.page_2 == null)
                errors.Add("page_2가 없습니다.");
            else if (string.IsNullOrWhiteSpace(data.page_2.presentation_script_content))
                errors.Add("presentation_script_content가 없습니다.");
            if (data.page_3 == null)
                errors.Add("page_3이 없습니다.");
            else
            {
                if (!IsProfileLevel(data.page_3.audience_expertise))
                    errors.Add("audience_expertise는 low/middle/high 범위여야 합니다.");
                if (!IsProfileLevel(data.page_3.audience_interest))
                    errors.Add("audience_interest는 low/middle/high 범위여야 합니다.");
                if (data.page_3.audience_scale <= 0)
                    errors.Add("audience_scale은 1 이상이어야 합니다.");
            }

            return new PresentationValidationResult(errors);
        }

        private static bool IsProfileLevel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            switch (value.Trim().ToLowerInvariant())
            {
                case "low":
                case "낮음":
                case "0.25":
                case "middle":
                case "medium":
                case "보통":
                case "0.5":
                case "high":
                case "높음":
                case "0.75":
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsFourDigitPin(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 4)
                return false;

            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] < '0' || value[index] > '9')
                    return false;
            }

            return true;
        }
    }

    public enum PresentationLoadStatus
    {
        Success,
        NotFound,
        Invalid,
        Cancelled,
        Failed
    }

    public sealed class PresentationLoadResult
    {
        private PresentationLoadResult(
            PresentationLoadStatus status,
            PresentationDataDto data,
            string userMessage,
            IReadOnlyList<string> diagnostics)
        {
            Status = status;
            Data = data;
            UserMessage = userMessage ?? string.Empty;
            Diagnostics = diagnostics ?? Array.Empty<string>();
        }

        public PresentationLoadStatus Status { get; }
        public PresentationDataDto Data { get; }
        public string UserMessage { get; }
        public IReadOnlyList<string> Diagnostics { get; }
        public bool IsSuccess => Status == PresentationLoadStatus.Success;

        public static PresentationLoadResult Success(PresentationDataDto data) =>
            new PresentationLoadResult(PresentationLoadStatus.Success, data, string.Empty, Array.Empty<string>());

        public static PresentationLoadResult Error(
            PresentationLoadStatus status,
            string userMessage,
            IReadOnlyList<string> diagnostics = null) =>
            new PresentationLoadResult(status, null, userMessage, diagnostics);
    }

    public interface IPresentationRepository
    {
        Task<PresentationLoadResult> LoadAsync(string pin, CancellationToken cancellationToken);
    }

    public static class FirebasePresentationValueMapper
    {
        public static PresentationDataDto Map(
            IDictionary root,
            string pin,
            out IReadOnlyList<string> errors)
        {
            var diagnostics = new List<string>();
            var page1 = ReadMap(root, "page_1", diagnostics);
            var page2 = ReadMap(root, "page_2", diagnostics);
            var page3 = ReadMap(root, "page_3", diagnostics);
            var slideImage = ReadMap(page2, "slide_image", diagnostics, false);

            var result = new PresentationDataDto
            {
                pin = pin,
                presentation_title = ReadString(root, "presentation_title", diagnostics),
                page_1 = page1 != null ? new PresentationPage1Dto
                {
                    duration_minutes = ReadInt(page1, "duration_minutes", diagnostics),
                    environment_type = ReadString(page1, "environment_type", diagnostics),
                    qa_count = ReadInt(page1, "qa_count", diagnostics, 3, true)
                } : null,
                page_2 = page2 != null ? new PresentationPage2Dto
                {
                    presentation_script_content = ReadString(
                        page2,
                        "presentation_script_content",
                        diagnostics),
                    slide_image = new SlideImageDto
                    {
                        image_urls = ReadStringArray(slideImage, "image_urls", diagnostics)
                    }
                } : null,
                page_3 = page3 != null ? new PresentationPage3Dto
                {
                    audience_expertise = ReadString(page3, "audience_expertise", diagnostics),
                    audience_interest = ReadString(page3, "audience_interest", diagnostics),
                    audience_scale = ReadInt(page3, "audience_scale", diagnostics)
                } : null,
                is_demo_fallback = false
            };

            errors = diagnostics;
            return result;
        }

        private static IDictionary ReadMap(
            IDictionary parent,
            string key,
            ICollection<string> errors,
            bool required = true)
        {
            if (parent == null || !parent.Contains(key) || parent[key] == null)
            {
                if (required)
                    errors.Add(key + " is missing.");
                return null;
            }

            if (parent[key] is IDictionary result)
                return result;

            errors.Add(key + " is not an object.");
            return null;
        }

        private static string ReadString(
            IDictionary parent,
            string key,
            ICollection<string> errors)
        {
            if (parent == null || !parent.Contains(key) || parent[key] == null)
                return string.Empty;
            if (parent[key] is string value)
                return value;

            errors.Add(key + " is not a string.");
            return string.Empty;
        }

        private static int ReadInt(
            IDictionary parent,
            string key,
            ICollection<string> errors,
            int defaultValue = 0,
            bool optional = false)
        {
            if (parent == null || !parent.Contains(key) || parent[key] == null)
                return optional ? defaultValue : 0;

            try
            {
                var value = parent[key];
                if (!(value is sbyte) && !(value is byte) &&
                    !(value is short) && !(value is ushort) &&
                    !(value is int) && !(value is uint) &&
                    !(value is long) && !(value is ulong) &&
                    !(value is float) && !(value is double) &&
                    !(value is decimal))
                {
                    throw new FormatException();
                }
                if (value is float || value is double || value is decimal)
                {
                    var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    if (Math.Abs(number % 1d) > double.Epsilon)
                        throw new FormatException();
                }
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                errors.Add(key + " is not an integer.");
                return 0;
            }
        }

        private static string[] ReadStringArray(
            IDictionary parent,
            string key,
            ICollection<string> errors)
        {
            if (parent == null || !parent.Contains(key) || parent[key] == null)
                return Array.Empty<string>();

            var values = new List<object>();
            if (parent[key] is IDictionary dictionary)
            {
                var entries = new List<DictionaryEntry>();
                foreach (DictionaryEntry entry in dictionary)
                    entries.Add(entry);
                entries.Sort((left, right) => CompareFirebaseKeys(left.Key, right.Key));
                for (var index = 0; index < entries.Count; index++)
                    values.Add(entries[index].Value);
            }
            else if (parent[key] is IEnumerable enumerable && !(parent[key] is string))
            {
                foreach (var value in enumerable)
                    values.Add(value);
            }
            else
            {
                errors.Add(key + " is not an array.");
                return Array.Empty<string>();
            }

            var urls = new List<string>();
            for (var index = 0; index < values.Count; index++)
            {
                if (!(values[index] is string value) ||
                    !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                {
                    errors.Add("slide image URL is invalid.");
                    continue;
                }
                urls.Add(value);
            }
            return urls.ToArray();
        }

        private static int CompareFirebaseKeys(object left, object right)
        {
            var leftText = Convert.ToString(left, CultureInfo.InvariantCulture);
            var rightText = Convert.ToString(right, CultureInfo.InvariantCulture);
            if (int.TryParse(leftText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var leftNumber) &&
                int.TryParse(rightText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rightNumber))
            {
                return leftNumber.CompareTo(rightNumber);
            }
            return string.CompareOrdinal(leftText, rightText);
        }
    }
}
