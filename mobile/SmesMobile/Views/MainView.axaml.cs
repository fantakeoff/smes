
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SmesMobile.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private void ScrollMenuRight(object? sender, RoutedEventArgs e)
    {
        var maxOffset = Math.Max(0, MenuScroller.Extent.Width - MenuScroller.Viewport.Width);
        var nextOffset = maxOffset - MenuScroller.Offset.X <= 48
            ? 0 : Math.Min(maxOffset, MenuScroller.Offset.X + 180);
        MenuScroller.Offset = new Vector(nextOffset, MenuScroller.Offset.Y);
    }
}