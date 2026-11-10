using FutureNews.Core.Models;
using FutureNews.Core.Topics;

namespace FutureNews.App.ViewModels;

/// <summary>Editable per-topic settings shown in the settings dialog.</summary>
public sealed class SubscriptionEdit : ObservableObject
{
    private double _minScore;
    private int _maxPerDay;
    private string _extraKeywords = "";
    private string _excludeKeywords = "";

    public SubscriptionEdit(Topic topic, Subscription subscription)
    {
        Topic = topic;
        TopicId = topic.Id;
        _minScore = subscription.MinScore;
        _maxPerDay = subscription.MaxPerDay;
        _extraKeywords = subscription.ExtraKeywords;
        _excludeKeywords = subscription.ExcludeKeywords;
    }

    public Topic Topic { get; }

    public string TopicId { get; }

    public string Name => Topic.Name;

    public string ColorHex => Topic.Color;

    public double MinScore
    {
        get => _minScore;
        set => SetProperty(ref _minScore, value);
    }

    public int MaxPerDay
    {
        get => _maxPerDay;
        set => SetProperty(ref _maxPerDay, value);
    }

    public string ExtraKeywords
    {
        get => _extraKeywords;
        set => SetProperty(ref _extraKeywords, value);
    }

    public string ExcludeKeywords
    {
        get => _excludeKeywords;
        set => SetProperty(ref _excludeKeywords, value);
    }
}
