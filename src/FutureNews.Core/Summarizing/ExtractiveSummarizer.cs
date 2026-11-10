using FutureNews.Core.Fetching;
using FutureNews.Core.Models;

namespace FutureNews.Core.Summarizing;

/// <summary>
/// Offline fallback summariser. Picks the most informative sentence from the abstract or
/// description and compresses it into a single line. Used when no LLM key is configured
/// or when the LLM call fails.
/// </summary>
public sealed class ExtractiveSummarizer
{
    private readonly IReadOnlyList<string> _keywords;

    public ExtractiveSummarizer(IReadOnlyList<string>? keywords = null)
    {
        _keywords = keywords ?? Array.Empty<string>();
    }

    /// <summary>Builds a one-line summary from whatever text is available.</summary>
    public string Summarize(Article article)
    {
        var body = TextUtil.Collapse(TextUtil.StripHtml(article.Abstract));

        if (!string.IsNullOrWhiteSpace(body))
        {
            var sentences = TextUtil.SplitSentences(body);
            var ranked = sentences
                .Select((s, i) => (Sentence: s, Index: i, Score: ScoreSentence(s, i, article)))
                .Where(x => x.Score > double.NegativeInfinity)
                .OrderByDescending(x => x.Score)
                .ToList();

            foreach (var candidate in ranked)
            {
                var compressed = Compress(candidate.Sentence);
                // Very short fragments or teaser questions make poor summaries; try the next one.
                if (compressed.Length < 12) continue;
                if (compressed.EndsWith('?') || compressed.EndsWith('？')) continue;
                return compressed;
            }
        }

        // No usable body text: fall back to a descriptive title line.
        var title = TextUtil.Collapse(article.Title);
        if (title.Length == 0) return string.Empty;
        var label = article.SourceType switch
        {
            "paper" => "论文",
            "preprint" => "预印本",
            "community" => "社区讨论",
            _ => "报道",
        };
        return $"{label}：{TextUtil.TrimToLength(title, 60)}";
    }

    private double ScoreSentence(string sentence, int index, Article article)
    {
        var text = sentence.ToLowerInvariant();
        var score = 0.0;

        foreach (var keyword in _keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword)) continue;
            if (text.Contains(keyword.ToLowerInvariant(), StringComparison.Ordinal)) score += 1.6;
        }

        // The opening sentence of an abstract usually carries the claim.
        score += Math.Max(0, 4 - index) * 0.7;

        // Prefer sentences with concrete results.
        if (text.Contains("we show", StringComparison.Ordinal) || text.Contains("we report", StringComparison.Ordinal) ||
            text.Contains("we present", StringComparison.Ordinal) || text.Contains("our results", StringComparison.Ordinal) ||
            text.Contains("achiev", StringComparison.Ordinal) || text.Contains("demonstrat", StringComparison.Ordinal) ||
            text.Contains("outperform", StringComparison.Ordinal) || text.Contains("first", StringComparison.Ordinal))
        {
            score += 1.2;
        }

        if (text.Contains("however", StringComparison.Ordinal) || text.Contains("in this paper", StringComparison.Ordinal) ||
            text.Contains("we propose", StringComparison.Ordinal) && text.Contains("framework", StringComparison.Ordinal))
        {
            score -= 0.4;
        }

        var wordCount = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount is < 6 or > 60) score -= 1.5;
        if (wordCount is >= 12 and <= 35) score += 0.8;

        // Prefer sentences that mention numbers.
        if (sentence.Any(char.IsDigit)) score += 0.5;

        // Teaser questions ("Want to fly to the moon?") are not summaries.
        if (sentence.TrimEnd().EndsWith('?') || sentence.TrimEnd().EndsWith('？')) score -= 2.0;

        // Prefer sentences that read like a statement about the result.
        if (text.Contains("announced", StringComparison.Ordinal) ||
            text.Contains("reported", StringComparison.Ordinal) ||
            text.Contains("unveiled", StringComparison.Ordinal) ||
            text.Contains("launched", StringComparison.Ordinal) ||
            text.Contains("achieved", StringComparison.Ordinal) ||
            text.Contains("reached", StringComparison.Ordinal) ||
            text.Contains("developed", StringComparison.Ordinal) ||
            text.Contains("built", StringComparison.Ordinal) ||
            text.Contains("created", StringComparison.Ordinal) ||
            text.Contains("designed", StringComparison.Ordinal) ||
            text.Contains("succeeded", StringComparison.Ordinal))
        {
            score += 0.9;
        }

        return score;
    }

    /// <summary>Trims a sentence down to a single readable line.</summary>
    internal static string Compress(string sentence)
    {
        var text = TextUtil.Collapse(sentence);

        // Drop leading discourse markers that add nothing.
        foreach (var marker in new[] { "In this paper, ", "In this work, ", "Here we ", "Here, we ", "This paper ", "We ", "we " })
        {
            if (text.StartsWith(marker, StringComparison.Ordinal))
            {
                text = text[marker.Length..];
                if (text.Length > 0) text = char.ToUpperInvariant(text[0]) + text[1..];
                break;
            }
        }

        // Remove parenthetical asides.
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*\([^()]{0,120}\)", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*（[^（）]{0,60}）", " ");

        text = TextUtil.Collapse(text).Trim(' ', ',', ';', ':');
        return TextUtil.TrimToLength(text, 110);
    }
}
