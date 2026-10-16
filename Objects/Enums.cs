namespace CC98.Objects;

/// <summary>
///     动态页面的内容类型
/// </summary>
public enum FocusContentType
{
    Followee = 0,
    FavoriteUpdate = 1,
    FavoriteBoard = 2
}

/// <summary>
///     赞/踩
/// </summary>
public enum ReactionType
{
    Like = 0,
    Dislike = 1
}

public enum PostOrder
{
    Time = 0, // 按发帖时间排序
    LastReply = 1, // 按最后回复时间排序
    Mark = 2 // 按收藏顺序排序
}

public enum EditorMode
{
    ReplyToPost = 0, //回复跟帖
    ReplyToTopic = 1, //回复主题作者
    DraftNewTopic = 2, //发表新主题
    EditMyPost = 3, //编辑已发出的帖子
    EditMyTopic = 4,
    EditSignatureCode = 5, //编辑签名档
    Chat = 6, //回复私信
    Vote = 7 //发起投票
}

/// <summary>
///     通知类型，与网页端值一致。
/// </summary>
public enum NoticeType
{
    System = 1,
    Reply = 2,
    At = 3
}

/// <summary>
///     UI事件的分类
/// </summary>
public enum FlowStatus
{
    Warning = 0,
    Success = 1,
    Fail = 2,
    Info = 3
}

public enum SearchType
{
    Topic,
    User,
    Board,
    Guide //使用指南
}

public enum ContentType
{
    Ubb = 0,
    Markdown = 1
}


/// <summary>
/// 搜索结果排序方式。
/// </summary>
public enum SearchSortMode
{
    /// <summary>默认排序(API 发帖时间)。</summary>
    Default = 0,
    /// <summary>最多回复。</summary>
    MostReplies = 1,
    /// <summary>最多点击。</summary>
    MostHits = 2
}
public enum PostType
{
    Normal=0,
    AcademicNotice=1
}
/// <summary>
/// 表示版面中安装最新，置顶或精华来聚合帖子
/// </summary>
public enum BoardTopicFilterType
{
    Latest=0,
    Top=1,
    Best=2,
}

public enum ActiveMode
{
    OpenId=0,//通过OpenId登录
    Password = 1//通过用户名密码登录
}

public enum NetworkStatus
{
    InCampus = 0, //在校园网内
    NotInCampus = 1,//不在校园网内
    NoConnection = 2, //无网络
    ConnectionFail=3,//连接失败
    VpnCookieExpired=4,//Cookie无效
    MirrorError = 5, //IP被镜像站拦截访问
}