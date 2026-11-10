using FutureNews.Core.Models;

namespace FutureNews.Core.Sources;

/// <summary>Everything a source adapter needs to build its queries.</summary>
public sealed class SourceQuery
{
    public required Topic Topic { get; init; }

    /// <summary>Earliest publication date to accept.</summary>
    public required DateTimeOffset Since { get; init; }

    /// <summary>Maximum number of raw items to return per request.</summary>
    public int Limit { get; init; } = 25;

    /// <summary>Additional keywords typed by the user for this topic.</summary>
    public IReadOnlyList<string> ExtraKeywords { get; init; } = Array.Empty<string>();

    /// <summary>Contact address for the "polite pool" of scholarly APIs.</summary>
    public string ContactEmail { get; init; } = "";

    /// <summary>Optional API key (OpenAlex).</summary>
    public string ApiKey { get; init; } = "";

    /// <summary>
    /// Decides whether a publication date falls inside the wanted window. Sources apply this
    /// on the client side because several APIs either ignore or mis-apply their own date
    /// filters (and some records carry future issue dates).
    /// </summary>
    /// <param name="published">Publication timestamp reported by the source.</param>
    /// <param name="extraLookbackDays">
    /// Extra days of slack for sources with a known announcement lag — arXiv, for example,
    /// surfaces papers a few days after they were submitted.
    /// </param>
    public bool IsInWindow(DateTimeOffset published, int extraLookbackDays = 0)
    {
        // Allow a small grace period for timezone skew between the publisher and us.
        if (published < Since.AddHours(-36).AddDays(-extraLookbackDays)) return false;
        if (published > DateTimeOffset.UtcNow.AddHours(36)) return false;
        return true;
    }
}

/// <summary>A source adapter that can retrieve articles for a topic.</summary>
public interface IArticleSource
{
    /// <summary>Stable identifier, persisted with articles.</summary>
    string Id { get; }

    /// <summary>Human readable name shown in the UI.</summary>
    string DisplayName { get; }

    /// <summary>Broad category of the source: paper | preprint | news | community.</summary>
    string SourceType { get; }

    /// <summary>Minimum spacing between requests to this source's host.</summary>
    TimeSpan MinDelay { get; }

    /// <summary>How many times a failed request is retried.</summary>
    int MaxRetries => 2;

    Task<IReadOnlyList<Article>> FetchAsync(SourceQuery query, CancellationToken ct);
}
