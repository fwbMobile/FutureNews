using System.Xml.Linq;
using FutureNews.Core.Fetching;
using FutureNews.Core.Models;

namespace FutureNews.Core.Sources;

/// <summary>Generic RSS 2.0 / Atom / RDF feed reader used for news sources.</summary>
public sealed class RssSource : IArticleSource
{
    private readonly HttpFetcher _http;
    private readonly List<(string Url, string Name)> _feeds;

    public RssSource(HttpFetcher http, IEnumerable<(string Url, string Name)> feeds)
    {
        _http = http;
        _feeds = feeds.ToList();
    }

    public string Id => "rss";

    public string DisplayName => "新闻源";

    public string SourceType => "news";

    public TimeSpan MinDelay => TimeSpan.FromMilliseconds(600);

    public int MaxRetries => 1;

    /// <summary>Builds a reader for a specific feed list.</summary>
    public static RssSource For(HttpFetcher http, IEnumerable<(string Url, string Name)> feeds) => new(http, feeds);

    public async Task<IReadOnlyList<Article>> FetchAsync(SourceQuery query, CancellationToken ct)
    {
        var results = new List<Article>();

        foreach (var (url, name) in _feeds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var xml = await _http.GetStringAsync(url, ct, MaxRetries).ConfigureAwait(false);
                results.AddRange(Parse(xml, name, query));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // A single broken feed must not abort the whole run.
            }
        }

        return results;
    }

    internal static IEnumerable<Article> Parse(string xml, string feedName, SourceQuery query)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (Exception)
        {
            yield break;
        }

        var entries = doc.Descendants().Where(e => e.Name.LocalName is "item" or "entry").ToList();

        foreach (var entry in entries)
        {
            var title = ChildText(entry, "title");
            if (string.IsNullOrWhiteSpace(title)) continue;

            var link = ExtractLink(entry);
            if (string.IsNullOrWhiteSpace(link)) continue;

            var summary = ChildText(entry, "description")
                          ?? ChildText(entry, "summary")
                          ?? ChildText(entry, "encoded")
                          ?? ChildText(entry, "content");

            var published = ParseDate(ChildText(entry, "pubDate"))
                            ?? ParseDate(ChildText(entry, "published"))
                            ?? ParseDate(ChildText(entry, "updated"))
                            ?? ParseDate(ChildText(entry, "date"))
                            ?? DateTimeOffset.UtcNow;

            if (!query.IsInWindow(published)) continue;

            var article = ArticleFactory.Create(link, title, "rss", feedName, "news");
            article.Abstract = TextUtil.Collapse(TextUtil.StripHtml(summary));
            article.PublishedAt = published;
            article.Authors = new List<string> { feedName };
            yield return article;
        }
    }

    private static string? ChildText(XElement parent, string localName)
    {
        var child = parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName);
        if (child is null) return null;
        var value = child.Value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ExtractLink(XElement entry)
    {
        var linkElement = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "link");
        if (linkElement is not null)
        {
            var href = (string?)linkElement.Attribute("href");
            if (!string.IsNullOrWhiteSpace(href)) return href;
            var text = linkElement.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(text) && text.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return text;
        }

        var guid = ChildText(entry, "guid");
        if (!string.IsNullOrWhiteSpace(guid) && guid.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return guid;

        return ChildText(entry, "link");
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var dto))
            return dto;
        return null;
    }
}
