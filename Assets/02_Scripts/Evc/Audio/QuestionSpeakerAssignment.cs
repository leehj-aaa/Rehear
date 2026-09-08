using System;
using System.Collections.Generic;

namespace Rehear.Evc.Audio
{
    public static class QuestionSpeakerAssignment
    {
        // Stable within a session, distinct until every available audience member has a turn.
        public static string[] Create(IReadOnlyList<string> audienceIds, int questionCount, int seed)
        {
            if (questionCount < 0) throw new ArgumentOutOfRangeException(nameof(questionCount));
            if (audienceIds == null || audienceIds.Count == 0)
                throw new InvalidOperationException("질문할 청중이 없습니다.");
            var shuffled = new List<string>(audienceIds);
            shuffled.Sort(StringComparer.Ordinal);
            var random = new Random(seed);
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }
            var result = new string[questionCount];
            for (int i = 0; i < result.Length; i++) result[i] = shuffled[i % shuffled.Count];
            return result;
        }
    }
}
