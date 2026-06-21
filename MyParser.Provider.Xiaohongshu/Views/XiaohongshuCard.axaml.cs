using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MyParser.Provider.Xiaohongshu.Views;

public partial class XiaohongshuCard : UserControl
{
    public XiaohongshuCard()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
