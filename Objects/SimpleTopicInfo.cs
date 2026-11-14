using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace CC98.Objects;

/// <summary>
///     用于单个版面页和个人主页的简单展示帖
/// </summary>
public partial class SimpleTopicInfo : ObservableObject
{
    public string UserName
    {
        get => field ?? "匿名";
        set => SetProperty(ref field, value);
    }
    public int? UserId
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string Title
    {
        get => field?.Trim() ?? string.Empty;
        set => SetProperty(ref field, value);
    }

    [ObservableProperty] public partial int Id { get; set; }

    [ObservableProperty] public partial int BoardId { get; set; }

    public HighlightInfo? HighlightInfo { get; set; }
    public string BoardName
    {
        get => field ?? string.Empty;
        set => SetProperty(ref field, value);
    }
    [ObservableProperty]
    public partial bool IsAnonymous { get; set; }

    [ObservableProperty] public partial int HitCount { get; set; }

    [ObservableProperty] public partial int ReplyCount { get; set; }

    [ObservableProperty] public partial DateTime Time { get; set; }

    [ObservableProperty] public partial DateTime LastBrowsingTime { get; set; }


}

public partial class SearchTopicInfo : ObservableObject
{
    public string UserName
    {
        get => field ?? "匿名";
        set => SetProperty(ref field, value);
    }
    public int? UserId
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string Title
    {
        get => field?.Trim() ?? string.Empty;
        set => SetProperty(ref field, value);
    }
    
    [ObservableProperty] public partial int Id { get; set; }

    [ObservableProperty] public partial int BoardId { get; set; }

    [JsonIgnore][ObservableProperty] public partial int SortId { get; set; }

    public string BoardName
    {
        get => field ?? string.Empty;
        set => SetProperty(ref field, value);
    }
    [ObservableProperty]
    public partial bool IsAnonymous { get; set; }
    

    [ObservableProperty] public partial int HitCount { get; set; }

    [ObservableProperty] public partial int ReplyCount { get; set; }

    [ObservableProperty] public partial DateTime Time { get; set; }

    [JsonIgnore]
    public string Keyword
    {
        get=> field ?? string.Empty;
        set=> SetProperty(ref field, value);
    }
    public string HitAndReplyCount => $"{HitCount}点击/{ReplyCount}回复";
}

public class HighlightInfo
{
    public string Color { get; set; } = string.Empty;
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }
}
