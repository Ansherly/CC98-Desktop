using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CC98.Objects;

/// <summary>
///     详细个人信息
/// </summary>
public partial class UserInfo : ObservableObject
{
    public string Name
    {
        get => field ?? string.Empty;
        set => SetProperty(ref field, value);
    }

    public string SignatureCode
    {
        get => field ?? "[i]该用户还没设置签名档[i]";
        set => SetProperty(ref field, value);
    }

    [ObservableProperty] public partial int Id { get; set; }

    [ObservableProperty] public partial int PostCount { get; set; }

    [ObservableProperty] public partial int Wealth { get; set; }

    [ObservableProperty] public partial DateTime LastLogOnTime { get; set; }

    public string PortraitUrl
    {
        get => field ?? "";
        set => SetProperty(ref field, value);
    }

    [ObservableProperty] public partial int Popularity { get; set; }

    [ObservableProperty] public partial int FanCount { get; set; }

    [ObservableProperty] public partial int FollowCount { get; set; }

    [ObservableProperty] public partial DateTime RegisterTime { get; set; }

    [JsonIgnore] [ObservableProperty] public partial bool IsMe { get; set; }

    [ObservableProperty] public partial bool IsFollowing { get; set; }
    [ObservableProperty]public partial bool BrowsingHistoryEnabled { get; set; }


    public List<int> CustomBoards
    {
        get => field ?? [];
        set => SetProperty(ref field, value);
    }
}