using System;
using System.Text.Json.Serialization;

namespace CC98.Objects;

public class Notice
{
    [JsonPropertyName("title")] public string? Title { get; set; }

    [JsonPropertyName("topicId")] public int? TopicId { get; set; }

    [JsonPropertyName("postId")] public int? PostId { get; set; }

    [JsonPropertyName("time")] public DateTime Time { get; set; }

    [JsonPropertyName("postBasicInfo")] public PostBasicInfo? PostBasicInfo { get; set; }

    [JsonPropertyName("type")] public int Type { get; set; }

    [JsonPropertyName("content")] public string? Content { get; set; }

    [JsonPropertyName("isRead")] public bool IsRead { get; set; }
}

public class PostBasicInfo
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("floor")] public int Floor { get; set; }

    [JsonPropertyName("userId")] public long UserId { get; set; }

    [JsonPropertyName("userName")] public string UserName { get; set; } = string.Empty;

    [JsonPropertyName("isDeleted")] public bool IsDeleted { get; set; }
}