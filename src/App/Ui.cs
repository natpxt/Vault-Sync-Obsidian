using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Automation;

namespace VaultSync.App;

internal static class Ui
{
    public static Brush Ink => Theme.Get("Ink");
    public static Brush Muted => Theme.Get("Muted");
    public static Brush Surface => Theme.Get("Surface");
    public static Brush Background => Theme.Get("Background");
    public static Brush Accent => Theme.Get("Accent");
    public static Brush Brush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    public static void Style(Window window)
    {
        window.Background = Background; window.Foreground = Ink; window.FontFamily = new FontFamily("Segoe UI"); window.FontSize = 13;
        window.Icon = new DrawingImage(Brand.Drawing()); window.UseLayoutRounding = true; window.SnapsToDevicePixels = true;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Theme.AddResources(window.Resources);
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/VaultSync;component/Themes/Controls.xaml", UriKind.Relative) });
        window.SourceInitialized += (_, _) => FitWindow(window);
        window.Loaded += (_, _) => FitWindow(window);
        void Localize() => Strings.Apply(window);
        window.Loaded += (_, _) => Localize(); Strings.Changed += Localize;
        window.Closed += (_, _) => Strings.Changed -= Localize;
        window.DpiChanged += (_, _) => window.Dispatcher.BeginInvoke(() => FitWindow(window));
    }
    internal static Button Primary(Button button) { button.Background = Theme.Get("Primary"); button.Foreground = Theme.Get("OnPrimary"); return button; }
    public static void Chrome(Window window, UIElement body, string title, string? subtitle = null, Action? close = null)
    {
        window.WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
        var frame = new Border { BorderBrush = Theme.Get("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12, 16, 12), Background = Background };
        var root = new DockPanel(); frame.Child = root; window.Content = frame;
        var caption = new DockPanel { Background = Background, Margin = new Thickness(0, 0, 0, 16), LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        void Add(string label, string tip, Action action)
        {
            var button = Button(label, (_, _) => action()); button.Tag = "WindowCommand"; button.ToolTip = tip; AutomationProperties.SetName(button, tip); button.Width = 32; button.Height = 32; button.Padding = new Thickness(2); button.Margin = new Thickness(5, 0, 0, 0); buttons.Children.Add(button);
        }
        if (window.ResizeMode != ResizeMode.NoResize)
        {
            Add("—", "Minimizar", () => window.WindowState = WindowState.Minimized);
            Add("□", "Maximizar / restaurar", () => window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
        }
        Add("×", "Fechar", close ?? window.Close);
        DockPanel.SetDock(buttons, Dock.Right); caption.Children.Add(buttons);
        var brand = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var logo = Brand.Logo(32); DockPanel.SetDock(logo, Dock.Left); brand.Children.Add(logo);
        var labels = new StackPanel(); labels.Children.Add(new TextBlock { Text = title, Foreground = Ink, FontWeight = FontWeights.SemiBold, FontSize = 17, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = window.Title });
        if (subtitle != null) labels.Children.Add(new TextBlock { Text = subtitle, Foreground = Muted, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
        brand.Children.Add(labels); caption.Children.Add(brand);
        caption.MouseLeftButtonDown += (_, e) =>
        {
            if (AncestorButton(e.OriginalSource as DependencyObject)) return;
            if (e.ClickCount == 2 && window.ResizeMode != ResizeMode.NoResize) window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else if (e.LeftButton == MouseButtonState.Pressed) window.DragMove();
        };
        DockPanel.SetDock(caption, Dock.Top); root.Children.Add(caption); root.Children.Add(body);
    }
    private static bool AncestorButton(DependencyObject? item)
    {
        while (item is Visual) { if (item is Button) return true; item = VisualTreeHelper.GetParent(item); } return false;
    }
    internal static void FitWindow(Window window, Rect? workArea = null)
    {
        if (window.WindowState != WindowState.Normal) return;
        var owner = window.Owner;
        var handle = new WindowInteropHelper(owner ?? window).Handle;
        var area = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(owner ?? window);
        var bounds = workArea ?? new Rect(area.Left / dpi.DpiScaleX, area.Top / dpi.DpiScaleY, area.Width / dpi.DpiScaleX, area.Height / dpi.DpiScaleY);
        double width = Math.Max(200, bounds.Width - 24), height = Math.Max(160, bounds.Height - 24);
        window.MinWidth = Math.Min(window.MinWidth, width); window.MinHeight = Math.Min(window.MinHeight, height);
        window.Width = Math.Min(window.Width, width); window.Height = Math.Min(window.Height, height);
        double x = owner == null ? bounds.Left + (bounds.Width - window.Width) / 2 : owner.Left + (owner.ActualWidth - window.Width) / 2;
        double y = owner == null ? bounds.Top + (bounds.Height - window.Height) / 2 : owner.Top + (owner.ActualHeight - window.Height) / 2;
        window.Left = Math.Clamp(x, bounds.Left + 12, Math.Max(bounds.Left + 12, bounds.Right - window.Width - 12));
        window.Top = Math.Clamp(y, bounds.Top + 12, Math.Max(bounds.Top + 12, bounds.Bottom - window.Height - 12));
    }
    public static TextBlock Text(string text, double size = 13, Brush? color = null) => new() { Text = text, FontSize = size, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 6) };
    public static Button Button(string text, RoutedEventHandler action) { var b = new Button { Content = text }; AutomationProperties.SetName(b, text); b.Click += action; return b; }
    public static WrapPanel Row(params UIElement[] items) { var row = new WrapPanel { Orientation = Orientation.Horizontal }; foreach (var i in items) row.Children.Add(i); return row; }
    internal static Border Card(UIElement child) => new() { Background = Surface, BorderBrush = Theme.Get("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10), Child = child };
    internal static UIElement Empty(string title, string description)
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Ink });
        panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 0), MaxWidth = 430, Foreground = Muted }); return panel;
    }
}
