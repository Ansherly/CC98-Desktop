using CC98.Objects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CC98.Kernel.Network;
/// <summary>
/// 与浙江大学镜像站通信，判断当前是否需要使用VPN服务的类
/// </summary>
public class MirrorService(IHttpClientFactory httpClientFactory)
{
    private const string MirrorUrl = "https://mirrors.zju.edu.cn/api/is_campus_network";
    /// <summary>
    /// 检查是否内网环境。
    /// </summary>
    public async Task<NetworkStatus> CheckNetworkAsync(bool useVpn,CancellationToken cancellationToken = default)
    {
        try
        {
            var httpClient =useVpn ? httpClientFactory.CreateClient("MirrorClient") : httpClientFactory.CreateClient("DefaultClient");
            using var response = await httpClient.GetAsync(MirrorUrl, cancellationToken);
            var resText = await response.Content.ReadAsStringAsync(cancellationToken);
            Debug.WriteLine(response.StatusCode);
            if (response.IsSuccessStatusCode)
            {
                switch (resText)
                {
                    case "0":
                        return NetworkStatus.NotInCampus;
                    case "1" or "2":
                        return NetworkStatus.InCampus;
                    default:
                        Debug.WriteLine("访问镜像站失败",$"{response.StatusCode}:{response.ReasonPhrase ?? ""},响应正文{resText}");
                        return NetworkStatus.MirrorError;
                }  
            }
            else if(useVpn && response.StatusCode == System.Net.HttpStatusCode.Found)
            {
                return NetworkStatus.VpnCookieExpired;
            }
            else
            {
                return NetworkStatus.ConnectionFail;
            }
            
        }
        catch (Exception ex)
        {
            Debug.WriteLine("错误", $"{ex.Message}");
            return NetworkStatus.NoConnection;
        }
    }
    public static string FriendlyStatus(NetworkStatus status)
    {
        return status switch
        {
            NetworkStatus.InCampus => "处于内网环境",
            NetworkStatus.NotInCampus => "不在内网环境",
            NetworkStatus.ConnectionFail => "无法连接镜像站",
            NetworkStatus.MirrorError => "IP被镜像站拦截",
            NetworkStatus.NoConnection => "无网络",
            NetworkStatus.VpnCookieExpired => "VPN Cookie过期",
            _ => "未知状态"
        };
    }
}
