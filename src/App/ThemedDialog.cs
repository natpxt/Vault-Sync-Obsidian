using System.Windows;
using System.Windows.Controls;

namespace VaultSync.App;

internal sealed class ThemedDialog : Window
{
    internal ThemedDialog(string message, bool confirmation)
    {
        Title = Program.Product; Width = 460; Height = 245; MinWidth = 300; MinHeight = 190; ResizeMode = ResizeMode.NoResize;
        Ui.Style(this);
        var root = new DockPanel { Margin = new Thickness(8) };
        Ui.Chrome(this, root, confirmation ? "Confirmar alteração" : Program.Product);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var accept = Ui.Button(confirmation ? "Aplicar escolha" : "Entendi", (_, _) => DialogResult = true); accept.IsDefault = true;
        if (confirmation) { var cancel = Ui.Button("Cancelar", (_, _) => DialogResult = false); cancel.IsCancel = true; buttons.Children.Add(cancel); }
        buttons.Children.Add(accept); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = Ui.Text(message), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }
    public static bool Confirm(Window? owner, string message) => Show(owner, message, true);
    public static void Info(Window? owner, string message) => Show(owner, message, false);
    private static bool Show(Window? owner, string message, bool confirmation)
    {
        var dialog = new ThemedDialog(message, confirmation);
        owner ??= System.Windows.Application.Current?.MainWindow;
        if (owner?.IsVisible == true) dialog.Owner = owner;
        return dialog.ShowDialog() == true;
    }
}
