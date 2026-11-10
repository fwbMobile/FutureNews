using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FutureNews.Core.Models;

namespace FutureNews.Core.Storage;

/// <summary>
/// Persists application settings in SQLite. API keys are encrypted with DPAPI
/// (CurrentUser scope) so they never sit in plain text on disk.
/// </summary>
public sealed class SettingsStore
{
    private const string SettingsKey = "app";
    private const string ApiKeyKey = "app.apikey";
    private const string OpenAlexKey = "app.openalex.key";

    private readonly Database _db;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public SettingsStore(Database db) => _db = db;

    public AppSettings Load()
    {
        var settings = new AppSettings();

        var raw = GetValue(SettingsKey);
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(raw, _json);
                if (loaded is not null) settings = loaded;
            }
            catch (JsonException)
            {
                // Corrupt settings: fall back to defaults rather than crashing.
            }
        }

        settings.Llm.ApiKey = Unprotect(GetValue(ApiKeyKey));
        var openAlex = Unprotect(GetValue(OpenAlexKey));
        if (!string.IsNullOrEmpty(openAlex)) settings.OpenAlexApiKey = openAlex;

        return settings;
    }

    public void Save(AppSettings settings)
    {
        var clone = Clone(settings);
        clone.Llm.ApiKey = string.Empty;
        clone.OpenAlexApiKey = string.Empty;

        SetValue(SettingsKey, JsonSerializer.Serialize(clone, _json));
        SetValue(ApiKeyKey, Protect(settings.Llm.ApiKey));
        SetValue(OpenAlexKey, Protect(settings.OpenAlexApiKey));
    }

    private static AppSettings Clone(AppSettings settings) => new()
    {
        UpdateTime = settings.UpdateTime,
        AutoUpdateEnabled = settings.AutoUpdateEnabled,
        LookbackDays = settings.LookbackDays,
        MaxPerTopicPerDay = settings.MaxPerTopicPerDay,
        MaxPerDay = settings.MaxPerDay,
        RetentionDays = settings.RetentionDays,
        MinScore = settings.MinScore,
        EnableOpenAlex = settings.EnableOpenAlex,
        OpenAlexApiKey = settings.OpenAlexApiKey,
        ContactEmail = settings.ContactEmail,
        RunOnStartup = settings.RunOnStartup,
        RegisterScheduledTask = settings.RegisterScheduledTask,
        Llm = new LlmSettings
        {
            Enabled = settings.Llm.Enabled,
            Provider = settings.Llm.Provider,
            BaseUrl = settings.Llm.BaseUrl,
            Model = settings.Llm.Model,
            ApiKey = settings.Llm.ApiKey,
            BatchSize = settings.Llm.BatchSize,
            TimeoutSeconds = settings.Llm.TimeoutSeconds,
            Chinese = settings.Llm.Chinese,
        },
        Notify = new NotifySettings
        {
            Enabled = settings.Notify.Enabled,
            Mode = settings.Notify.Mode,
            OnlyWhenMinimized = settings.Notify.OnlyWhenMinimized,
        },
    };

    private string? GetValue(string key)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    private void SetValue(string key, string? value)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO settings (key, value) VALUES ($k, $v)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", (object?)value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static string Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return string.Empty;
        try
        {
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string Unprotect(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return string.Empty;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(cipher), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
