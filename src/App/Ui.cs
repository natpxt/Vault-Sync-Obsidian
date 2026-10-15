using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;

namespace VaultSync.App;

internal static class Ui
{
    public static readonly Brush Ink = Brush("#E9E5F2");
    public static readonly Brush Muted = Brush("#ADA4BF");
    public static readonly Brush Surface = Brush("#211D2D");
    public static readonly Brush Background = Brush("#17141F");
    public static readonly Brush Accent = Brush("#A78BFA");
    public static Brush Brush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    public static void Style(Window window)
    {
        window.Background = Background; window.Foreground = Ink; window.FontFamily = new FontFamily("Segoe UI"); window.FontSize = 13;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/VaultSync;component/Themes/Controls.xaml", UriKind.Relative) });
        window.SourceInitialized += (_, _) => FitWindow(window);
        window.Loaded += (_, _) => FitWindow(window);
        window.DpiChanged += (_, _) => window.Dispatcher.BeginInvoke(() => FitWindow(window));
        var buttons = new Style(typeof(Button));
        buttons.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 7, 12, 7)));
        buttons.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 3, 7, 3)));
        buttons.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#7855C8")));
        buttons.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
        buttons.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        var bt = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(content); bt.VisualTree = border;
        buttons.Setters.Add(new Setter(Control.TemplateProperty, bt));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(UIElement.OpacityProperty, .85)); buttons.Triggers.Add(hover);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .38)); buttons.Triggers.Add(disabled); window.Resources.Add(typeof(Button), buttons);
        var input = new Style(typeof(TextBox));
        input.Setters.Add(new Setter(Control.BackgroundProperty, Surface)); input.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
        input.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7))); input.Setters.Add(new Setter(Control.BorderBrushProperty, Brush("#4B405E")));
        input.Setters.Add(new Setter(TextBox.CaretBrushProperty, Accent)); window.Resources.Add(typeof(TextBox), input);
        var tab = new Style(typeof(TabItem)); tab.Setters.Add(new Setter(Control.ForegroundProperty, Muted)); tab.Setters.Add(new Setter(Control.BackgroundProperty, Background));
        var template = new ControlTemplate(typeof(TabItem)); var tabBorder = new FrameworkElementFactory(typeof(Border));
        tabBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(5, 5, 0, 0)); tabBorder.SetValue(Border.PaddingProperty, new Thickness(12, 8, 12, 8));
        tabBorder.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        var label = new FrameworkElementFactory(typeof(ContentPresenter)); label.SetValue(ContentPresenter.ContentSourceProperty, "Header"); tabBorder.AppendChild(label); template.VisualTree = tabBorder;
        tab.Setters.Add(new Setter(Control.TemplateProperty, template)); var selected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, Surface)); selected.Setters.Add(new Setter(Control.ForegroundProperty, Accent)); tab.Triggers.Add(selected); window.Resources.Add(typeof(TabItem), tab);
        var list = new Style(typeof(ListBox)); list.Setters.Add(new Setter(Control.BackgroundProperty, Surface)); list.Setters.Add(new Setter(Control.ForegroundProperty, Ink)); window.Resources.Add(typeof(ListBox), list);
        var grid = new Style(typeof(DataGrid)); grid.Setters.Add(new Setter(Control.BackgroundProperty, Surface)); grid.Setters.Add(new Setter(Control.ForegroundProperty, Ink)); grid.Setters.Add(new Setter(DataGrid.RowBackgroundProperty, Surface)); grid.Setters.Add(new Setter(DataGrid.GridLinesVisibilityProperty, DataGridGridLinesVisibility.None)); grid.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0))); window.Resources.Add(typeof(DataGrid), grid);
        var header = new Style(typeof(DataGridColumnHeader)); header.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#2C253B"))); header.Setters.Add(new Setter(Control.ForegroundProperty, Muted)); header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7))); window.Resources.Add(typeof(DataGridColumnHeader), header);
    }
    public static void Chrome(Window window, UIElement body, string title, string? subtitle = null, Action? close = null)
    {
        window.WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
        var frame = new Border { BorderBrush = Brush("#493D60"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 9, 14, 10), Background = Background };
        var root = new DockPanel(); frame.Child = root; window.Content = frame;
        var caption = new DockPanel { Background = Background, Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        void Add(string label, string tip, Action action)
        {
            var button = Button(label, (_, _) => action()); button.ToolTip = tip; button.Width = 30; button.Height = 30; button.Padding = new Thickness(2); button.Margin = new Thickness(5, 0, 0, 0); button.Background = Surface; buttons.Children.Add(button);
        }
        if (window.ResizeMode != ResizeMode.NoResize)
        {
            Add("—", "Minimizar", () => window.WindowState = WindowState.Minimized);
            Add("□", "Maximizar / restaurar", () => window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
        }
        Add("×", "Fechar", close ?? window.Close);
        DockPanel.SetDock(buttons, Dock.Right); caption.Children.Add(buttons);
        var name = new TextBlock { Text = title + (subtitle == null ? "" : "  ·  " + subtitle), Foreground = Accent, FontSize = subtitle == null ? 15 : 19, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 0), ToolTip = window.Title };
        caption.Children.Add(name);
        caption.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource != caption && e.OriginalSource is not TextBlock) return;
            if (e.ClickCount == 2 && window.ResizeMode != ResizeMode.NoResize) window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else if (e.LeftButton == MouseButtonState.Pressed) window.DragMove();
        };
        DockPanel.SetDock(caption, Dock.Top); root.Children.Add(caption); root.Children.Add(body);
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
    public static TextBlock Text(string text, double size = 13, Brush? color = null) => new() { Text = text, FontSize = size, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 5) };
    public static Button Button(string text, RoutedEventHandler action) { var b = new Button { Content = text }; b.Click += action; return b; }
    public static StackPanel Row(params UIElement[] items) { var row = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var i in items) row.Children.Add(i); return row; }
}
