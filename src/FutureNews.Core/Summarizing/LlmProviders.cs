namespace FutureNews.Core.Summarizing;

/// <summary>Preset endpoints for OpenAI-compatible chat completion APIs.</summary>
public sealed record LlmProvider(string Id, string Name, string BaseUrl, string Model, bool RequiresKey, string? Hint);

public static class LlmProviders
{
    public static readonly IReadOnlyList<LlmProvider> All = new List<LlmProvider>
    {
        new("deepseek", "DeepSeek", "https://api.deepseek.com/v1", "deepseek-chat", true, "便宜、速度快，中文表现好"),
        new("dashscope", "阿里云百炼（通义千问）", "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus", true, "国内网络友好"),
        new("moonshot", "月之暗面 Kimi", "https://api.moonshot.cn/v1", "moonshot-v1-32k", true, "长上下文"),
        new("zhipu", "智谱 GLM", "https://open.bigmodel.cn/api/paas/v4", "glm-4-flash", true, "免费额度"),
        new("siliconflow", "SiliconFlow 硅基流动", "https://api.siliconflow.cn/v1", "Qwen/Qwen2.5-7B-Instruct", true, "聚合多家模型"),
        new("openai", "OpenAI", "https://api.openai.com/v1", "gpt-4o-mini", true, "需国际网络"),
        new("ollama", "Ollama 本地模型", "http://localhost:11434/v1", "qwen2.5:7b", false, "完全离线，不消耗额度"),
        new("custom", "自定义（OpenAI 兼容）", "", "", true, "填写任意 base_url 与 model"),
    };

    public static LlmProvider Get(string id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];
}
