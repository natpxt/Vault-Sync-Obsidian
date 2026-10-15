using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VaultSync.App;

internal static class Ui
{
    public static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(225, 232, 242));
    public static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(158, 174, 196));
    public static readonly Brush Surface = new SolidColorBrush(Color.FromRgb(25, 35, 51));
    public static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(82, 207, 179));
    public static void Style(Window window)
    {
        window.Background = new SolidColorBrush(Color.FromRgb(13, 20, 32)); window.Foreground = Ink;
        window.FontFamily = new FontFamily("Segoe UI"); window.FontSize = 14;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var buttonStyle = new Style(typeof(Button));
        buttonStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 8, 14, 8)));
        buttonStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 4, 8, 4)));
        buttonStyle.Setters.Add(new Setter(Control.BackgroundProperty, Accent));
        buttonStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Black));
        window.Resources.Add(typeof(Button), buttonStyle);
        var input = new Style(typeof(TextBox));
        input.Setters.Add(new Setter(Control.BackgroundProperty, Surface)); input.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
        input.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9))); input.Setters.Add(new Setter(Control.BorderBrushProperty, Muted));
        window.Resources.Add(typeof(TextBox), input);
        var tabs = new Style(typeof(TabItem)); tabs.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Black));
        tabs.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 7, 14, 7))); window.Resources.Add(typeof(TabItem), tabs);
    }
    public static TextBlock Text(string text, double size = 14, Brush? color = null) => new() { Text = text, FontSize = size, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
    public static Button Button(string text, RoutedEventHandler action) { var b = new Button { Content = text }; b.Click += action; return b; }
    public static StackPanel Row(params UIElement[] items) { var row = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var i in items) row.Children.Add(i); return row; }
}
