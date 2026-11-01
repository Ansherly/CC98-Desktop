using System;
using System.Collections.Generic;

namespace CC98.Objects;

/// <summary>
/// MyRecord字段有更新。
/// </summary>
public class VoteInfo
{
    public List<int>? MyRecord { get; set; } = [];
    public List<VoteItem> VoteItems { get; set; } = [];
    public bool CanVote { get; set; }
    public bool IsAvailable { get; set; }
    public int MaxVoteCount { get; set; }
    public DateTime ExpiredTime { get; set; }
    public int VoteUserCount { get; set; }
}

public class VoteItem
{
    public int Id { get; set; }
    public int Count { get; set; }
    public string Description { get; set; } = string.Empty;
}