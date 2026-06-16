using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Shirobot.Plugin.MyParser.Providers.Bilibili.Views;

public partial class BiliCard : UserControl
{
    public BiliCard()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
