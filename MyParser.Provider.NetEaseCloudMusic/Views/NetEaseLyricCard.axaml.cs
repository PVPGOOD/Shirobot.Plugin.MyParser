using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MyParser.Provider.NetEaseCloudMusic.Views;

public partial class NetEaseLyricCard : UserControl
{
    public NetEaseLyricCard()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
