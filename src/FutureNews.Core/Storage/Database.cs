using Microsoft.Data.Sqlite;

namespace FutureNews.Core.Storage;

/// <summary>Creates and owns the SQLite database used by the app.</summary>
public sealed class Database : IDisposable
{
    private readonly SqliteConnection _connection;

    public Database(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connection = new SqliteConnection($"Data Source={path}");
        _connection.Open();
        Execute("PRAGMA journal_mode=WAL;");
        Execute("PRAGMA foreign_keys=ON;");
        CreateSchema();
    }

    private void Execute(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public SqliteConnection Connection => _connection;

    private void CreateSchema()
    {
        Execute("""
            CREATE TABLE IF NOT EXISTS articles (
                id            TEXT PRIMARY KEY,
                url           TEXT NOT NULL UNIQUE,
                title         TEXT NOT NULL,
                abstract      TEXT,
                one_liner     TEXT,
                summary_source TEXT,
                source_id     TEXT,
                source_name   TEXT,
                source_type   TEXT,
                published_at  TEXT,
                fetched_at    TEXT,
                topics        TEXT,
                authors       TEXT,
                venue         TEXT,
                simhash       INTEGER,
                score         REAL,
                is_read       INTEGER DEFAULT 0,
                is_starred    INTEGER DEFAULT 0,
                digest_date   TEXT,
                original_id   TEXT,
                title_norm    TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_articles_fetched ON articles(fetched_at DESC);
            CREATE INDEX IF NOT EXISTS ix_articles_digest ON articles(digest_date DESC);
            CREATE INDEX IF NOT EXISTS ix_articles_score ON articles(score DESC);

            CREATE TABLE IF NOT EXISTS seen_hashes (
                hash         INTEGER NOT NULL,
                title_norm   TEXT,
                first_seen_at TEXT,
                article_id   TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_seen_hash ON seen_hashes(hash);
            CREATE INDEX IF NOT EXISTS ix_seen_title ON seen_hashes(title_norm);

            CREATE TABLE IF NOT EXISTS settings (
                key   TEXT PRIMARY KEY,
                value TEXT
            );

            CREATE TABLE IF NOT EXISTS subscriptions (
                topic_id      TEXT PRIMARY KEY,
                enabled       INTEGER DEFAULT 1,
                min_score     REAL DEFAULT 0.32,
                max_per_day   INTEGER DEFAULT 6,
                extra_keywords TEXT,
                exclude_keywords TEXT,
                source_filter TEXT
            );

            CREATE TABLE IF NOT EXISTS runs (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                started_at   TEXT,
                finished_at  TEXT,
                fetched      INTEGER,
                kept         INTEGER,
                error        TEXT
            );

            CREATE TABLE IF NOT EXISTS source_health (
                source_id        TEXT PRIMARY KEY,
                failures         INTEGER DEFAULT 0,
                disabled_until   TEXT,
                last_error       TEXT,
                last_success     TEXT
            );
            """);
    }

    public void Dispose() => _connection.Dispose();
}
