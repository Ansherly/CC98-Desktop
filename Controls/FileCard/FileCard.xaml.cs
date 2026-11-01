using System;
using System.IO;
using CC98.Controls.Primitives;
using CC98.Controls.UbbTextBlock.Common.Events;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Symbol = FluentIcons.Common.Symbol;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CC98.Controls;

public sealed partial class FileCard : UserControl
{
    public FileCard()
    {
        InitializeComponent();
    }

    #region 依赖属性

    public static readonly DependencyProperty SrcProperty =
        DependencyProperty.Register(
            "Src",
            typeof(string),
            typeof(FileCard),
            new("", OnSrcChanged));

    public string Src
    {
        get => (string)GetValue(SrcProperty);
        set => SetValue(SrcProperty, value);
    }

    #endregion

    #region 加载事件

    private static void OnSrcChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FileCard fileCard) return;
        fileCard.LoadFile();
    }

    public event EventHandler<MediaClickEventArgs> DownloadRequested;

    private void Root_Click(object sender, RoutedEventArgs e)
    {
        DownloadRequested?.Invoke(this, new(Src, MediaType.File));
    }

    private void LoadFile()
    {
        var fileName = ExtractFileNameFromUrl(Src);
        FileNameBox.Text = fileName;
        TypeIcon.Symbol = GetFileTypeIcon(fileName);
    }

    #endregion


    #region 辅助函数

    private static Symbol GetFileTypeIcon(string fileName)
    {
        var extension = Path.GetExtension(fileName)?.ToLower();
        return extension switch
        {
            ".pdf" => Symbol.DocumentPdf,
            ".xls" or ".xlsx" => Symbol.Table,
            ".ppt" or ".pptx" => Symbol.DocumentFlowchart,
            "zip" or ".rar" => Symbol.FolderZip,
            "mp3" or ".wav" or "m4a" => Symbol.MusicNote1,
            "mp4" or ".avi" => Symbol.Video,
            "jpg" or ".jpeg" or ".png" or ".gif" or "webp" => Symbol.Image,
            _ => Symbol.Document
        };
    }


    /// <summary>
    ///     从URL中提取文件名
    /// </summary>
    private static string ExtractFileNameFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var fileName = Path.GetFileName(uri.LocalPath);
   
            // 如果文件名无效，生成默认文件名
            if (string.IsNullOrEmpty(fileName)) fileName = $"CC{DateTime.Now:MMdd_HHmm}";

            return fileName;
        }
        catch
        {
            return $"CC{DateTime.Now:MMdd_HHmm}";
        }
    }

    #endregion
}