using System.Security.Cryptography;
using System.Text;
using FutureNews.Core.Fetching;
using FutureNews.Core.Models;

namespace FutureNews.Core.Sources;

public static class ArticleFactory
{
    private static readonly string[] TrackingParams =
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "utm_id",
        "fbclid", "gclid", "mc_cid", "mc_eid", "igshid", "ref", "referrer", "cmpid",
        "ito", "spm", "share_source", "from", "at_medium", "at_campaign",
    };

    /// <summary>Removes tracking parameters and normalises the URL so the same story maps to one id.</summary>
    public static string NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        var raw = url.Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return raw;

        var kept = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !TrackingParams.Contains(p.Split('=')[0], StringComparer.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.Append(uri.Scheme.ToLowerInvariant()).Append("://").Append(uri.Host.ToLowerInvariant());
        if (!uri.IsDefaultPort) sb.Append(':').Append(uri.Port);
        sb.Append(uri.AbsolutePath.TrimEnd('/'));
        if (kept.Any()) sb.Append('?').Append(string.Join("&", kept));
        return sb.ToString();
    }

    /// <summary>Stable id derived from the canonical URL.</summary>
    public static string IdFor(string url)
    {
        var normalized = NormalizeUrl(url);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    public static Article Create(string url, string title, string sourceId, string sourceName, string sourceType)
    {
        var normalized = NormalizeUrl(url);
        return new Article
        {
            Id = IdFor(normalized),
            Url = normalized,
            Title = TextUtil.Collapse(title),
            SourceId = sourceId,
            SourceName = sourceName,
            SourceType = sourceType,
        };
    }
}
