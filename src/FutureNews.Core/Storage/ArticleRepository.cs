using System.Text.Json;
using FutureNews.Core.Models;

namespace FutureNews.Core.Storage;

/// <summary>Reads and writes articles, subscriptions and settings.</summary>
public sealed class ArticleRepository
{
    private readonly Database _db;

    public ArticleRepository(Database db) => _db = db;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------------------------------------------------------------- articles

    public void UpsertArticle(Article article)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO articles (id, url, title, abstract, one_liner, summary_source, source_id, source_name,
                                  source_type, published_at, fetched_at, topics, authors, venue, simhash, score,
                                  is_read, is_starred, digest_date, original_id, title_norm)
            VALUES ($id, $url, $title, $abstract, $oneLiner, $summarySource, $sourceId, $sourceName,
                    $sourceType, $publishedAt, $fetchedAt, $topics, $authors, $venue, $simhash, $score,
                    $isRead, $isStarred, $digestDate, $originalId, $titleNorm)
            ON CONFLICT(id) DO UPDATE SET
                one_liner = COALESCE(excluded.one_liner, articles.one_liner),
                summary_source = COALESCE(excluded.summary_source, articles.summary_source),
                score = MAX(articles.score, excluded.score),
                topics = excluded.topics,
                venue = COALESCE(excluded.venue, articles.venue),
                abstract = COALESCE(articles.abstract, excluded.abstract);
            """;
        cmd.Parameters.AddWithValue("$id", article.Id);
        cmd.Parameters.AddWithValue("$url", article.Url);
        cmd.Parameters.AddWithValue("$title", article.Title);
        cmd.Parameters.AddWithValue("$abstract", (object?)article.Abstract ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$oneLiner", (object?)article.OneLiner ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$summarySource", article.SummarySource);
        cmd.Parameters.AddWithValue("$sourceId", article.SourceId);
        cmd.Parameters.AddWithValue("$sourceName", article.SourceName);
        cmd.Parameters.AddWithValue("$sourceType", article.SourceType);
        cmd.Parameters.AddWithValue("$publishedAt", article.PublishedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$fetchedAt", article.FetchedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$topics", JsonSerializer.Serialize(article.Topics, Json));
        cmd.Parameters.AddWithValue("$authors", JsonSerializer.Serialize(article.Authors, Json));
        cmd.Parameters.AddWithValue("$venue", (object?)article.Venue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$simhash", article.SimHash);
        cmd.Parameters.AddWithValue("$score", article.Score);
        cmd.Parameters.AddWithValue("$isRead", article.IsRead ? 1 : 0);
        cmd.Parameters.AddWithValue("$isStarred", article.IsStarred ? 1 : 0);
        cmd.Parameters.AddWithValue("$digestDate", (object?)article.DigestDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$originalId", (object?)article.OriginalId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$titleNorm", article.TitleNormalized);
        cmd.ExecuteNonQuery();
    }

    public void SaveArticles(IEnumerable<Article> articles)
    {
        using var tx = _db.Connection.BeginTransaction();
        foreach (var article in articles) UpsertArticle(article);
        tx.Commit();
    }

    public List<Article> GetArticles(int limit = 500, string? topicId = null, bool unreadOnly = false, bool starredOnly = false)
    {
        var sql = "SELECT * FROM articles WHERE 1=1";
        if (unreadOnly) sql += " AND is_read = 0";
        if (starredOnly) sql += " AND is_starred = 1";
        sql += " ORDER BY is_starred DESC, published_at DESC, score DESC LIMIT $limit";

        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$limit", limit);

        var list = new List<Article>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var article = ReadArticle(reader);
            if (topicId is not null && !article.Topics.Contains(topicId)) continue;
            list.Add(article);
        }
        return list;
    }

    public List<Article> GetArticlesByDate(string date)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM articles WHERE digest_date = $d ORDER BY score DESC, published_at DESC";
        cmd.Parameters.AddWithValue("$d", date);
        var list = new List<Article>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(ReadArticle(reader));
        return list;
    }

    public void MarkRead(string id, bool isRead)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE articles SET is_read = $r WHERE id = $id";
        cmd.Parameters.AddWithValue("$r", isRead ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void MarkAllRead(string? topicId = null)
    {
        using var cmd = _db.Connection.CreateCommand();
        if (topicId is null)
        {
            cmd.CommandText = "UPDATE articles SET is_read = 1 WHERE is_read = 0";
        }
        else
        {
            cmd.CommandText = "UPDATE articles SET is_read = 1 WHERE is_read = 0 AND topics LIKE $t";
            cmd.Parameters.AddWithValue("$t", $"%\"{topicId}\"%");
        }
        cmd.ExecuteNonQuery();
    }

    public void ToggleStar(string id)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE articles SET is_starred = CASE WHEN is_starred = 1 THEN 0 ELSE 1 END WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public bool Exists(string id)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM articles WHERE id = $id LIMIT 1";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteScalar() is not null;
    }

    private static Article ReadArticle(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        Url = r.GetString(r.GetOrdinal("url")),
        Title = r.GetString(r.GetOrdinal("title")),
        Abstract = r.IsDBNull(r.GetOrdinal("abstract")) ? null : r.GetString(r.GetOrdinal("abstract")),
        OneLiner = r.IsDBNull(r.GetOrdinal("one_liner")) ? null : r.GetString(r.GetOrdinal("one_liner")),
        SummarySource = r.IsDBNull(r.GetOrdinal("summary_source")) ? "none" : r.GetString(r.GetOrdinal("summary_source")),
        SourceId = r.GetString(r.GetOrdinal("source_id")),
        SourceName = r.GetString(r.GetOrdinal("source_name")),
        SourceType = r.GetString(r.GetOrdinal("source_type")),
        PublishedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("published_at")), null, System.Globalization.DateTimeStyles.RoundtripKind),
        FetchedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("fetched_at")), null, System.Globalization.DateTimeStyles.RoundtripKind),
        Topics = DeserializeList(r.GetString(r.GetOrdinal("topics"))),
        Authors = DeserializeList(r.GetString(r.GetOrdinal("authors"))),
        Venue = r.IsDBNull(r.GetOrdinal("venue")) ? null : r.GetString(r.GetOrdinal("venue")),
        SimHash = r.GetInt64(r.GetOrdinal("simhash")),
        Score = r.GetDouble(r.GetOrdinal("score")),
        IsRead = r.GetInt64(r.GetOrdinal("is_read")) == 1,
        IsStarred = r.GetInt64(r.GetOrdinal("is_starred")) == 1,
        DigestDate = r.IsDBNull(r.GetOrdinal("digest_date")) ? null : r.GetString(r.GetOrdinal("digest_date")),
        OriginalId = r.IsDBNull(r.GetOrdinal("original_id")) ? null : r.GetString(r.GetOrdinal("original_id")),
        TitleNormalized = r.IsDBNull(r.GetOrdinal("title_norm")) ? "" : r.GetString(r.GetOrdinal("title_norm")),
    };

    private static List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try { return JsonSerializer.Deserialize<List<string>>(json, Json) ?? new List<string>(); }
        catch (JsonException) { return new List<string>(); }
    }

    // ------------------------------------------------------------ seen hashes

    public void RememberSeen(Article article)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO seen_hashes (hash, title_norm, first_seen_at, article_id) VALUES ($h, $t, $d, $a)";
        cmd.Parameters.AddWithValue("$h", article.SimHash);
        cmd.Parameters.AddWithValue("$t", article.TitleNormalized);
        cmd.Parameters.AddWithValue("$d", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$a", article.Id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Returns seen entries newer than the given cut-off for near-duplicate checks.</summary>
    public List<(long Hash, string TitleNorm)> GetSeenHashes(DateTimeOffset since)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT hash, title_norm FROM seen_hashes WHERE first_seen_at >= $since";
        cmd.Parameters.AddWithValue("$since", since.ToString("O"));
        var list = new List<(long, string)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add((reader.GetInt64(0), reader.IsDBNull(1) ? "" : reader.GetString(1)));
        }
        return list;
    }

    public void PruneSeen(DateTimeOffset before)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM seen_hashes WHERE first_seen_at < $since";
        cmd.Parameters.AddWithValue("$since", before.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    // ---------------------------------------------------------- subscriptions

    public List<Subscription> GetSubscriptions()
    {
        var result = new List<Subscription>();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM subscriptions";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new Subscription
            {
                TopicId = reader.GetString(0),
                Enabled = reader.GetInt64(1) == 1,
                MinScore = reader.GetDouble(2),
                MaxPerDay = reader.GetInt32(3),
                ExtraKeywords = reader.IsDBNull(4) ? "" : reader.GetString(4),
                ExcludeKeywords = reader.IsDBNull(5) ? "" : reader.GetString(5),
                SourceFilter = reader.IsDBNull(6) ? "" : reader.GetString(6),
            });
        }
        return result;
    }

    public void SaveSubscription(Subscription subscription)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO subscriptions (topic_id, enabled, min_score, max_per_day, extra_keywords, exclude_keywords, source_filter)
            VALUES ($t, $e, $m, $d, $x, $xc, $sf)
            ON CONFLICT(topic_id) DO UPDATE SET
                enabled = excluded.enabled, min_score = excluded.min_score, max_per_day = excluded.max_per_day,
                extra_keywords = excluded.extra_keywords, exclude_keywords = excluded.exclude_keywords,
                source_filter = excluded.source_filter;
            """;
        cmd.Parameters.AddWithValue("$t", subscription.TopicId);
        cmd.Parameters.AddWithValue("$e", subscription.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$m", subscription.MinScore);
        cmd.Parameters.AddWithValue("$d", subscription.MaxPerDay);
        cmd.Parameters.AddWithValue("$x", subscription.ExtraKeywords);
        cmd.Parameters.AddWithValue("$xc", subscription.ExcludeKeywords);
        cmd.Parameters.AddWithValue("$sf", subscription.SourceFilter);
        cmd.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ runs

    public long StartRun()    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO runs (started_at) VALUES ($s); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$s", DateTimeOffset.UtcNow.ToString("O"));
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public void FinishRun(long id, int fetched, int kept, string? error)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE runs SET finished_at = $f, fetched = $fe, kept = $k, error = $e WHERE id = $id";
        cmd.Parameters.AddWithValue("$f", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$fe", fetched);
        cmd.Parameters.AddWithValue("$k", kept);
        cmd.Parameters.AddWithValue("$e", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public int PurgeOlderThan(DateTimeOffset cutoff)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM articles WHERE fetched_at < $c";
        cmd.Parameters.AddWithValue("$c", cutoff.ToString("O"));
        return cmd.ExecuteNonQuery();
    }

    // ---------------------------------------------------------- source health

    /// <summary>Loads the persisted reliability state so a broken source stays skipped between runs.</summary>
    public Dictionary<string, (int Failures, DateTimeOffset? DisabledUntil, string Error)> LoadSourceHealth()
    {
        var result = new Dictionary<string, (int, DateTimeOffset?, string)>(StringComparer.OrdinalIgnoreCase);
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT source_id, failures, disabled_until, last_error FROM source_health";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            DateTimeOffset? until = null;
            if (!reader.IsDBNull(2) &&
                DateTimeOffset.TryParse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
            {
                until = parsed;
            }
            result[reader.GetString(0)] = (reader.GetInt32(1), until, reader.IsDBNull(3) ? "" : reader.GetString(3));
        }
        return result;
    }

    /// <summary>Persists the reliability state of every source.</summary>
    public void SaveSourceHealth(IReadOnlyDictionary<string, (int Failures, DateTimeOffset? DisabledUntil, string Error, DateTimeOffset LastSuccess)> states)
    {
        using var tx = _db.Connection.BeginTransaction();
        foreach (var (sourceId, state) in states)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO source_health (source_id, failures, disabled_until, last_error, last_success)
                VALUES ($id, $f, $u, $e, $s)
                ON CONFLICT(source_id) DO UPDATE SET
                    failures = excluded.failures, disabled_until = excluded.disabled_until,
                    last_error = excluded.last_error, last_success = excluded.last_success;
                """;
            cmd.Parameters.AddWithValue("$id", sourceId);
            cmd.Parameters.AddWithValue("$f", state.Failures);
            cmd.Parameters.AddWithValue("$u", (object?)state.DisabledUntil?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$e", state.Error);
            cmd.Parameters.AddWithValue("$s", state.LastSuccess.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
}
