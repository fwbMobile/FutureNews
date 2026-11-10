using FutureNews.Core.Models;

namespace FutureNews.Core.Summarizing;

/// <summary>Result of a summarisation pass.</summary>
public sealed class SummaryOutcome
{
    public int LlmCount { get; init; }

    public int FallbackCount { get; init; }

    public string? Error { get; init; }

    public bool LlmAvailable { get; init; }
}

/// <summary>
/// Produces one-line summaries, preferring the LLM and silently degrading to the offline
/// extractive summariser whenever the LLM is unconfigured or fails.
/// </summary>
public sealed class SummaryService
{
    private readonly ExtractiveSummarizer _fallback;

    public SummaryService(IReadOnlyList<string>? keywords = null) => _fallback = new ExtractiveSummarizer(keywords);

    public async Task<SummaryOutcome> SummarizeAsync(IReadOnlyList<Article> articles, AppSettings settings, CancellationToken ct)
    {
        if (articles.Count == 0) return new SummaryOutcome { LlmAvailable = false };

        var llm = settings.Llm;
        if (!llm.Enabled || !llm.IsConfigured)
        {
            ApplyFallback(articles);
            return new SummaryOutcome { FallbackCount = articles.Count, LlmAvailable = false };
        }

        var provider = LlmProviders.Get(llm.Provider);
        var baseUrl = string.IsNullOrWhiteSpace(llm.BaseUrl) ? provider.BaseUrl : llm.BaseUrl;
        var model = string.IsNullOrWhiteSpace(llm.Model) ? provider.Model : llm.Model;

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
        {
            ApplyFallback(articles);
            return new SummaryOutcome { FallbackCount = articles.Count, LlmAvailable = false, Error = "未配置模型地址或模型名" };
        }

        var client = new OpenAiCompatClient(baseUrl, model, llm.ApiKey, llm.TimeoutSeconds);
        var summarizer = new LlmSummarizer(client, llm.Chinese);
        var batchSize = Math.Clamp(llm.BatchSize, 1, 20);

        var llmCount = 0;
        string? error = null;

        for (var offset = 0; offset < articles.Count; offset += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = articles.Skip(offset).Take(batchSize).ToList();
            try
            {
                var map = await summarizer.SummarizeAsync(batch, ct).ConfigureAwait(false);
                foreach (var article in batch)
                {
                    if (map.TryGetValue(article.Id, out var summary) && !string.IsNullOrWhiteSpace(summary))
                    {
                        article.OneLiner = summary;
                        article.SummarySource = "llm";
                        llmCount++;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                break;
            }
        }

        ApplyFallback(articles.Where(a => string.IsNullOrWhiteSpace(a.OneLiner)).ToList());

        return new SummaryOutcome
        {
            LlmCount = llmCount,
            FallbackCount = articles.Count - llmCount,
            Error = error,
            LlmAvailable = error is null,
        };
    }

    private void ApplyFallback(IEnumerable<Article> articles)
    {
        foreach (var article in articles)
        {
            var summary = _fallback.Summarize(article);
            article.OneLiner = summary;
            article.SummarySource = string.IsNullOrEmpty(summary) ? "none" : "extractive";
        }
    }
}
