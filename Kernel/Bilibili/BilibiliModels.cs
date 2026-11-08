using System.Collections.Generic;
using System.Linq;

namespace CC98.Kernel.Bilibili;

/// <summary>
///     视频分 P。
/// </summary>
/// <param name="Index">分 P 序号(从 1 开始,对应 <c>?p=</c>)。</param>
/// <param name="Cid">播放用的 cid,取直链时必须带上。</param>
/// <param name="Title">分 P 标题。</param>
/// <param name="Duration">分 P 时长(秒)。</param>
public sealed record BilibiliVideoPart(int Index, long Cid, string Title, long Duration);

/// <summary>
///     视频基础信息(字段全部取自未登录即可访问的 <c>web-interface/view</c> 接口)。
/// </summary>
/// <param name="Bvid">BV 号。</param>
/// <param name="Aid">av 号。</param>
/// <param name="Title">标题。</param>
/// <param name="Owner">UP 主昵称。</param>
/// <param name="Cover">封面地址(可能是 <c>http</c>,使用前需升级为 <c>https</c>)。</param>
/// <param name="Duration">总时长(秒)。</param>
/// <param name="Parts">分 P 列表;接口未返回时为空集合。</param>
public sealed record BilibiliVideoInfo(
    string Bvid,
    long Aid,
    string Title,
    string Owner,
    string? Cover,
    long Duration,
    IReadOnlyList<BilibiliVideoPart> Parts)
{
    /// <summary>
    ///     按分 P 序号取播放目标;序号越界时退回第一 P,拿不到任何分 P 时返回 <see langword="null" />。
    /// </summary>
    /// <remarks>
    ///     帖子里的 <c>?p=</c> 可能指向已删除的分 P,退回首页比直接报错更符合预期。
    /// </remarks>
    public BilibiliVideoPart? GetPart(int page)
    {
        if (Parts.Count == 0) return null;

        return Parts.FirstOrDefault(part => part.Index == page) ?? Parts[0];
    }
}

/// <summary>
///     匿名直链结果。
/// </summary>
/// <param name="Url">可下载的媒体地址(带时效,下发后约 2 小时失效)。</param>
/// <param name="Quality">实际下发画质(64=720P,32=480P,16=360P)。</param>
/// <param name="Format">容器格式(匿名单文件通道为 <c>mp4</c>/<c>mp4720</c>)。</param>
/// <param name="Size">文件字节数(接口未给定时为 0)。</param>
/// <param name="Duration">媒体时长(毫秒)。</param>
public sealed record BilibiliPlayUrl(string Url, int Quality, string Format, long Size, long Duration)
{
    /// <summary>画质的中文描述,用于卡片上的角标。</summary>
    public string QualityText => Quality switch
    {
        >= 80 => "1080P",
        >= 64 => "720P",
        >= 32 => "480P",
        >= 16 => "360P",
        _ => Format
    };
}
