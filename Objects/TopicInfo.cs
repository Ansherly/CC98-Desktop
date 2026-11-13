using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CC98.Objects;

/// <summary>
///     主题帖数据
/// </summary>
public partial class TopicInfo : ObservableObject
{
    [ObservableProperty] public partial int Id { get; set; }

    [ObservableProperty] public partial string? LockReason { get; set; }

    public string UserName
    {
        get => field ?? "匿名";
        set => SetProperty(ref field, value);
    }

    [ObservableProperty] public partial int? UserId { get; set; }

    public string Title
    {
        get => field?.Trim() ?? string.Empty;
        set => SetProperty(ref field, value);
    }

    [ObservableProperty] public partial int FavoriteCount { get; set; }

    [ObservableProperty] public partial int ReplyCount { get; set; }

    [ObservableProperty] public partial int HitCount { get; set; }

    [ObservableProperty] public partial bool IsAnonymous { get; set; }

    [ObservableProperty] public partial DateTime Time { get; set; }

    [ObservableProperty] public partial bool IsMe { get; set; }

    [ObservableProperty] public partial bool IsVote { get; set; }

    [ObservableProperty] [JsonIgnore] public partial bool IsFavorite { get; set; }

    public MediaContent MediaContent 
    { 
        get => field ?? new MediaContent();
        set => SetProperty(ref field, value);
    }

    [JsonIgnore]
    public string PortraitUrl
    {
        get => field ?? string.Empty;
        set => SetProperty(ref field, value);
    }

    /// <summary>搜索页填充:当前搜索关键词,用于标题高亮绑定。</summary>
    [JsonIgnore]
    public string SearchKeyword
    {
        get => field ?? string.Empty;
        set => SetProperty(ref field, value);
    }

}