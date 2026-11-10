namespace FutureNews.Core.Models;

/// <summary>A science-fiction inspired technology domain the user can subscribe to.</summary>
public sealed class Topic
{
    public required string Id { get; init; }

    /// <summary>Chinese display name.</summary>
    public required string Name { get; init; }

    /// <summary>English display name.</summary>
    public required string NameEn { get; init; }

    /// <summary>Short description shown in the subscription panel.</summary>
    public required string Description { get; init; }

    /// <summary>Accent colour used for the topic chip (hex, e.g. #4F8CFF).</summary>
    public required string Color { get; init; }

    /// <summary>Weighted keyword phrases. A phrase containing a space requires all words to be present.</summary>
    public required IReadOnlyList<(string Phrase, double Weight)> Keywords { get; init; }

    /// <summary>arXiv categories to query for this topic.</summary>
    public required IReadOnlyList<string> ArxivCategories { get; init; }

    /// <summary>General scientific search phrases used by Crossref / OpenAlex / EuropePMC.</summary>
    public required IReadOnlyList<string> SearchQueries { get; init; }

    /// <summary>Topic-scoped RSS feeds (URL -> source name).</summary>
    public required IReadOnlyList<(string Url, string Name)> Feeds { get; init; }

    /// <summary>Phrases that mark an article as low-value for this topic (e.g. "call for papers").</summary>
    public required IReadOnlyList<string> NegativeKeywords { get; init; }

    /// <summary>
    /// Very generic phrases that are only counted when at least one other (more specific)
    /// keyword of the topic also matches. "moon" alone should not turn a Pokémon article into
    /// space news, but "moon" together with "lander" or "regolith" certainly should.
    /// </summary>
    public IReadOnlyList<string> AmbiguousKeywords { get; init; } = Array.Empty<string>();
}
