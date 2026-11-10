using System.Text.Json;
using FutureNews.Core.Fetching;
using FutureNews.Core.Models;

namespace FutureNews.Core.Sources;

/// <summary>Europe PMC — biomedical literature, preprints and clinical research across Europe.</summary>
public sealed class EuropePmcSource : IArticleSource
{
    private readonly HttpFetcher _http;

    public EuropePmcSource(HttpFetcher http) => _http = http;

    public string Id => "europepmc";

    public string DisplayName => "Europe PMC";

    public string SourceType => "paper";

    public TimeSpan MinDelay => TimeSpan.FromSeconds(1.1);

    public int MaxRetries => 1;

    public async Task<IReadOnlyList<Article>> FetchAsync(SourceQuery query, CancellationToken ct)
    {
        var results = new List<Article>();
        var since = query.Since.UtcDateTime.ToString("yyyy-MM-dd");
        var until = DateTimeOffset.UtcNow.UtcDateTime.AddDays(1).ToString("yyyy-MM-dd");
        var keywords = query.Topic.Keywords.Select(k => k.Phrase)
            .Concat(query.ExtraKeywords)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .Select(k => "\"" + k.Replace("\"", "") + "\"")
            .ToList();

        if (keywords.Count == 0) return results;

        var term = string.Join(" OR ", keywords);

        foreach (var search in query.Topic.SearchQueries.Take(2))
        {
            ct.ThrowIfCancellationRequested();

            // The date range must stay outside parentheses: Europe PMC's parser rejects
            // "(... ) AND (FIRST_PDATE:[...])" and silently returns nothing.
            var url = "https://www.ebi.ac.uk/europepmc/webservices/rest/search?query=" +
                      Uri.EscapeDataString($"({term}) AND FIRST_PDATE:[{since} TO {until}]") +
                      $"&format=json&pageSize={Math.Clamp(query.Limit / 2, 5, 25)}&sort={Uri.EscapeDataString("FIRST_PDATE_D desc")}&resultType=core";

            var json = await _http.GetStringAsync(url, ct, MaxRetries).ConfigureAwait(false);
            results.AddRange(Parse(json, query));
        }

        return results;
    }

    private static IEnumerable<Article> Parse(string json, SourceQuery query)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("resultList", out var list) || !list.TryGetProperty("result", out var items))
            yield break;
        if (items.ValueKind != JsonValueKind.Array) yield break;

        foreach (var item in items.EnumerateArray())
        {
            var title = GetString(item, "title");
            if (string.IsNullOrWhiteSpace(title)) continue;

            var doi = GetString(item, "doi");
            var pmid = GetString(item, "pmid");
            var id = GetString(item, "id");
            var source = GetString(item, "source") ?? "MED";

            var url = !string.IsNullOrWhiteSpace(doi)
                ? "https://doi.org/" + doi
                : !string.IsNullOrWhiteSpace(pmid)
                    ? "https://pubmed.ncbi.nlm.nih.gov/" + pmid + "/"
                    : !string.IsNullOrWhiteSpace(id)
                        ? $"https://europepmc.org/article/{source}/{id}"
                        : null;

            if (string.IsNullOrWhiteSpace(url)) continue;

            var type = source.Equals("PPR", StringComparison.OrdinalIgnoreCase) ? "preprint" : "paper";
            var article = ArticleFactory.Create(url, title, "europepmc", "Europe PMC", type);
            article.OriginalId = id;
            article.Abstract = TextUtil.Collapse(GetString(item, "abstractText"));
            article.Venue = GetString(item, "journalTitle") ?? source;
            article.PublishedAt = ParseDate(GetString(item, "firstPublicationDate")) ?? DateTimeOffset.UtcNow;
            if (!query.IsInWindow(article.PublishedAt)) continue;

            var authors = GetString(item, "authorString");
            if (!string.IsNullOrWhiteSpace(authors))
            {
                article.Authors = authors.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Trim())
                    .Where(a => a.Length > 2)
                    .Take(10)
                    .ToList();
            }

            yield return article;
        }
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateUtil.ParseFlexible(value);
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var node)) return null;
        if (node.ValueKind != JsonValueKind.String) return null;
        var value = node.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
