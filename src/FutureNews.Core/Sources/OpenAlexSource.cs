using System.Text.Json;
using FutureNews.Core.Fetching;
using FutureNews.Core.Models;

namespace FutureNews.Core.Sources;

/// <summary>OpenAlex — 240M+ works across journals, preprints, books and chapters.</summary>
public sealed class OpenAlexSource : IArticleSource
{
    private readonly HttpFetcher _http;

    public OpenAlexSource(HttpFetcher http) => _http = http;

    public string Id => "openalex";

    public string DisplayName => "OpenAlex";

    public string SourceType => "paper";

    public TimeSpan MinDelay => TimeSpan.FromSeconds(1.1);

    /// <summary>OpenAlex shares a small free daily pool, so retrying a rate-limited request is pointless.</summary>
    public int MaxRetries => 0;

    public async Task<IReadOnlyList<Article>> FetchAsync(SourceQuery query, CancellationToken ct)
    {
        var results = new List<Article>();
        var since = query.Since.UtcDateTime.ToString("yyyy-MM-dd");

        foreach (var search in query.Topic.SearchQueries.Take(3))
        {
            ct.ThrowIfCancellationRequested();
            var url = "https://api.openalex.org/works?search=" + Uri.EscapeDataString(search) +
                      $"&filter=from_publication_date:{since}" +
                      $"&sort=publication_date:desc&per-page={Math.Clamp(query.Limit / 2, 5, 25)}";
            if (!string.IsNullOrWhiteSpace(query.ApiKey)) url += "&api_key=" + Uri.EscapeDataString(query.ApiKey);
            else if (!string.IsNullOrWhiteSpace(query.ContactEmail)) url += "&mailto=" + Uri.EscapeDataString(query.ContactEmail);

            var json = await _http.GetStringAsync(url, ct, MaxRetries).ConfigureAwait(false);
            results.AddRange(Parse(json, query));
        }

        return results;
    }

    private static IEnumerable<Article> Parse(string json, SourceQuery query)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var item in results.EnumerateArray())
        {
            var title = GetString(item, "title") ?? GetString(item, "display_name");
            if (string.IsNullOrWhiteSpace(title)) continue;

            var url = GetString(item, "doi");
            if (string.IsNullOrWhiteSpace(url)) url = GetString(item, "id");

            if (string.IsNullOrWhiteSpace(url) && item.TryGetProperty("primary_location", out var loc))
            {
                url = GetString(loc, "landing_page_url");
            }

            if (string.IsNullOrWhiteSpace(url) && item.TryGetProperty("best_oa_location", out var oa))
            {
                url = GetString(oa, "landing_page_url") ?? GetString(oa, "pdf_url");
            }

            if (string.IsNullOrWhiteSpace(url)) continue;

            var article = ArticleFactory.Create(url, title, "openalex", "OpenAlex", "paper");
            article.OriginalId = GetString(item, "id");
            article.Abstract = RebuildAbstract(item);
            article.Venue = GetVenue(item);
            article.PublishedAt = ParseDate(GetString(item, "publication_date")) ?? DateTimeOffset.UtcNow;
            if (!query.IsInWindow(article.PublishedAt)) continue;

            if (item.TryGetProperty("authorships", out var authorship) && authorship.ValueKind == JsonValueKind.Array)
            {
                article.Authors = authorship.EnumerateArray()
                    .Select(a => GetString(a.GetProperty("author"), "display_name"))
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n!)
                    .Take(10)
                    .ToList();
            }

            yield return article;
        }
    }

    /// <summary>OpenAlex stores abstracts as an inverted index; rebuild the running text.</summary>
    private static string? RebuildAbstract(JsonElement item)
    {
        if (!item.TryGetProperty("abstract_inverted_index", out var index) || index.ValueKind != JsonValueKind.Object) return null;
        var positions = new Dictionary<int, string>();
        foreach (var property in index.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array) continue;
            foreach (var position in property.Value.EnumerateArray())
            {
                if (position.TryGetInt32(out var p)) positions[p] = property.Name;
            }
        }
        if (positions.Count == 0) return null;
        var ordered = positions.OrderBy(kv => kv.Key).Select(kv => kv.Value);
        return TextUtil.Collapse(string.Join(" ", ordered));
    }

    private static string? GetVenue(JsonElement item)
    {
        if (item.TryGetProperty("primary_location", out var loc) && loc.ValueKind == JsonValueKind.Object)
        {
            if (loc.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object)
            {
                var name = GetString(source, "display_name");
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        return null;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateUtil.ParseFlexible(value);
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.String) return null;
        var value = node.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
