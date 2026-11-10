using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FutureNews.App.Logging;
using FutureNews.App.Tray;
using FutureNews.App.ViewModels;
using FutureNews.Core;
using FutureNews.Core.Models;
using FutureNews.Core.Pipeline;
using FutureNews.Core.Topics;

namespace FutureNews.App;

public partial class MainWindow : Window
{
    private readonly AppServices _services;
    private readonly TrayIconManager _tray;
    private readonly ObservableCollection<TopicItem> _topics = new();
    private readonly ObservableCollection<ArticleItem> _articles = new();
    private readonly List<ArticleItem> _allArticles = new();
    private CancellationTokenSource? _updateCts;
    private bool _loading = true;

    public MainWindow(AppServices services, TrayIconManager tray)
    {
        _services = services;
        _tray = tray;

        InitializeComponent();
        TopicList.ItemsSource = _topics;
        ArticleList.ItemsSource = _articles;

        LoadTopics();
        LoadArticles();
        UpdateNextRun();
        _loading = false;
        ApplyFilter();
    }

    // ─────────────────────────────────────────────── loading

    private void LoadTopics()
    {
        _topics.Clear();
        var subscriptions = _services.Repository.GetSubscriptions();
        var known = new Dictionary<string, TopicItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var topic in TopicCatalog.All)
        {
            var item = new TopicItem(topic);
            known[topic.Id] = item;
            _topics.Add(item);
        }

        foreach (var subscription in subscriptions)
        {
            if (known.TryGetValue(subscription.TopicId, out var item)) item.IsEnabled = subscription.Enabled;
        }
    }

    private void LoadArticles()
    {
        _allArticles.Clear();
        foreach (var article in _services.Repository.GetArticles(limit: 600))
        {
            _allArticles.Add(new ArticleItem(article));
        }
    }

    private void ApplyFilter()
    {
        if (_loading) return;

        var topicIds = _topics.Where(t => t.IsEnabled).Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mode = (FilterAll.IsChecked == true ? "all"
            : FilterUnread.IsChecked == true ? "unread"
            : FilterStarred.IsChecked == true ? "starred"
            : "today");

        var query = SearchBox.Text.Trim();

        var visible = _allArticles.Where(a =>
        {
            if (topicIds.Count > 0 && !a.Article.Topics.Any(t => topicIds.Contains(t))) return false;
            if (mode == "unread" && a.IsRead) return false;
            if (mode == "starred" && !a.IsStarred) return false;
            if (mode == "today" && a.Article.DigestDate != DateTime.Now.ToString("yyyy-MM-dd")) return false;
            if (query.Length > 0 &&
                !a.Title.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !(a.OneLiner?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) &&
                !(a.Venue?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)) return false;
            return true;
        })
        .OrderByDescending(a => a.IsStarred)
        .ThenByDescending(a => a.Article.Score)
        .ThenByDescending(a => a.Article.PublishedAt)
        .ToList();

        _articles.Clear();
        foreach (var item in visible) _articles.Add(item);

        CountText.Text = $"{_articles.Count} 条";
        UpdateTopicCounts();

        var hasItems = _articles.Count > 0;
        EmptyState.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        if (!hasItems)
        {
            EmptyTitle.Text = topicIds.Count == 0 ? "还没有订阅任何领域" : "暂无内容";
            EmptyHint.Text = topicIds.Count == 0
                ? "在左侧勾选你关心的科技领域，然后点击「立即更新」。"
                : "试试点击「立即更新」抓取最新进展，或调整左侧的订阅设置。";
        }
    }

    private void UpdateTopicCounts()
    {
        foreach (var topic in _topics)
        {
            topic.ArticleCount = _allArticles.Count(a => a.Article.Topics.Contains(topic.Id));
        }
    }

    // ─────────────────────────────────────────────── update

    public async void StartUpdate()
    {
        if (_updateCts is not null) return;

        _updateCts = new CancellationTokenSource();
        BusyOverlay.Visibility = Visibility.Visible;
        RefreshIcon.Visibility = Visibility.Collapsed;
        RefreshText.Text = "更新中…";
        StatusText.Text = "正在从各来源抓取最新进展…";
        _tray.SetStatus("正在更新…");

        try
        {
            // Persist the current subscription state before fetching.
            SaveTopicSubscriptions();

            var result = await _services.Pipeline.RunAsync(_updateCts.Token);

            LoadArticles();
            ApplyFilter();

            var summary = result.Summary;
            var llmNote = summary is null
                ? string.Empty
                : summary.LlmCount > 0
                    ? $"（LLM 摘要 {summary.LlmCount} 条）"
                    : summary.LlmAvailable == false ? "（本地摘要）" : $"（本地摘要 {summary.FallbackCount} 条）";

            StatusText.Text = $"本次抓取 {result.Fetched} 条 → 保留 {result.Persisted} 条 · 去重 {result.Duplicate} 条 · 用时 {result.Duration.TotalSeconds:F1}s{llmNote}";
            _tray.SetStatus($"上次更新 {DateTime.Now:HH:mm}");

            foreach (var error in result.Errors) Log.Write("更新提示: " + error);

            if (result.Persisted > 0)
            {
                var settings = _services.Settings.Load();
                if (settings.Notify.Enabled && !settings.Notify.OnlyWhenMinimized)
                {
                    _tray.Notify($"FutureNews · {result.Persisted} 条新进展",
                        TrayIconManager.BuildDigestMessage(result));
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "更新已取消";
        }
        catch (Exception ex)
        {
            Log.Error("更新失败", ex);
            StatusText.Text = "更新失败: " + ex.Message;
            MessageBox.Show($"更新失败：{ex.Message}", "FutureNews", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BusyOverlay.Visibility = Visibility.Collapsed;
            RefreshIcon.Visibility = Visibility.Visible;
            RefreshText.Text = "立即更新";
            _updateCts.Dispose();
            _updateCts = null;
            UpdateNextRun();
        }
    }

    private void SaveTopicSubscriptions()
    {
        var settings = _services.Settings.Load();
        foreach (var topic in _topics)
        {
            var existing = _services.Repository.GetSubscriptions()
                .FirstOrDefault(s => s.TopicId == topic.Id);

            var subscription = existing ?? new Subscription
            {
                TopicId = topic.Id,
                MinScore = settings.MinScore,
                MaxPerDay = settings.MaxPerTopicPerDay,
            };

            subscription.Enabled = topic.IsEnabled;
            _services.Repository.SaveSubscription(subscription);
        }
    }

    private void UpdateNextRun()
    {
        var settings = _services.Settings.Load();
        var time = App.ParseTime(settings.UpdateTime);
        var now = DateTime.Now;
        var next = new DateTime(now.Year, now.Month, now.Day, time.Hours, time.Minutes, 0);
        if (next <= now) next = next.AddDays(1);
        NextRunText.Text = $"{next:MM 月 dd 日 HH:mm}";

        var sources = new List<string> { "arXiv", "Crossref", "Europe PMC", "RSS", "Hacker News" };
        if (settings.EnableOpenAlex) sources.Insert(2, "OpenAlex");
        SourceStatusText.Text = "数据源：" + string.Join(" · ", sources);
    }

    // ─────────────────────────────────────────────── handlers

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => StartUpdate();

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();

    public void OpenSettings()
    {
        var window = new Views.SettingsWindow(_services) { Owner = this };
        window.ShowDialog();
        LoadTopics();
        LoadArticles();
        ApplyFilter();
        UpdateNextRun();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radio) return;
        radio.IsChecked = true;
        ApplyFilter();
    }

    private void MarkAllRead_Click(object sender, RoutedEventArgs e)
    {
        _services.Repository.MarkAllRead();
        LoadArticles();
        ApplyFilter();
        StatusText.Text = "已全部标记为已读";
    }

    private void StarButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ArticleItem item }) return;
        _services.Repository.ToggleStar(item.Id);
        item.IsStarred = !item.IsStarred;
        ApplyFilter();
    }

    private void ReadButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ArticleItem item }) return;
        item.IsRead = !item.IsRead;
        _services.Repository.MarkRead(item.Id, item.IsRead);
        ApplyFilter();
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ArticleItem item }) return;
        OpenUrl(item.Url);
        if (!item.IsRead)
        {
            item.IsRead = true;
            _services.Repository.MarkRead(item.Id, true);
        }
    }

    private void CopyLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ArticleItem item }) return;
        try
        {
            Clipboard.SetText(item.Url);
            StatusText.Text = "链接已复制到剪贴板";
        }
        catch (Exception ex)
        {
            StatusText.Text = "复制失败: " + ex.Message;
        }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.DataDirectory) { UseShellExecute = true });

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        var path = AppPaths.LogPath;
        if (!File.Exists(path))
        {
            MessageBox.Show("还没有生成日志文件。", "FutureNews", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Process.Start(new ProcessStartInfo("notepad.exe", path) { UseShellExecute = true });
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // ignore
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Closing the window keeps the app alive in the tray.
        if (!IsExiting)
        {
            e.Cancel = true;
            Hide();
            _tray.Notify("FutureNews 仍在后台运行", "托盘图标可随时唤回窗口，或右键退出。", silent: true);
            return;
        }
        base.OnClosing(e);
    }

    private bool IsExiting { get; set; }
}
