namespace CC98.Services;

//用于保留当前应用启动期间的导航参数、滚动位置和全局对象

public class GlobalService
{
    /// <summary>
    /// 当前用户的 @ 消息总数。
    /// </summary>
    public int AtCount { get; set; }

    /// <summary>
    /// 当前用户的私信消息总数。
    /// </summary>
    public int MessageCount { get; set; }

    /// <summary>
    /// 当前用户的回复消息总数。
    /// </summary>
    public int ReplyCount { get; set; }

    /// <summary>
    /// 当前用户的系统消息总数。
    /// </summary>
    public int SystemCount { get; set; }

    /// <summary>
    /// 受保护的构造方法。
    /// </summary>
    private GlobalService()
    {
    }

    /// <summary>
    /// 获取当前对象的唯一实例。
    /// </summary>
    public static GlobalService Instance { get; } = new();


    public static bool IsVpnConfigured => PasswordManager.PasswordExists("VpnUserName") && PasswordManager.PasswordExists("VpnPassWord");
}