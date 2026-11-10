using System.Globalization;
using System.Xml.Linq;
using FutureNews.Core.Fetching;
using FutureNews.Core.Models;

namespace FutureNews.Core.Sources;

/// <summary>arXiv preprint server — the primary source for cutting-edge physics, CS and bio preprints.</summary>
public sealed class ArxivSource : IArticleSource
{
    private readonly HttpFetcher _http;

    public ArxivSource(HttpFetcher http) => _http = http;

    public string Id => "arxiv";

    public string DisplayName => "arXiv";

    public string SourceType => "preprint";

    public TimeSpan MinDelay => TimeSpan.FromSeconds(3.1);

    public int MaxRetries => 1;

    public async Task<IReadOnlyList<Article>> FetchAsync(SourceQuery query, CancellationToken ct)
    {
        var allPhrases = query.Topic.Keywords
            .Select(k => k.Phrase)
            .Concat(query.ExtraKeywords)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // arXiv indexes almost no CJK text, so Latin phrases give far better recall.
        var latin = allPhrases.Where(p => p.All(ch => ch <= 0x2E80)).ToList();
        var phrases = (latin.Count >= 3 ? latin : allPhrases).Take(12).ToList();

        if (phrases.Count == 0) return Array.Empty<Article>();

        var termList = string.Join(" OR ", phrases.Select(p => $"all:\"{p.Replace("\"", "")}\""));
        var catList = string.Join(" OR ", query.Topic.ArxivCategories.Select(c => $"cat:{c}"));

        // NOTE: arXiv's submittedDate range filter currently returns an empty result set,
        // so we rely on sorting by submission date and filter by date on the client side.
        var search = $"({termList}) AND ({catList})";
        var url = "http://export.arxiv.org/api/query?search_query=" + Uri.EscapeDataString(search) +
                  $"&start=0&max_results={Math.Clamp(query.Limit * 2, 2, 100)}&sortBy=submittedDate&sortOrder=descending";

        var xml = await _http.GetStringAsync(url, ct, MaxRetries).ConfigureAwait(false);
        var doc = XDocument.Parse(xml);

        var results = new List<Article>();
        foreach (var entry in doc.Descendants().Where(e => e.Name.LocalName == "entry"))
        {
            ct.ThrowIfCancellationRequested();

            var title = Value(entry, "title");
            if (string.IsNullOrWhiteSpace(title)) continue;

            var link = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "link" &&
                            string.Equals((string?)e.Attribute("rel"), "alternate", StringComparison.OrdinalIgnoreCase))
                        ?? entry.Elements().FirstOrDefault(e => e.Name.LocalName == "link");

            var absUrl = (string?)link?.Attribute("href");
            if (string.IsNullOrWhiteSpace(absUrl)) absUrl = Value(entry, "id");
            if (string.IsNullOrWhiteSpace(absUrl)) continue;

            var published = ParseDate(Value(entry, "published")) ?? DateTimeOffset.UtcNow;
            if (!query.IsInWindow(published, extraLookbackDays: 8)) continue;

            var article = ArticleFactory.Create(absUrl, title, Id, DisplayName, SourceType);
            article.Abstract = TextUtil.Collapse(Value(entry, "summary"));
            article.OriginalId = Value(entry, "id");
            article.PublishedAt = published;
            article.Authors = entry.Elements()
                .Where(e => e.Name.LocalName == "author")
                .Select(a => Value(a, "name"))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Take(12)
                .ToList();
            article.Venue = string.Join(", ", entry.Elements()
                .Where(e => e.Name.LocalName == "category")
                .Select(c => (string?)c.Attribute("term"))
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Take(6));
            results.Add(article);
        }

        return results;
    }

    private static string Value(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value?.Trim() ?? string.Empty;

    internal static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto)
            ? dto
            : null;
    }
}
