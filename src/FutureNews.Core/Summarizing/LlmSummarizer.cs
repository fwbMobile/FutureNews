using System.Text;
using System.Text.Json;
using FutureNews.Core.Fetching;
using FutureNews.Core.Models;

namespace FutureNews.Core.Summarizing;

/// <summary>Summarises articles into a single Chinese sentence using an LLM.</summary>
public sealed class LlmSummarizer
{
    private readonly OpenAiCompatClient _client;
    private readonly bool _chinese;

    public LlmSummarizer(OpenAiCompatClient client, bool chinese)
    {
        _client = client;
        _chinese = chinese;
    }

    private const string SystemPrompt = """
        你是一位严谨的科技简报编辑。给定若干条科技动态（标题与摘要），为每条写一句概括。
        规则：
        1. 只输出概括本身，不要编号、不要解释、不要加引号、不要复述标题。
        2. 必须客观陈述这条动态发生了什么（做了什么、达到了什么、发现了什么），不要评论、不要预测。
        3. 保留关键的量化信息（数值、单位、倍数、时间）。
        4. 长度不超过 45 个汉字，控制在 25-45 字之间。
        5. 严格按输入的 JSON 数组顺序输出，元素数量一致。
        """;

    private const string SystemPromptEn = """
        You are a precise technology briefing editor. For each item, write one sentence summarising it.
        Rules: state only what happened (what was done, achieved or found); no commentary or predictions;
        keep key quantitative details; 12-28 words; output only a JSON array with the same number of
        elements as the input, in the same order.
        """;

    /// <summary>Summarises a batch of articles. Returns a map of article id to one-liner.</summary>
    public async Task<Dictionary<string, string>> SummarizeAsync(IReadOnlyList<Article> articles, CancellationToken ct)
    {
        var result = new Dictionary<string, string>();
        if (articles.Count == 0) return result;

        var items = new List<object>();
        for (var i = 0; i < articles.Count; i++)
        {
            var a = articles[i];
            items.Add(new
            {
                i,
                title = a.Title,
                source = a.SourceName,
                kind = a.SourceType,
                abstractText = TextUtil.TrimToLength(a.Abstract ?? string.Empty, 700),
            });
        }

        var request = new
        {
            items,
            output = new[] { "id", "summary" },
        };

        var user = "请为下面每个条目生成一句话概括，返回 JSON 数组：" + JsonSerializer.Serialize(request);

        var raw = await _client.CompleteAsync(_chinese ? SystemPrompt : SystemPromptEn, user, ct).ConfigureAwait(false);
        return ParseResponse(raw, articles, result);
    }

    private static Dictionary<string, string> ParseResponse(string raw, IReadOnlyList<Article> articles, Dictionary<string, string> result)
    {
        var json = ExtractJsonArray(raw);
        if (json is null) return result;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var index = -1;
                var summary = string.Empty;

                foreach (var property in item.EnumerateObject())
                {
                    if (property.NameEquals("id") || property.NameEquals("i") || property.NameEquals("index"))
                    {
                        if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var n)) index = n;
                        else if (property.Value.ValueKind == JsonValueKind.String && int.TryParse(property.Value.GetString(), out var s)) index = s;
                    }
                    else if (property.NameEquals("summary") || property.NameEquals("s") || property.NameEquals("one_liner"))
                    {
                        summary = property.Value.GetString() ?? string.Empty;
                    }
                }

                if (index < 0 || index >= articles.Count) continue;
                var cleaned = CleanSummary(summary);
                if (cleaned.Length == 0) continue;
                result[articles[index].Id] = cleaned;
            }
        }
        catch (JsonException)
        {
            // fall through: the caller will use the extractive fallback
        }

        return result;
    }

    /// <summary>Extracts the first JSON array from a response that may be wrapped in prose or code fences.</summary>
    internal static string? ExtractJsonArray(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline > 0)
            {
                var closing = text.LastIndexOf("```", StringComparison.Ordinal);
                if (closing > firstNewline) text = text[(firstNewline + 1)..closing].Trim();
            }
        }

        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start < 0 || end <= start) return null;
        return text[start..(end + 1)];
    }

    internal static string CleanSummary(string summary)
    {
        var text = TextUtil.Collapse(summary);
        text = text.Trim('"', '\'', '“', '”', '「', '」', ' ', '。', '.', '，', ',');
        text = text.Replace("摘要：", "").Replace("概括：", "").Trim();
        return text;
    }
}
