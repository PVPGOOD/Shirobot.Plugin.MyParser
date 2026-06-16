using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Shirobot.Plugin.MyParser.Providers.Douyin.Views;

public partial class DouyinCard : UserControl
{
    public DouyinCard()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
