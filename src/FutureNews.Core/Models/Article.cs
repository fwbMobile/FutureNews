namespace FutureNews.Core.Models;

/// <summary>A single piece of tech news or a paper, normalised across all sources.</summary>
public sealed class Article
{
    /// <summary>Stable identity: hash of the canonical URL.</summary>
    public required string Id { get; set; }

    public required string Url { get; set; }

    public required string Title { get; set; }

    /// <summary>Original abstract / description / body snippet, used for summarisation and scoring.</summary>
    public string? Abstract { get; set; }

    /// <summary>The single-sentence Chinese summary shown in the UI.</summary>
    public string? OneLiner { get; set; }

    /// <summary>How the one-liner was produced: llm | extractive | none.</summary>
    public string SummarySource { get; set; } = "none";

    public string SourceId { get; set; } = "";

    public string SourceName { get; set; } = "";

    /// <summary>paper | preprint | news | community</summary>
    public string SourceType { get; set; } = "unknown";

    public DateTimeOffset PublishedAt { get; set; }

    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Topic ids this article was matched to.</summary>
    public List<string> Topics { get; set; } = new();

    public List<string> Authors { get; set; } = new();

    public string? Venue { get; set; }

    public long SimHash { get; set; }

    public double Score { get; set; }

    public bool IsRead { get; set; }

    public bool IsStarred { get; set; }

    /// <summary>Local date (yyyy-MM-dd) of the digest this article first appeared in.</summary>
    public string? DigestDate { get; set; }

    public string? OriginalId { get; set; }

    public string TitleNormalized { get; set; } = "";
}
