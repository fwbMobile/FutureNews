using System.Text.Json;
using FutureNews.Core.Fetching;
using FutureNews.Core.Models;

namespace FutureNews.Core.Sources;

/// <summary>Hacker News via the Algolia API — picks up items the mainstream press ignores.</summary>
public sealed class HackerNewsSource : IArticleSource
{
    private readonly HttpFetcher _http;

    public HackerNewsSource(HttpFetcher http) => _http = http;

    public string Id => "hackernews";

    public string DisplayName => "Hacker News";

    public string SourceType => "community";

    public TimeSpan MinDelay => TimeSpan.FromMilliseconds(500);

    public int MaxRetries => 1;

    public async Task<IReadOnlyList<Article>> FetchAsync(SourceQuery query, CancellationToken ct)
    {
        var results = new List<Article>();
        var since = query.Since.ToUnixTimeSeconds();

        var terms = query.Topic.Keywords.Select(k => k.Phrase)
            .Concat(query.ExtraKeywords)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        if (terms.Count == 0) return results;

        foreach (var term in terms.Take(4))
        {
            ct.ThrowIfCancellationRequested();
            var url = "https://hn.algolia.com/api/v1/search_by_date?query=" + Uri.EscapeDataString(term) +
                      $"&tags=story&numericFilters=" + Uri.EscapeDataString($"created_at_i>{since},points>20") +
                      $"&hitsPerPage={Math.Clamp(query.Limit / 3, 5, 20)}";

            try
            {
                var json = await _http.GetStringAsync(url, ct, MaxRetries).ConfigureAwait(false);
                results.AddRange(Parse(json, query));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // ignore transient failures for one term
            }
        }

        return results;
    }

    private static IEnumerable<Article> Parse(string json, SourceQuery query)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array) yield break;

        foreach (var hit in hits.EnumerateArray())
        {
            var title = GetString(hit, "title") ?? GetString(hit, "story_title");
            if (string.IsNullOrWhiteSpace(title)) continue;

            var url = GetString(hit, "url") ?? GetString(hit, "story_url");
            var objectId = GetString(hit, "objectID");
            if (string.IsNullOrWhiteSpace(url))
            {
                if (string.IsNullOrWhiteSpace(objectId)) continue;
                url = "https://news.ycombinator.com/item?id=" + objectId;
            }

            var article = ArticleFactory.Create(url, title, "hackernews", "Hacker News", "community");
            article.OriginalId = objectId;
            article.Abstract = TextUtil.Collapse(TextUtil.StripHtml(GetString(hit, "story_text")));

            var comments = GetInt(hit, "num_comments");
            var points = GetInt(hit, "points");
            if (points > 0 || comments > 0)
            {
                article.Abstract = (article.Abstract is { Length: > 0 } ? article.Abstract + " " : string.Empty) +
                                   $"(HN {points} 分 / {comments} 评论)";
            }

            article.PublishedAt = GetString(hit, "created_at") is { Length: > 0 } created
                ? DateTimeOffset.TryParse(created, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var dto) ? dto : DateTimeOffset.UtcNow
                : DateTimeOffset.UtcNow;

            if (!query.IsInWindow(article.PublishedAt)) continue;

            yield return article;
        }
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.String) return null;
        var value = node.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.Number) return 0;
        return node.TryGetInt32(out var v) ? v : 0;
    }
}
