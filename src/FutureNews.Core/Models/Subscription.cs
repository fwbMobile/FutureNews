namespace FutureNews.Core.Models;

/// <summary>Per-topic user preferences persisted in the database.</summary>
public sealed class Subscription
{
    public required string TopicId { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Minimum relevance score (0..1) an article must reach to be shown.</summary>
    public double MinScore { get; set; } = 0.30;

    /// <summary>Maximum number of articles kept per topic per daily run.</summary>
    public int MaxPerDay { get; set; } = 8;

    /// <summary>Extra user-supplied keywords (space separated, OR-ed with topic keywords).</summary>
    public string ExtraKeywords { get; set; } = "";

    /// <summary>User-supplied exclusion keywords.</summary>
    public string ExcludeKeywords { get; set; } = "";

    /// <summary>Restrict to these source ids (comma separated). Empty means all.</summary>
    public string SourceFilter { get; set; } = "";
}
