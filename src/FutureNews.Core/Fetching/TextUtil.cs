using System.Text;
using System.Text.RegularExpressions;

namespace FutureNews.Core.Fetching;

/// <summary>Small text helpers shared by scoring, dedup and summarisation.</summary>
public static class TextUtil
{
    private static readonly Regex HtmlTag = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex HtmlEntity = new("&(nbsp|amp|lt|gt|quot|#39|#x27|apos|ldquo|rdquo|lsquo|rsquo|hellip|mdash|ndash|times|middot);",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex NonWord = new(@"[^\p{L}\p{Nd}\s]+", RegexOptions.Compiled);
    private static readonly Regex SentenceSplit = new(@"(?<=[。！？!?])\s*|(?<=\.)\s+(?=[A-Z(])|\n+", RegexOptions.Compiled);
    private static readonly Regex Parenthetical = new(@"\s*\((?:[^()]{0,120})\)|（[^（）]{0,80}）", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> EntityMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["&nbsp;"] = " ", ["&amp;"] = "&", ["&lt;"] = "<", ["&gt;"] = ">",
        ["&quot;"] = "\"", ["&#39;"] = "'", ["&#x27;"] = "'", ["&apos;"] = "'",
        ["&ldquo;"] = "\u201c", ["&rdquo;"] = "\u201d", ["&lsquo;"] = "\u2018",
        ["&rsquo;"] = "\u2019", ["&hellip;"] = "\u2026", ["&mdash;"] = "\u2014",
        ["&ndash;"] = "\u2013", ["&times;"] = "\u00d7", ["&middot;"] = "\u00b7",
    };

    public static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var text = HtmlTag.Replace(html, " ");
        text = HtmlEntity.Replace(text, m => EntityMap.TryGetValue(m.Value, out var v) ? v : " ");
        return Collapse(text);
    }

    public static string Collapse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return Whitespace.Replace(text, " ").Trim();
    }

    /// <summary>Lower-cased, punctuation-free title used for exact/near duplicate detection.</summary>
    public static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        var t = StripHtml(title).ToLowerInvariant();
        t = Parenthetical.Replace(t, " ");
        t = NonWord.Replace(t, " ");
        return Collapse(t);
    }

    /// <summary>Splits text into sentences for extractive summarisation.</summary>
    public static IReadOnlyList<string> SplitSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        var parts = SentenceSplit.Split(text);
        var result = new List<string>();
        var buffer = new StringBuilder();

        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            buffer.Append(buffer.Length > 0 ? " " : "").Append(part.Trim());
            // CJK sentences are already complete; Latin sentences end with a terminator.
            if (part.EndsWith('。') || part.EndsWith('！') || part.EndsWith('？') || part.EndsWith('.') || part.EndsWith('!') || part.EndsWith('?'))
            {
                result.Add(buffer.ToString().Trim());
                buffer.Clear();
            }
        }

        if (buffer.Length > 0) result.Add(buffer.ToString().Trim());
        return result.Where(s => s.Length > 0).ToList();
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","a","an","and","or","of","in","on","for","to","with","by","from","at","as","is","are","was","were",
        "be","been","being","it","its","this","that","these","those","we","our","their","they","he","she","his","her",
        "has","have","had","do","does","did","can","could","may","might","will","would","should","not","but","if","then",
        "than","so","such","which","who","what","when","where","how","into","via","using","use","used","new","study",
        "research","researchers","paper","papers","based","show","shows","shown","here","also","more","most","over","under",
        "between","about","after","before","during","while","their","them","because","both","each","other","same",
        "论文","研究","我们","本文","该文","一种","一个","以及","通过","进行","提出","方法","结果","表明","发现","可以","已经",
    };

    /// <summary>Lower-cased content tokens, stop-words removed. Handles CJK by emitting bigrams.</summary>
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        var tokens = new List<string>();
        var sb = new StringBuilder();

        void Flush()
        {
            if (sb.Length == 0) return;
            var word = sb.ToString();
            sb.Clear();
            if (word.Length is >= 2 and <= 40 && !StopWords.Contains(word)) tokens.Add(word);
        }

        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                continue;
            }

            Flush();
        }
        Flush();

        // CJK text has no spaces: add character bigrams so keyword matching still works.
        var cjk = new StringBuilder();
        foreach (var ch in text)
        {
            if (ch >= 0x4E00 && ch <= 0x9FFF)
            {
                cjk.Append(ch);
            }
            else if (cjk.Length > 0)
            {
                AddCjkBigrams(cjk.ToString(), tokens);
                cjk.Clear();
            }
        }
        if (cjk.Length > 0) AddCjkBigrams(cjk.ToString(), tokens);

        return tokens;
    }

    private static void AddCjkBigrams(string run, List<string> tokens)
    {
        for (var i = 0; i + 1 < run.Length; i++)
        {
            tokens.Add(run.Substring(i, 2));
        }
        if (run.Length >= 2 && !StopWords.Contains(run)) tokens.Add(run);
    }

    /// <summary>
    /// Counts how many times a phrase occurs in the text. Latin-script phrases are matched
    /// on word boundaries so that e.g. "ITER" does not match "criter-ia" or "liter-al";
    /// CJK phrases are matched as plain substrings because Chinese has no word separators.
    /// </summary>
    public static int CountOccurrences(string text, string phrase)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(phrase)) return 0;
        var needle = phrase.ToLowerInvariant().Trim();
        if (needle.Length == 0) return 0;

        if (IsLatinPhrase(needle))
        {
            var pattern = @"(?<![a-z0-9])" + Regex.Escape(needle) + @"(?![a-z0-9])";
            return Regex.Matches(text.ToLowerInvariant(), pattern).Count;
        }

        var haystack = text.ToLowerInvariant();
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
            if (count > 50) break;
        }
        return count;
    }

    /// <summary>True when the phrase consists only of Latin letters, digits and separators.</summary>
    private static bool IsLatinPhrase(string phrase)
    {
        foreach (var ch in phrase)
        {
            if (ch > 0x2E80) return false; // any CJK / full-width character
        }
        return phrase.Any(ch => char.IsLetter(ch));
    }

    /// <summary>Removes asides and truncates on a clause boundary, producing a single compact line.</summary>
    public static string TrimToLength(string text, int maxChars)
    {
        var t = Collapse(text);
        if (t.Length <= maxChars) return t;
        var cut = t[..maxChars];
        var boundary = cut.LastIndexOfAny(new[] { '，', ',', '、', '；', ';', '：', ':', '。', '.', ' ', ' ' });
        if (boundary > maxChars / 2) cut = cut[..boundary];
        return cut.TrimEnd(' ', '，', ',', '、', '；', ';', '：', ':', '。', '.');
    }
}
