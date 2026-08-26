using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Rehear.Evc.Audience
{
    public static class ClipPoolActionIdExtractor
    {
        private static readonly Regex ActionIdPattern = new Regex(
            "\\\"action_id\\\"\\s*:\\s*\\\"(?<id>[^\\\"]+)\\\"",
            RegexOptions.CultureInvariant);

        public static IReadOnlyList<string> Extract(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<string>();

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var matches = ActionIdPattern.Matches(json);
            for (var index = 0; index < matches.Count; index++)
            {
                var id = matches[index].Groups["id"].Value;
                if (!string.IsNullOrWhiteSpace(id) && seen.Add(id))
                    result.Add(id);
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }
    }
}
