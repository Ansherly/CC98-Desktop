using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CC98.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class GuidePage : Page
    {
        public GuidePage()
        {
            InitializeComponent();
        }

        private const string GuideText = "**在校外登录** \r\n\r\n使用[ZJU-Connect](https://github.com/Mythologyli/ZJU-Connect-for-Windows/releases)连接到浙江大学内网。\r\n\r\n**忘记密码/无账号？**\r\n\r\n进入[CC98](https://www.cc98.org/logon)官网操作。\r\n\r\n**遇到问题/想要新功能?**\r\n\r\n你可以在Github,微软商店或[开发进度记录楼](https://www.cc98.org/topic/6173309)反馈此问题。\r\n\r\n你也可以克隆本应用仓库，自由修改和编译新的分支。不过，在分发时，应向用户告知所有的改动。\r\n\r\n**成为开发者**\r\n\r\n本应用使用`WinUI3`,`C#`,`XAML`构建。欢迎所有对.NET生态感兴趣的开发者加入本应用团队，欢迎所有使用者对本应用UI、功能和代码提供建议。";

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            TipText.MarkdownText = GuideText;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if(Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }
    }
}
