using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FutureNews.App.Logging;
using FutureNews.App.Scheduling;
using FutureNews.App.ViewModels;
using FutureNews.Core;
using FutureNews.Core.Models;
using FutureNews.Core.Summarizing;
using FutureNews.Core.Topics;

namespace FutureNews.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppServices _services;
    private readonly List<SubscriptionEdit> _topicEdits = new();
    private AppSettings _settings;

    public SettingsWindow(AppServices services)
    {
        _services = services;

        InitializeComponent();

        _settings = _services.Settings.Load();
        LoadProviders();
        LoadValues();
        LoadTopicEdits();
        RefreshTaskStatus();
    }

    private void LoadProviders()
    {
        foreach (var provider in LlmProviders.All)
        {
            ProviderCombo.Items.Add(provider);
        }

        ProviderCombo.SelectedItem = LlmProviders.Get(_settings.Llm.Provider);
    }

    private void LoadValues()
    {
        UpdateTimeBox.Text = _settings.UpdateTime;
        AutoUpdateCheck.IsChecked = _settings.AutoUpdateEnabled;
        RunOnStartupCheck.IsChecked = _settings.RunOnStartup;
        NotifyCheck.IsChecked = _settings.Notify.Enabled;
        NotifyOnlyMinimizedCheck.IsChecked = _settings.Notify.OnlyWhenMinimized;

        LookbackBox.Text = _settings.LookbackDays.ToString();
        MaxPerTopicBox.Text = _settings.MaxPerTopicPerDay.ToString();
        MaxPerDayBox.Text = _settings.MaxPerDay.ToString();
        MinScoreBox.Text = _settings.MinScore.ToString("0.00");
        RetentionBox.Text = _settings.RetentionDays.ToString();

        OpenAlexCheck.IsChecked = _settings.EnableOpenAlex;
        OpenAlexKeyBox.Password = _settings.OpenAlexApiKey;
        ContactEmailBox.Text = _settings.ContactEmail;

        LlmCheck.IsChecked = _settings.Llm.Enabled;
        BaseUrlBox.Text = string.IsNullOrWhiteSpace(_settings.Llm.BaseUrl)
            ? LlmProviders.Get(_settings.Llm.Provider).BaseUrl
            : _settings.Llm.BaseUrl;
        ModelBox.Text = string.IsNullOrWhiteSpace(_settings.Llm.Model)
            ? LlmProviders.Get(_settings.Llm.Provider).Model
            : _settings.Llm.Model;
        ApiKeyBox.Password = _settings.Llm.ApiKey;
        BatchSizeBox.Text = _settings.Llm.BatchSize.ToString();
        ChineseCheck.IsChecked = _settings.Llm.Chinese;

        DataPathText.Text = "数据目录：" + AppPaths.DataDirectory;

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = "版本：" + (version is null ? "1.0.0" : version.ToString(3));
    }

    private void LoadTopicEdits()
    {
        _topicEdits.Clear();
        var stored = _services.Repository.GetSubscriptions().ToDictionary(s => s.TopicId, StringComparer.OrdinalIgnoreCase);

        foreach (var topic in TopicCatalog.All)
        {
            var subscription = stored.TryGetValue(topic.Id, out var existing)
                ? existing
                : new Subscription
                {
                    TopicId = topic.Id,
                    Enabled = false,
                    MinScore = _settings.MinScore,
                    MaxPerDay = _settings.MaxPerTopicPerDay,
                };
            _topicEdits.Add(new SubscriptionEdit(topic, subscription));
        }

        TopicSettingsList.ItemsSource = _topicEdits;
    }

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderCombo.SelectedItem is not LlmProvider provider) return;
        BaseUrlBox.Text = provider.BaseUrl;
        ModelBox.Text = provider.Model;
        LlmTestResult.Text = provider.Hint ?? "";
        LlmTestResult.Foreground = (Brush)FindResource("TextDimBrush");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.UpdateTime = NormalizeTime(UpdateTimeBox.Text);
        _settings.AutoUpdateEnabled = AutoUpdateCheck.IsChecked == true;
        _settings.RunOnStartup = RunOnStartupCheck.IsChecked == true;
        _settings.Notify.Enabled = NotifyCheck.IsChecked == true;
        _settings.Notify.OnlyWhenMinimized = NotifyOnlyMinimizedCheck.IsChecked == true;

        _settings.LookbackDays = Clamp(ParseInt(LookbackBox.Text, 3), 1, 30);
        _settings.MaxPerTopicPerDay = Clamp(ParseInt(MaxPerTopicBox.Text, 8), 1, 50);
        _settings.MaxPerDay = Clamp(ParseInt(MaxPerDayBox.Text, 40), 1, 200);
        _settings.MinScore = Math.Clamp(ParseDouble(MinScoreBox.Text, 0.30), 0.01, 0.99);
        _settings.RetentionDays = Clamp(ParseInt(RetentionBox.Text, 30), 3, 365);

        _settings.EnableOpenAlex = OpenAlexCheck.IsChecked == true;
        _settings.OpenAlexApiKey = OpenAlexKeyBox.Password.Trim();
        _settings.ContactEmail = ContactEmailBox.Text.Trim();

        _settings.Llm.Enabled = LlmCheck.IsChecked == true;
        _settings.Llm.Provider = (ProviderCombo.SelectedItem as LlmProvider)?.Id ?? "deepseek";
        _settings.Llm.BaseUrl = BaseUrlBox.Text.Trim();
        _settings.Llm.Model = ModelBox.Text.Trim();
        _settings.Llm.ApiKey = ApiKeyBox.Password.Trim();
        _settings.Llm.BatchSize = Clamp(ParseInt(BatchSizeBox.Text, 8), 1, 20);
        _settings.Llm.Chinese = ChineseCheck.IsChecked == true;

        _services.Settings.Save(_settings);

        foreach (var edit in _topicEdits)
        {
            var stored = _services.Repository.GetSubscriptions().FirstOrDefault(s => s.TopicId == edit.TopicId);
            var subscription = stored ?? new Subscription { TopicId = edit.TopicId };
            subscription.MinScore = Math.Clamp(edit.MinScore, 0.01, 0.99);
            subscription.MaxPerDay = Clamp(edit.MaxPerDay, 1, 50);
            subscription.ExtraKeywords = edit.ExtraKeywords;
            subscription.ExcludeKeywords = edit.ExcludeKeywords;
            _services.Repository.SaveSubscription(subscription);
        }

        ApplyStartupRegistration();
        Log.Write("设置已保存");
        DialogResult = true;
        Close();
    }

    private void ApplyStartupRegistration()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            var launcher = Path.Combine(startupFolder, "FutureNews.cmd");

            if (_settings.RunOnStartup)
            {
                // A tiny launcher script avoids fragile COM/shortcut quoting.
                File.WriteAllText(launcher,
                    "@echo off\r\n" +
                    $"start \"\" \"{exePath}\" --tray\r\n");
            }
            else if (File.Exists(launcher))
            {
                File.Delete(launcher);
            }
        }
        catch (Exception ex)
        {
            Log.Error("设置开机自启失败", ex);
        }
    }

    private async void TestLlm_Click(object sender, RoutedEventArgs e)
    {
        LlmTestResult.Text = "正在测试…";
        LlmTestResult.Foreground = (Brush)FindResource("TextDimBrush");

        var provider = ProviderCombo.SelectedItem as LlmProvider ?? LlmProviders.All[0];
        var baseUrl = string.IsNullOrWhiteSpace(BaseUrlBox.Text) ? provider.BaseUrl : BaseUrlBox.Text.Trim();
        var model = string.IsNullOrWhiteSpace(ModelBox.Text) ? provider.Model : ModelBox.Text.Trim();
        var key = ApiKeyBox.Password.Trim();

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
        {
            LlmTestResult.Text = "请先填写 Base URL 与模型名";
            LlmTestResult.Foreground = (Brush)FindResource("DangerBrush");
            return;
        }

        try
        {
            var client = new OpenAiCompatClient(baseUrl, model, key, 45);
            var reply = await client.CompleteAsync(
                "你是科技简报编辑。",
                "只回复两个字：可用",
                CancellationToken.None);

            LlmTestResult.Text = string.IsNullOrWhiteSpace(reply) ? "连接成功但返回为空" : "连接成功：" + reply.Trim();
            LlmTestResult.Foreground = (Brush)FindResource("SuccessBrush");
        }
        catch (Exception ex)
        {
            LlmTestResult.Text = "失败：" + ex.Message;
            LlmTestResult.Foreground = (Brush)FindResource("DangerBrush");
        }
    }

    private void RegisterTask_Click(object sender, RoutedEventArgs e)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return;

        var (success, message) = TaskSchedulerRegistrar.Register(App.ParseTime(_settings.UpdateTime), exePath);
        SaveHint.Text = success ? "计划任务已注册" : "注册失败：" + message;
        RefreshTaskStatus();
    }

    private void UnregisterTask_Click(object sender, RoutedEventArgs e)
    {
        var (success, message) = TaskSchedulerRegistrar.Unregister();
        SaveHint.Text = success ? "计划任务已移除" : "移除失败：" + message;
        RefreshTaskStatus();
    }

    private void RefreshTaskStatus()
    {
        TaskStatusText.Text = TaskSchedulerRegistrar.IsRegistered() ? "状态：已注册" : "状态：未注册";
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.DataDirectory) { UseShellExecute = true });

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(AppPaths.LogPath))
            Process.Start(new ProcessStartInfo("notepad.exe", AppPaths.LogPath) { UseShellExecute = true });
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show("将清空本地保存的全部文章与去重记录（不会影响已订阅的领域设置）。确定继续？",
            "清空本地历史", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK) return;

        _services.Repository.PurgeOlderThan(DateTimeOffset.UtcNow.AddDays(3650));
        SaveHint.Text = "已清空本地历史";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static string NormalizeTime(string raw)
    {
        if (TimeSpan.TryParse(raw, out var parsed))
        {
            return $"{parsed.Hours:00}:{parsed.Minutes:00}";
        }
        return "08:30";
    }

    private static int ParseInt(string raw, int fallback) =>
        int.TryParse(raw, out var value) ? value : fallback;

    private static double ParseDouble(string raw, double fallback) =>
        double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
}
