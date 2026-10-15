using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using VaultSync.Core;

namespace VaultSync.App;

internal sealed class ConflictWindow : Window
{
    private readonly TextBox merge = new() { AcceptsReturn = true, AcceptsTab = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), FontSize = 13 };
    public string? Choice { get; private set; }
    public string MergeText => merge.Text;
    public ConflictWindow(ComparisonSnapshot snapshot, string? deviceName = null)
    {
        var conflict = snapshot.Conflict;
        Title = "Comparar conflito · " + Program.Product; Width = 940; Height = 640; MinWidth = 620; MinHeight = 420; Ui.Style(this);
        var root = new DockPanel { Margin = new Thickness(6) }; Ui.Chrome(this, root, "Comparar conflito · Vault Sync");
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = conflict.Path, FontSize = 16, Foreground = Ui.Accent, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = conflict.Path, Margin = new Thickness(0, 2, 0, 5) }); header.Children.Add(Ui.Text(conflict.Reason, 12, Ui.Muted));
        header.Children.Add(Ui.Text("Este PC: " + (deviceName ?? Environment.MachineName) + ". A autoria da versão recebida pelo iCloud pode não estar disponível.", 12, Ui.Muted));
        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(Ui.Button("Usar local", (_, _) => Choose("local"))); buttons.Children.Add(Ui.Button("Usar iCloud", (_, _) => Choose("cloud")));
        buttons.Children.Add(Ui.Button("Salvar combinação", (_, _) => Choose("merge"))); buttons.Children.Add(Ui.Button("Decidir depois", (_, _) => Close()));
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        bool markdown = conflict.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
        ((Button)buttons.Children[2]).IsEnabled = snapshot.CanMerge;
        buttons.Children.Add(Ui.Button("Versões preservadas", (_, _) =>
        {
            if (Directory.Exists(conflict.Folder)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = conflict.Folder, UseShellExecute = true, Verb = "open" });
        }));
        var tabs = new TabControl { Background = Ui.Surface };
        root.Children.Add(tabs);
        string local = snapshot.LocalText, cloud = snapshot.CloudText;
        var diff = new Grid(); diff.ColumnDefinitions.Add(new()); diff.ColumnDefinitions.Add(new());
        var left = DiffPane("LOCAL", conflict.Local, local, cloud, conflict.Cloud); var right = DiffPane("ICLOUD", conflict.Cloud, cloud, local, conflict.Local); Grid.SetColumn(right, 1); diff.Children.Add(left); diff.Children.Add(right);
        tabs.Items.Add(new TabItem { Header = "Diferenças", Content = diff });
        if (markdown && snapshot.CanMerge)
        {
            var previews = new Grid(); previews.ColumnDefinitions.Add(new()); previews.ColumnDefinitions.Add(new());
            var a = new FlowDocumentScrollViewer { Document = MarkdownPreview(local), IsToolBarVisible = false };
            var b = new FlowDocumentScrollViewer { Document = MarkdownPreview(cloud), IsToolBarVisible = false }; Grid.SetColumn(b, 1); previews.Children.Add(a); previews.Children.Add(b);
            tabs.Items.Add(new TabItem { Header = "Pré-visualização Markdown", Content = previews });
            merge.Text = conflict.Local == null ? cloud : local;
            tabs.Items.Add(new TabItem { Header = "Combinar manualmente", Content = merge });
        }
    }
    private void Choose(string choice)
    {
        if (!ThemedDialog.Confirm(this, "Aplicar esta escolha às duas pastas? O conteúdo será revalidado antes da alteração.")) return;
        Choice = choice; DialogResult = true;
    }
    private static UIElement DiffPane(string title, FileStamp? stamp, string text, string other, FileStamp? opposite)
    {
        var root = new DockPanel { Margin = new Thickness(6) };
        bool newest = stamp != null && opposite != null && stamp.ModifiedUtcTicks > opposite.ModifiedUtcTicks;
        var label = Ui.Text(title + (newest ? " · MAIS RECENTE" : "") + "\n" + (stamp == null ? "excluído" : $"{stamp.Length:N0} bytes · {new DateTime(stamp.ModifiedUtcTicks, DateTimeKind.Utc).ToLocalTime():dd/MM/yyyy HH:mm:ss}"), 11, newest ? Ui.Accent : Ui.Muted);
        DockPanel.SetDock(label, Dock.Top); root.Children.Add(label);
        var document = new FlowDocument { FontFamily = new FontFamily("Consolas"), FontSize = 12, Background = Ui.Surface, Foreground = Ui.Ink, PagePadding = new Thickness(10) };
        var lines = text.Replace("\r\n", "\n").Split('\n'); var theirs = other.Replace("\r\n", "\n").Split('\n');
        int prefix = 0; while (prefix < Math.Min(lines.Length, theirs.Length) && lines[prefix] == theirs[prefix]) prefix++;
        int suffix = 0; while (suffix < Math.Min(lines.Length, theirs.Length) - prefix && lines[^(suffix + 1)] == theirs[^(suffix + 1)]) suffix++;
        for (int i = 0; i < lines.Length; i++)
        {
            var p = new Paragraph(new Run($"{i + 1,4}  {lines[i]}")) { Margin = new Thickness(0), LineHeight = 19, TextAlignment = TextAlignment.Left };
            if (i >= prefix && i < lines.Length - suffix) p.Background = new SolidColorBrush(Color.FromRgb(78, 57, 32));
            document.Blocks.Add(p);
        }
        root.Children.Add(new FlowDocumentScrollViewer { Document = document, IsToolBarVisible = false }); return root;
    }
    internal static FlowDocument MarkdownPreview(string text)
    {
        // Safe, deliberately limited native renderer: no browser, HTML, scripts, URLs or image fetches.
        var doc = new FlowDocument { Background = Ui.Surface, Foreground = Ui.Ink, FontFamily = new FontFamily("Segoe UI"), FontSize = 14, PagePadding = new Thickness(20), TextAlignment = TextAlignment.Left };
        bool code = false;
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal)) { code = !code; continue; }
            int heading = line.TakeWhile(c => c == '#').Count();
            var p = new Paragraph(new Run(heading is > 0 and < 7 && !code ? line[heading..].TrimStart() : line)) { Margin = new Thickness(0, 3, 0, 6) };
            if (code) { p.FontFamily = new FontFamily("Consolas"); p.Background = Brushes.Black; }
            else if (heading is > 0 and < 7) { p.FontSize = 26 - heading * 2; p.FontWeight = FontWeights.SemiBold; }
            doc.Blocks.Add(p);
        }
        return doc;
    }
}
