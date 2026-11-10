using FutureNews.Core.Fetching;
using FutureNews.Core.Models;
using FutureNews.Core.Topics;

namespace FutureNews.Core.Processing;

/// <summary>Scores how well an article matches a topic and ranks it against others.</summary>
public static class RelevanceScorer
{
    /// <summary>Computes a 0..1 relevance score for an article against a topic.</summary>
    public static double Score(Article article, Topic topic, IReadOnlyList<string> extraKeywords, IReadOnlyList<string> excludeKeywords)
    {
        var title = article.Title ?? string.Empty;
        var body = article.Abstract ?? string.Empty;
        var venue = article.Venue ?? string.Empty;
        var haystack = $"{title} {body} {venue}";

        if (excludeKeywords.Count > 0 &&
            excludeKeywords.Any(k => !string.IsNullOrWhiteSpace(k) && TextUtil.CountOccurrences(haystack, k) > 0))
        {
            return 0;
        }

        var negativeHits = topic.NegativeKeywords.Count(k => TextUtil.CountOccurrences(haystack, k) > 0);
        if (negativeHits > 0) return 0;

        double raw = 0;
        double best = 0;
        double specificRaw = 0;
        var specificHits = 0;
        var ambiguousHits = 0;

        var ambiguous = topic.AmbiguousKeywords.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (phrase, weight) in topic.Keywords)
        {
            if (string.IsNullOrWhiteSpace(phrase)) continue;
            var inTitle = TextUtil.CountOccurrences(title, phrase);
            var inBody = TextUtil.CountOccurrences(body, phrase);
            if (inTitle == 0 && inBody == 0) continue;

            // A title hit is far stronger evidence than a mention buried in the abstract.
            var contribution = weight * (1 + Math.Min(inTitle, 2) * 1.8 + Math.Min(inBody, 2) * 0.4);
            raw += contribution;
            best = Math.Max(best, weight);

            if (ambiguous.Contains(phrase))
            {
                ambiguousHits++;
            }
            else
            {
                specificRaw += contribution;
                specificHits++;
            }
        }

        foreach (var phrase in extraKeywords)
        {
            if (string.IsNullOrWhiteSpace(phrase)) continue;
            var inTitle = TextUtil.CountOccurrences(title, phrase);
            var inBody = TextUtil.CountOccurrences(body, phrase);
            if (inTitle == 0 && inBody == 0) continue;
            raw += 1.5 * (1 + Math.Min(inTitle, 2) * 1.5 + Math.Min(inBody, 2) * 0.3);
            best = Math.Max(best, 1.5);
            specificRaw += 1.5;
            specificHits++;
        }

        // A generic phrase on its own is never enough. "moon" alone must not turn a Pokémon
        // article into space news, and "fusion" alone must not turn a statistics paper into
        // fusion news. Generic terms only count when the article also carries either one
        // more specific keyword or at least two independent generic ones.
        if (specificHits == 0)
        {
            if (ambiguousHits < 2) return 0;
            raw *= 0.5;
            best = 0;
        }

        if (raw <= 0) return 0;

        // arXiv category affinity: a paper in the right category is strong evidence.
        if (!string.IsNullOrEmpty(article.Venue) && topic.ArxivCategories.Any(c => article.Venue.Contains(c, StringComparison.OrdinalIgnoreCase)))
        {
            raw += 1.6;
        }

        // Titles that are round-ups, calls for papers, reviews or opinion pieces carry less signal.
        var titleLower = title.ToLowerInvariant();
        if (titleLower.Contains("survey") || titleLower.Contains("review:") || titleLower.Contains("editorial") ||
            titleLower.Contains("call for") || titleLower.Contains("roundup") || titleLower.Contains("round-up") ||
            titleLower.Contains("what is") || titleLower.Contains("explained:") ||
            titleLower.Contains("systematic review") || titleLower.Contains("literature review") ||
            titleLower.Contains("meta-analysis") || titleLower.Contains("bibliometric") ||
            titleLower.Contains("a review of"))
        {
            raw *= 0.45;
        }

        if (titleLower.Contains("we ") || titleLower.Contains("a new ") || titleLower.Contains("here's"))
        {
            raw *= 0.9;
        }

        // Recency: a gentle two-week half-life. A daily digest looks back several days, so a
        // steep decay would throw away perfectly relevant papers that are only a week old.
        var ageDays = Math.Max(0, (DateTimeOffset.UtcNow - article.PublishedAt).TotalDays);
        var recency = Math.Max(0.40, Math.Pow(0.5, ageDays / 12.0));

        // Source authority: peer-reviewed papers rank slightly above preprints and blog posts.
        var authority = article.SourceType switch
        {
            "paper" => 1.0,
            "preprint" => 0.94,
            "news" => 0.86,
            "community" => 0.68,
            _ => 0.80,
        };

        // Saturating curve: the strongest single keyword already means "this is about the
        // topic", additional keyword hits push the score up but with diminishing returns.
        var normalized = 1.0 - Math.Exp(-(raw + 0.5 * best) / 3.5);
        var score = normalized * recency * authority;

        return Math.Clamp(score, 0, 1);
    }

    /// <summary>Combines multiple topic scores: the article belongs to every topic it clears the bar for.</summary>
    public static (double Score, List<string> Topics) Evaluate(
        Article article, IReadOnlyList<Subscription> subscriptions)
    {
        var matched = new List<string>();
        var best = 0.0;

        foreach (var subscription in subscriptions)
        {
            var topic = TopicCatalog.Get(subscription.TopicId);
            if (topic is null) continue;

            var extra = SplitKeywords(subscription.ExtraKeywords);
            var exclude = SplitKeywords(subscription.ExcludeKeywords);
            var score = Score(article, topic, extra, exclude);
            if (score < subscription.MinScore) continue;
            matched.Add(topic.Id);
            best = Math.Max(best, score);
        }

        return (best, matched);
    }

    public static IReadOnlyList<string> SplitKeywords(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? Array.Empty<string>()
            : raw.Split(new[] { ',', '，', ';', '；', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => k.Length > 0)
                .ToList();
}
