using System.Windows;
using System.Windows.Controls;
using VaultSync.Updates;

namespace VaultSync.App;

internal sealed class UpdateWindow : Window
{
    internal UpdateWindow(UpdateRelease release, bool install)
    {
        Title = "Atualização disponível · Vault Sync"; Width = 620; Height = 500; MinWidth = 360; MinHeight = 300; Ui.Style(this);
        var root = new DockPanel { Margin = new Thickness(10) }; Ui.Chrome(this, root, "Atualização disponível · Vault Sync");
        var header = new StackPanel(); header.Children.Add(Ui.Text("Nova versão disponível: " + release.Version, 20, Ui.Accent));
        header.Children.Add(Ui.Text("Versão instalada: " + Program.Version, 12, Ui.Muted));
        header.Children.Add(Ui.Text(install ? "Após confirmar, o aplicativo baixa, verifica e instala a atualização. A sincronização será retomada no estado anterior." : "Esta cópia usa instalação manual pela página de releases.", 12, Ui.Muted));
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var actions = Ui.Row(Ui.Button("Depois", (_, _) => DialogResult = false), Ui.Primary(Ui.Button(install ? "Atualizar agora" : "Abrir página de releases", (_, _) => DialogResult = true)));
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var notes = Ui.Text(release.Notes); notes.Tag = "UserContent";
        root.Children.Add(new ScrollViewer { Content = notes, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }
}
