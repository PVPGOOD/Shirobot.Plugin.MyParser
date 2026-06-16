using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Shirobot.Plugin.MyParser.Providers.Bilibili.Views;

public partial class BiliArticleDocument : UserControl
{
    public BiliArticleDocument()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
