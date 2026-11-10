using System.Windows.Media;
using FutureNews.Core.Models;
using FutureNews.Core.Topics;
using Brush = System.Windows.Media.Brush;

namespace FutureNews.App.ViewModels;

/// <summary>A topic the user can subscribe to, bound to a checkbox in the sidebar.</summary>
public sealed class TopicItem : ObservableObject
{
    private bool _isEnabled;
    private int _articleCount;

    public TopicItem(Topic topic)
    {
        Topic = topic;
        Brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(topic.Color));
        Brush.Freeze();
    }

    public Topic Topic { get; }

    public string Id => Topic.Id;

    public string Name => Topic.Name;

    public string NameEn => Topic.NameEn;

    public string Description => Topic.Description;

    public Brush Brush { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public int ArticleCount
    {
        get => _articleCount;
        set
        {
            if (SetProperty(ref _articleCount, value)) OnPropertyChanged(nameof(CountLabel));
        }
    }

    public string CountLabel => _articleCount > 0 ? _articleCount.ToString() : string.Empty;

    public static TopicItem? FromSubscription(Subscription subscription, IReadOnlyDictionary<string, TopicItem> known)
    {
        if (!known.TryGetValue(subscription.TopicId, out var item)) return null;
        item.IsEnabled = subscription.Enabled;
        return item;
    }
}

/// <summary>One row in the article list.</summary>
public sealed class ArticleItem : ObservableObject
{
    private bool _isRead;
    private bool _isStarred;

    public ArticleItem(Article article)
    {
        Article = article;
        _isRead = article.IsRead;
        _isStarred = article.IsStarred;
    }

    public Article Article { get; }

    public string Id => Article.Id;

    public string Title => Article.Title;

    public string OneLiner => string.IsNullOrWhiteSpace(Article.OneLiner) ? "（暂无摘要）" : Article.OneLiner!;

    public bool HasSummary => !string.IsNullOrWhiteSpace(Article.OneLiner);

    public string SourceName => Article.SourceName;

    public string SourceType => Article.SourceType;

    public string Venue => string.IsNullOrWhiteSpace(Article.Venue) ? Article.SourceName : Article.Venue!;

    public string TimeText => FormatTime(Article.PublishedAt);

    public string DateGroup => FormatDateGroup(Article.PublishedAt);

    public string TopicText => Article.Topics.Count == 0
        ? string.Empty
        : string.Join(" · ", Article.Topics.Select(TopicCatalog.Get).Where(t => t is not null).Select(t => t!.Name));

    public Brush TopicBrush
    {
        get
        {
            var topic = Article.Topics.Select(TopicCatalog.Get).FirstOrDefault(t => t is not null);
            if (topic is null) return new SolidColorBrush(Color.FromRgb(0x8B, 0x95, 0xA9));
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(topic.Color));
            brush.Freeze();
            return brush;
        }
    }

    public string TypeLabel => Article.SourceType switch
    {
        "paper" => "论文",
        "preprint" => "预印本",
        "news" => "新闻",
        "community" => "社区",
        _ => "资讯",
    };

    public string Url => Article.Url;

    public bool IsRead
    {
        get => _isRead;
        set => SetProperty(ref _isRead, value);
    }

    public bool IsStarred
    {
        get => _isStarred;
        set => SetProperty(ref _isStarred, value);
    }

    private static string FormatTime(DateTimeOffset published)
    {
        var local = published.ToLocalTime();
        var now = DateTimeOffset.Now;
        var diff = now - local;

        if (diff.TotalMinutes < -60) return "刚刚发布";
        if (diff.TotalMinutes < 60) return $"{Math.Max(1, (int)diff.TotalMinutes)} 分钟前";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} 小时前";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} 天前";
        return local.ToString("MM-dd HH:mm");
    }

    private static string FormatDateGroup(DateTimeOffset published)
    {
        var local = published.ToLocalTime().Date;
        var today = DateTime.Today;
        if (local == today) return "今天";
        if (local == today.AddDays(-1)) return "昨天";
        if (local > today.AddDays(-7)) return "本周";
        if (local.Year == today.Year) return local.ToString("MM 月");
        return local.ToString("yyyy 年 MM 月");
    }
}
