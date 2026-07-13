namespace Outspoken.Core.Transcription;

/// <summary>
/// Word-level similarity for transcript comparison (T5 verify: fixture transcript must
/// fuzzy-match ≥90%). 1 − word error rate: Levenshtein distance over normalized words,
/// standard for speech-to-text accuracy.
/// </summary>
public static class TranscriptSimilarity
{
    public static double Compare(string expected, string actual)
    {
        var e = Tokenize(expected);
        var a = Tokenize(actual);
        if (e.Length == 0)
            return a.Length == 0 ? 1.0 : 0.0;

        var distance = WordLevenshtein(e, a);
        return Math.Max(0.0, 1.0 - (double)distance / e.Length);
    }

    private static string[] Tokenize(string text) =>
        text.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()))
            .Where(w => w.Length > 0)
            .ToArray();

    private static int WordLevenshtein(string[] a, string[] b)
    {
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }
}
