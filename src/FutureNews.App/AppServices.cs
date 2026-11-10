using FutureNews.Core;
using FutureNews.Core.Fetching;
using FutureNews.Core.Pipeline;
using FutureNews.Core.Storage;

namespace FutureNews.App;

/// <summary>Shared long-lived services for the running app.</summary>
public sealed class AppServices : IDisposable
{
    public AppServices()
    {
        AppPaths.EnsureCreated();
        Database = new Database(AppPaths.DatabasePath);
        Repository = new ArticleRepository(Database);
        Settings = new SettingsStore(Database);
        Http = new HttpFetcher();
        Pipeline = new DigestPipeline(Repository, Settings, Http);
    }

    public Database Database { get; }

    public ArticleRepository Repository { get; }

    public SettingsStore Settings { get; }

    public HttpFetcher Http { get; }

    public DigestPipeline Pipeline { get; }

    public void Dispose()
    {
        Http.Dispose();
        Database.Dispose();
    }
}
