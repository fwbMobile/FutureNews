namespace FutureNews.Core.Models;

/// <summary>Which LLM endpoint to use for one-line summaries.</summary>
public sealed class LlmSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Preset id (deepseek, dashscope, openai, moonshot, zhipu, siliconflow, ollama, custom).</summary>
    public string Provider { get; set; } = "deepseek";

    /// <summary>Override the preset base URL.</summary>
    public string BaseUrl { get; set; } = "";

    public string Model { get; set; } = "";

    /// <summary>Encrypted at rest with DPAPI (current user).</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>How many articles to summarise per LLM request.</summary>
    public int BatchSize { get; set; } = 8;

    /// <summary>Seconds before an LLM call is abandoned.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Write summaries in Chinese.</summary>
    public bool Chinese { get; set; } = true;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) || Provider == "ollama";
}

public sealed class NotifySettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>auto | balloon | toast | none</summary>
    public string Mode { get; set; } = "auto";

    public bool OnlyWhenMinimized { get; set; } = true;
}

public sealed class AppSettings
{
    /// <summary>Daily refresh time in local time, HH:mm.</summary>
    public string UpdateTime { get; set; } = "08:30";

    /// <summary>Master switch for the internal scheduler.</summary>
    public bool AutoUpdateEnabled { get; set; } = true;

    /// <summary>How many days back a run may look for new items.</summary>
    public int LookbackDays { get; set; } = 3;

    /// <summary>Maximum number of articles kept per topic per day.</summary>
    public int MaxPerTopicPerDay { get; set; } = 8;

    /// <summary>Maximum number of articles kept across all topics per day.</summary>
    public int MaxPerDay { get; set; } = 40;

    /// <summary>Articles older than this many days are purged from the database.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>Global minimum relevance score.</summary>
    public double MinScore { get; set; } = 0.30;

    /// <summary>
    /// Enable the OpenAlex source. Off by default: without a personal API key OpenAlex shares
    /// one small daily pool across every user on the same IP and quickly starts returning 429.
    /// </summary>
    public bool EnableOpenAlex { get; set; }

    /// <summary>Optional OpenAlex API key.</summary>
    public string OpenAlexApiKey { get; set; } = "";

    /// <summary>Contact address sent to OpenAlex / Crossref / PubMed for the polite pool.</summary>
    public string ContactEmail { get; set; } = "";

    public bool RunOnStartup { get; set; }

    public bool RegisterScheduledTask { get; set; } = true;

    public LlmSettings Llm { get; set; } = new();

    public NotifySettings Notify { get; set; } = new();
}
