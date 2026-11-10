using System.Diagnostics;
using FutureNews.Core.Diagnostics;
using FutureNews.Core.Fetching;
using FutureNews.Core.Models;
using FutureNews.Core.Processing;
using FutureNews.Core.Sources;
using FutureNews.Core.Storage;
using FutureNews.Core.Summarizing;
using FutureNews.Core.Topics;

namespace FutureNews.Core.Pipeline;

/// <summary>Outcome of a single daily run.</summary>
public sealed class DigestResult
{
    public required string Date { get; init; }

    public List<Article> Articles { get; set; } = new();

    public int Fetched { get; set; }

    public int Duplicate { get; set; }

    public int Filtered { get; set; }

    public int Persisted { get; set; }

    public SummaryOutcome? Summary { get; set; }

    public List<string> Errors { get; } = new();

    /// <summary>Raw items fetched per source id.</summary>
    public Dictionary<string, int> SourceCounts { get; } = new();

    /// <summary>Articles matched per topic id.</summary>
    public Dictionary<string, int> TopicCounts { get; } = new();

    /// <summary>Articles kept per topic id after the daily caps.</summary>
    public Dictionary<string, int> KeptPerTopic { get; } = new();

    public TimeSpan Duration { get; set; }

    public IReadOnlyDictionary<string, string> SourceStatus => Health.Snapshot();

    internal SourceHealth Health { get; set; } = new();
}

/// <summary>
/// Runs the daily update: fetch from every enabled source, score against the subscribed
/// topics, drop duplicates and near-duplicates, summarise, then persist the digest.
/// </summary>
public sealed class DigestPipeline
{
    private readonly ArticleRepository _repository;
    private readonly SettingsStore _settingsStore;
    private readonly HttpFetcher _http;
    private readonly SourceHealth _health = new();

    public DigestPipeline(ArticleRepository repository, SettingsStore settingsStore, HttpFetcher? http = null)
    {
        _repository = repository;
        _settingsStore = settingsStore;
        _http = http ?? new HttpFetcher(DelayFor);
    }

    private static TimeSpan DelayFor(string host) => host.ToLowerInvariant() switch
    {
        "export.arxiv.org" or "arxiv.org" => TimeSpan.FromSeconds(3.1),
        "api.crossref.org" => TimeSpan.FromSeconds(1.2),
        "api.openalex.org" => TimeSpan.FromSeconds(1.1),
        "www.ebi.ac.uk" => TimeSpan.FromSeconds(1.1),
        "hn.algolia.com" => TimeSpan.FromMilliseconds(600),
        _ => TimeSpan.FromMilliseconds(500),
    };

    public async Task<DigestResult> RunAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var settings = _settingsStore.Load();
        var result = new DigestResult { Date = DateTime.Now.ToString("yyyy-MM-dd") };
        result.Health = _health;

        var subscriptions = ResolveSubscriptions(settings);
        var active = subscriptions.Where(s => s.Enabled && TopicCatalog.Exists(s.TopicId)).ToList();
        if (active.Count == 0)
        {
            result.Errors.Add("没有启用任何领域");
            return result;
        }

        var since = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, settings.LookbackDays));

        // A source that failed yesterday should not be retried at length today.
        _health.Restore(_repository.LoadSourceHealth());

        // 1. Fetch from every source for every enabled topic, in parallel.
        var fetchTasks = new List<Task<(string SourceId, IReadOnlyList<Article> Articles)>>();
        foreach (var subscription in active)
        {
            var topic = TopicCatalog.Get(subscription.TopicId)!;
            var query = new SourceQuery
            {
                Topic = topic,
                Since = since,
                Limit = 30,
                ExtraKeywords = RelevanceScorer.SplitKeywords(subscription.ExtraKeywords),
                ContactEmail = settings.ContactEmail,
                ApiKey = settings.OpenAlexApiKey,
            };

            foreach (var (sourceId, factory) in BuildSources(topic, settings, ct))
            {
                if (!_health.IsAvailable(sourceId)) continue;
                fetchTasks.Add(FetchOneAsync(sourceId, factory(query), ct));
            }
        }

        var fetched = await Task.WhenAll(fetchTasks).ConfigureAwait(false);

        // 2. Merge, keeping the richest metadata per URL.
        var merged = new Dictionary<string, Article>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sourceId, articles) in fetched)
        {
            result.SourceCounts[sourceId] = result.SourceCounts.GetValueOrDefault(sourceId) + articles.Count;
            foreach (var article in articles)
            {
                if (string.IsNullOrWhiteSpace(article.Url) || string.IsNullOrWhiteSpace(article.Title)) continue;
                if (merged.TryGetValue(article.Url, out var existing))
                {
                    MergeInto(existing, article);
                }
                else
                {
                    merged[article.Url] = article;
                }
            }
        }
        result.Fetched = merged.Count;

        // 3. Score against the subscribed topics.
        var scored = new List<(Article Article, double Score, List<string> Topics)>();
        foreach (var article in merged.Values)
        {
            var (score, topics) = RelevanceScorer.Evaluate(article, active);
            if (topics.Count == 0) { result.Filtered++; continue; }
            foreach (var topicId in topics)
            {
                result.TopicCounts[topicId] = result.TopicCounts.GetValueOrDefault(topicId) + 1;
            }
            scored.Add((article, score, topics));
        }

        // 4. Drop anything already stored or seen before (cross-day near-duplicate check).
        var seen = _repository.GetSeenHashes(DateTimeOffset.UtcNow.AddDays(-45));
        var kept = new List<(Article Article, double Score, List<string> Topics)>();
        foreach (var (article, score, topics) in scored)
        {
            article.TitleNormalized = TextUtil.NormalizeTitle(article.Title);
            article.SimHash = SimHash.Compute(article.TitleNormalized);

            if (_repository.Exists(article.Id)) { result.Duplicate++; continue; }
            if (IsNearDuplicate(article, seen)) { result.Duplicate++; continue; }

            article.Score = score;
            article.Topics = topics;
            kept.Add((article, score, topics));
        }

        // 5. Apply per-topic and global daily caps.
        var perTopic = active.ToDictionary(s => s.TopicId, s => s.MaxPerDay);
        var globalCap = Math.Max(1, settings.MaxPerDay);
        var selected = new List<Article>();
        var topicCounts = new Dictionary<string, int>();

        foreach (var (article, _, topics) in kept.OrderByDescending(k => k.Score).ThenByDescending(k => k.Article.PublishedAt))
        {
            if (selected.Count >= globalCap) break;

            var admitted = false;
            foreach (var topicId in topics)
            {
                var cap = perTopic.TryGetValue(topicId, out var c) ? c : settings.MaxPerTopicPerDay;
                var used = topicCounts.GetValueOrDefault(topicId);
                if (used >= cap) continue;
                topicCounts[topicId] = used + 1;
                admitted = true;
            }

            if (!admitted) continue;
            selected.Add(article);
            foreach (var topicId in topics)
            {
                result.KeptPerTopic[topicId] = result.KeptPerTopic.GetValueOrDefault(topicId) + 1;
            }
        }

        // 6. Summarise.
        var keywords = active.SelectMany(s => TopicCatalog.Get(s.TopicId)!.Keywords.Select(k => k.Phrase)).Distinct().ToList();
        var summaryService = new SummaryService(keywords);
        result.Summary = await summaryService.SummarizeAsync(selected, settings, ct).ConfigureAwait(false);
        if (result.Summary.Error is not null) result.Errors.Add("摘要： " + result.Summary.Error);

        // 7. Persist.
        var today = result.Date;
        foreach (var article in selected)
        {
            article.DigestDate = today;
            article.FetchedAt = DateTimeOffset.UtcNow;
        }

        _repository.SaveArticles(selected);
        foreach (var article in selected) _repository.RememberSeen(article);
        result.Persisted = selected.Count;
        result.Articles = selected;

        _repository.PruneSeen(DateTimeOffset.UtcNow.AddDays(-45));
        _repository.PurgeOlderThan(DateTimeOffset.UtcNow.AddDays(-Math.Max(7, settings.RetentionDays)));
        _repository.SaveSourceHealth(_health.Export());

        sw.Stop();
        result.Duration = sw.Elapsed;
        return result;
    }

    private async Task<(string SourceId, IReadOnlyList<Article> Articles)> FetchOneAsync(
        string sourceId, Task<IReadOnlyList<Article>> task, CancellationToken ct)
    {
        try
        {
            var articles = await task.ConfigureAwait(false);
            _health.RecordSuccess(sourceId);
            return (sourceId, articles);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _health.RecordFailure(sourceId, ex.Message);
            return (sourceId, Array.Empty<Article>());
        }
    }

    private IEnumerable<(string SourceId, Func<SourceQuery, Task<IReadOnlyList<Article>>> Factory)> BuildSources(Topic topic, AppSettings settings, CancellationToken ct)
    {
        yield return ("arxiv", q => new ArxivSource(_http).FetchAsync(q, ct));
        yield return ("crossref", q => new CrossrefSource(_http).FetchAsync(q, ct));
        yield return ("europepmc", q => new EuropePmcSource(_http).FetchAsync(q, ct));
        yield return ("rss", q => RssSource.For(_http, topic.Feeds).FetchAsync(q, ct));
        yield return ("hackernews", q => new HackerNewsSource(_http).FetchAsync(q, ct));

        if (settings.EnableOpenAlex)
        {
            yield return ("openalex", q => new OpenAlexSource(_http).FetchAsync(q, ct));
        }
    }

    private List<Subscription> ResolveSubscriptions(AppSettings settings)
    {
        var stored = _repository.GetSubscriptions().ToDictionary(s => s.TopicId, StringComparer.OrdinalIgnoreCase);
        var result = new List<Subscription>();

        foreach (var topic in TopicCatalog.All)
        {
            if (stored.TryGetValue(topic.Id, out var existing))
            {
                result.Add(existing);
                continue;
            }

            result.Add(new Subscription
            {
                TopicId = topic.Id,
                Enabled = false,
                MinScore = settings.MinScore,
                MaxPerDay = settings.MaxPerTopicPerDay,
            });
        }

        return result;
    }

    private static bool IsNearDuplicate(Article article, IReadOnlyList<(long Hash, string TitleNorm)> seen)
    {
        foreach (var (hash, titleNorm) in seen)
        {
            if (!string.IsNullOrEmpty(titleNorm) && string.Equals(titleNorm, article.TitleNormalized, StringComparison.Ordinal))
                return true;
            if (SimHash.HammingDistance(hash, article.SimHash) <= 6)
                return true;
        }
        return false;
    }

    private static void MergeInto(Article existing, Article incoming)
    {
        if (string.IsNullOrWhiteSpace(existing.Abstract) && !string.IsNullOrWhiteSpace(incoming.Abstract))
            existing.Abstract = incoming.Abstract;
        if (existing.Authors.Count == 0 && incoming.Authors.Count > 0) existing.Authors = incoming.Authors;
        if (string.IsNullOrWhiteSpace(existing.Venue) && !string.IsNullOrWhiteSpace(incoming.Venue)) existing.Venue = incoming.Venue;
        if (incoming.PublishedAt > existing.PublishedAt) existing.PublishedAt = incoming.PublishedAt;
        if (incoming.SourceType == "paper" && existing.SourceType != "paper")
        {
            existing.SourceType = incoming.SourceType;
            existing.SourceName = incoming.SourceName;
        }
    }
}
