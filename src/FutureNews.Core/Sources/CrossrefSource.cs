using System.Globalization;
using System.Text.Json;
using FutureNews.Core.Fetching;
using FutureNews.Core.Models;

namespace FutureNews.Core.Sources;

/// <summary>Crossref — DOI metadata for essentially every scholarly journal worldwide.</summary>
public sealed class CrossrefSource : IArticleSource
{
    private readonly HttpFetcher _http;

    public CrossrefSource(HttpFetcher http) => _http = http;

    public string Id => "crossref";

    public string DisplayName => "Crossref";

    public string SourceType => "paper";

    public TimeSpan MinDelay => TimeSpan.FromSeconds(1.2);

    public int MaxRetries => 1;

    public async Task<IReadOnlyList<Article>> FetchAsync(SourceQuery query, CancellationToken ct)
    {
        var results = new List<Article>();
        var since = query.Since.UtcDateTime.ToString("yyyy-MM-dd");
        var until = DateTimeOffset.UtcNow.UtcDateTime.AddDays(1).ToString("yyyy-MM-dd");

        foreach (var search in query.Topic.SearchQueries.Take(3))
        {
            ct.ThrowIfCancellationRequested();
            var url = "https://api.crossref.org/works?query=" + Uri.EscapeDataString(search) +
                      $"&filter=from-pub-date:{since},until-pub-date:{until},type:journal-article" +
                      $"&sort=published&order=desc&rows={Math.Clamp(query.Limit / 2, 5, 30)}";
            if (!string.IsNullOrWhiteSpace(query.ContactEmail)) url += "&mailto=" + Uri.EscapeDataString(query.ContactEmail);

            var json = await _http.GetStringAsync(url, ct, MaxRetries).ConfigureAwait(false);
            results.AddRange(Parse(json, query));
        }

        return results;
    }

    private static IEnumerable<Article> Parse(string json, SourceQuery query)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("message", out var message)) yield break;
        if (!message.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) yield break;

        foreach (var item in items.EnumerateArray())
        {
            var title = GetString(item, "title") is { Length: > 0 } t ? t : null;
            if (title is null) continue;

            var doi = GetString(item, "DOI");
            var url = GetString(item, "URL");
            if (string.IsNullOrWhiteSpace(url)) url = string.IsNullOrWhiteSpace(doi) ? null : "https://doi.org/" + doi;
            if (string.IsNullOrWhiteSpace(url)) continue;

            var article = ArticleFactory.Create(url, title, "crossref", "Crossref", "paper");
            article.OriginalId = doi;

            var abstractText = GetString(item, "abstract");
            if (!string.IsNullOrWhiteSpace(abstractText)) article.Abstract = TextUtil.Collapse(TextUtil.StripHtml(abstractText));

            article.Venue = GetString(item, "container-title");
            article.PublishedAt = ParsePublished(item);
            if (!query.IsInWindow(article.PublishedAt)) continue;
            article.Authors = item.TryGetProperty("author", out var authors) && authors.ValueKind == JsonValueKind.Array
                ? authors.EnumerateArray()
                    .Select(a => (GetString(a, "given") + " " + GetString(a, "family")).Trim())
                    .Where(n => n.Length > 1)
                    .Take(10)
                    .ToList()
                : new List<string>();

            yield return article;
        }
    }

    private static DateTimeOffset ParsePublished(JsonElement item)
    {
        foreach (var key in new[] { "published", "published-online", "published-print", "issued", "created" })
        {
            if (!item.TryGetProperty(key, out var node) || node.ValueKind != JsonValueKind.Object) continue;
            if (!node.TryGetProperty("date-parts", out var parts) || parts.ValueKind != JsonValueKind.Array) continue;
            var first = parts.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Array) continue;
            var nums = first.EnumerateArray().Select(x => x.TryGetInt32(out var v) ? v : 0).ToList();
            if (nums.Count == 0) continue;

            var year = nums[0] is > 1000 and < 3000 ? nums[0] : 0;
            var month = nums.Count > 1 && nums[1] is >= 1 and <= 12 ? nums[1] : 0;
            var day = nums.Count > 2 && nums[2] is >= 1 and <= 31 ? nums[2] : 0;

            if (year == 0) continue;
            try
            {
                if (month == 0) return DateUtil.ParseFlexible(year.ToString(CultureInfo.InvariantCulture));
                if (day == 0) return new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
                return new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);
            }
            catch (ArgumentOutOfRangeException)
            {
                // fall through to the next date field
            }
        }
        return DateTimeOffset.UtcNow;
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var node)) return null;
        return node.ValueKind switch
        {
            JsonValueKind.String => node.GetString(),
            JsonValueKind.Array => node.EnumerateArray().FirstOrDefault().ValueKind == JsonValueKind.String
                ? node.EnumerateArray().First().GetString()
                : null,
            _ => null,
        };
    }
}
