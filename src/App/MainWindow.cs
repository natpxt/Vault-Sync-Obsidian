using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using VaultSync.Core;
using Forms = System.Windows.Forms;

namespace VaultSync.App;

internal sealed class MainWindow : Window
{
    private readonly TextBox local = new(), cloud = new();
    private readonly TextBlock status = Ui.Text("Escolha as duas pastas para começar.", 14, Ui.Accent);
    private readonly TextBox report = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly DataGrid preview = new() { IsReadOnly = true, AutoGenerateColumns = false, Background = Ui.Surface, Foreground = System.Windows.Media.Brushes.Black, EnableRowVirtualization = true };
    private readonly ListBox conflicts = new() { DisplayMemberPath = "Path" };
    private readonly CheckBox previousDisabled = new() { Content = "Conferi e desativei sincronizadores anteriores sobre estas pastas", Foreground = Ui.Ink };
    private readonly CheckBox startup = new() { Content = "Iniciar com o Windows", IsChecked = true, Foreground = Ui.Ink };
    private readonly Button start;
    private readonly Button pause;
    private readonly Forms.NotifyIcon tray;
    private SyncEngine? prepared;
    private SyncPlan? plan;
    private SyncWorker? worker;
    private SyncConfig? activeConfig;
    private bool exiting;
    private bool loaded;
    private bool busy;
    public MainWindow(bool testMode = false)
    {
        Title = Program.Product; Width = 1120; Height = 850; MinHeight = 700; MinWidth = 850; Ui.Style(this);
        var root = new DockPanel { Margin = new Thickness(26) }; Content = root;
        var head = new StackPanel(); DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
        head.Children.Add(Ui.Text("VAULT SYNC", 30, Ui.Accent)); head.Children.Add(Ui.Text("Obsidian · Seus arquivos locais. Sua sincronização sob controle.", 16, Ui.Muted)); head.Children.Add(status);
        var footer = Ui.Text("O iCloud transporta alterações entre dispositivos. Este aplicativo verifica as duas pastas deste PC. Não há confirmação de entrega remota.", 12, Ui.Muted);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var tabs = new TabControl { Background = Ui.Surface }; root.Children.Add(tabs);
        var setup = new DockPanel { Margin = new Thickness(18) };
        var fields = new StackPanel(); DockPanel.SetDock(fields, Dock.Top); setup.Children.Add(fields);
        fields.Children.Add(Ui.Text("1 · Pasta local de trabalho", 16)); fields.Children.Add(FolderRow(local));
        fields.Children.Add(Ui.Text("2 · Pasta correspondente dentro do iCloud", 16)); fields.Children.Add(FolderRow(cloud));
        fields.Children.Add(Ui.Text("3 · Verifique as diferenças antes da primeira sincronização", 16));
        fields.Children.Add(previousDisabled); fields.Children.Add(startup);
        start = Ui.Button("Criar backup e iniciar", async (_, _) => await StartApproved()); start.IsEnabled = false;
        pause = Ui.Button("Pausar", (sender, _) => { if (worker == null) return; worker.Pause(!worker.Paused); ((Button)sender).Content = worker.Paused ? "Retomar" : "Pausar"; status.Text = worker.Paused ? "Pausado" : "Monitorando alterações"; });
        fields.Children.Add(Ui.Row(Ui.Button("Verificar e pré-visualizar", async (_, _) => await Prepare()), start, pause, Ui.Button("Conferir agora", (_, _) => worker?.RequestScan())));
        preview.Columns.Add(new DataGridTextColumn { Header = "Arquivo", Binding = new System.Windows.Data.Binding("Path"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        preview.Columns.Add(new DataGridTextColumn { Header = "Ação", Binding = new System.Windows.Data.Binding("Action") });
        setup.Children.Add(preview); tabs.Items.Add(new TabItem { Header = "Configurar e sincronizar", Content = setup });
        var conflictPanel = new DockPanel { Margin = new Thickness(18) };
        var conflictButtons = Ui.Row(Ui.Button("Comparar e resolver", async (_, _) => await Resolve()), Ui.Button("Atualizar lista", async (_, _) => { if (worker != null) Update(await worker.GetStatusAsync()); }));
        DockPanel.SetDock(conflictButtons, Dock.Top); conflictPanel.Children.Add(conflictButtons); conflictPanel.Children.Add(conflicts);
        tabs.Items.Add(new TabItem { Header = "Conflitos", Content = conflictPanel });
        var checks = new DockPanel { Margin = new Thickness(18) };
        var checkButtons = Ui.Row(Ui.Button("Abrir dados e backups", (_, _) => { Directory.CreateDirectory(AppFiles.DataRoot); Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { AppFiles.DataRoot } }); }), Ui.Button("Revisar integração Obsidian/Codex", (_, _) => ReviewIntegration()));
        DockPanel.SetDock(checkButtons, Dock.Top); checks.Children.Add(checkButtons); checks.Children.Add(report);
        tabs.Items.Add(new TabItem { Header = "Verificações e integração", Content = checks });
        local.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MegaBrain");
        cloud.Text = FindCloud();
        if (testMode) { local.Text = @"C:\Users\demo\Documents\MegaBrain"; cloud.Text = @"C:\Users\demo\iCloudDrive\iCloud~md~obsidian\MegaBrain"; }
        tray = new Forms.NotifyIcon { Text = "Vault Sync · Obsidian", Icon = System.Drawing.SystemIcons.Application, Visible = !testMode };
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add("Abrir", null, (_, _) => Dispatcher.BeginInvoke(() => { Show(); Activate(); }));
        menu.Items.Add("Pausar / retomar", null, (_, _) => Dispatcher.BeginInvoke(() => { if (worker != null) { worker.Pause(!worker.Paused); pause.Content = worker.Paused ? "Retomar" : "Pausar"; } }));
        menu.Items.Add("Conferir agora", null, (_, _) => worker?.RequestScan()); menu.Items.Add("Sair", null, (_, _) => Dispatcher.BeginInvoke(Exit)); tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(() => { Show(); Activate(); });
        Closing += (_, e) => { if (!exiting) { e.Cancel = true; Hide(); } };
        if (!testMode) Loaded += (_, _) => LoadSavedConfiguration();
        SystemEvents.PowerModeChanged += PowerChanged;
    }
    internal void CloseSmoke() { exiting = true; SystemEvents.PowerModeChanged -= PowerChanged; tray.Dispose(); Close(); }
    private static string FindCloud()
    {
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var basePath in new[] { Path.Combine(user, "iCloudDrive"), Path.Combine(user, "iCloud Drive") })
        {
            string candidate = Path.Combine(basePath, "iCloud~md~obsidian", "MegaBrain");
            if (Directory.Exists(candidate)) return candidate;
        }
        return "";
    }
    private static UIElement FolderRow(TextBox input)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var browse = Ui.Button("Escolher…", (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "Selecione uma pasta existente (vazia para criar a cópia local)" };
            if (Directory.Exists(input.Text)) dialog.InitialDirectory = input.Text;
            if (dialog.ShowDialog() == true) input.Text = dialog.FolderName;
        });
        var create = Ui.Button("Criar pasta", (_, _) =>
        {
            try { Paths.CheckAncestors(input.Text); Directory.CreateDirectory(input.Text); }
            catch (Exception ex) { MessageBox.Show(ex.Message, Program.Product); }
        });
        DockPanel.SetDock(browse, Dock.Right); DockPanel.SetDock(create, Dock.Right); row.Children.Add(browse); row.Children.Add(create); row.Children.Add(input); return row;
    }
    public void LoadSavedConfiguration()
    {
        if (loaded) return; loaded = true;
        try
        {
            var config = AppFiles.LoadConfig(); if (config == null) return;
            local.Text = config.LocalRoot; cloud.Text = config.CloudRoot; startup.IsChecked = config.StartWithWindows;
            var engine = new SyncEngine(config, AppFiles.DataRoot);
            if (!engine.Store.HasSetting("activated")) { engine.Dispose(); return; }
            BeginWorker(engine, config);
        }
        catch (Exception ex) { status.Text = "Não foi possível retomar: " + ex.Message; Show(); }
    }
    private async Task Prepare()
    {
        if (busy) return;
        if (worker != null) { MessageBox.Show("Saia pelo ícone da bandeja e reabra após remover a configuração local se precisar trocar as raízes. A configuração ativa está protegida contra troca acidental.", Program.Product); return; }
        busy = true; start.IsEnabled = false; status.Text = "Verificando arquivos e permissões…";
        try
        {
            prepared?.Dispose(); prepared = null;
            var config = new SyncConfig(Paths.Normalize(local.Text), Paths.Normalize(cloud.Text), startup.IsChecked == true);
            var validation = await Task.Run(() => Scanner.Validate(config, AppFiles.DataRoot, true));
            report.Text = string.Join("\n", validation.Errors.Select(x => "BLOQUEIO · " + x).Concat(validation.Warnings.Select(x => "AVISO · " + x)));
            if (!validation.CanContinue) { status.Text = "Resolva os bloqueios antes de continuar."; return; }
            prepared = new SyncEngine(config, AppFiles.DataRoot);
            plan = await Task.Run(() => prepared.Preview());
            preview.ItemsSource = plan.Items.Where(p => p.Action != SyncAction.Equal).Select(p => new { p.Path, Action = Label(p.Action) }).ToList();
            long bytes = plan.Items.Where(p => p.Action == SyncAction.Download).Sum(p => p.Cloud?.Length ?? 0) + plan.Items.Where(p => p.Action == SyncAction.Upload).Sum(p => p.Local?.Length ?? 0);
            long backupBytes = plan.Items.Sum(p => (p.Local?.Length ?? 0) + (p.Cloud?.Length ?? 0));
            var free = new DriveInfo(Path.GetPathRoot(AppFiles.DataRoot)!).AvailableFreeSpace;
            bool room = free > backupBytes + bytes + 256L * 1024 * 1024;
            report.AppendText($"\n\nArquivos comparados: {plan.Items.Count:N0}\nTransferências previstas: {bytes:N0} bytes\nBackup de migração: até {backupBytes:N0} bytes\nEspaço livre para o backup: {free:N0} bytes\n");
            report.AppendText(string.Join("\n", plan.Errors.Select(x => "BLOQUEIO · " + x)));
            if (!room) report.AppendText("\nBLOQUEIO · Espaço insuficiente para backup e cópia inicial.");
            start.IsEnabled = plan.Complete && room;
            status.Text = start.IsEnabled ? $"Prévia pronta · {plan.Items.Count(p => p.Action == SyncAction.Conflict)} conflitos. Revise antes de iniciar." : "Leitura incompleta ou espaço insuficiente; início bloqueado.";
        }
        catch (Exception ex) { status.Text = ex.Message; }
        finally { busy = false; }
    }
    private async Task StartApproved()
    {
        if (busy || prepared == null || plan == null) return;
        if (!previousDisabled.IsChecked.GetValueOrDefault()) { MessageBox.Show("Confirme que os sincronizadores anteriores foram desativados para estas pastas.", Program.Product); return; }
        if (Paths.Normalize(local.Text) != Paths.Normalize(prepared.Config.LocalRoot) || Paths.Normalize(cloud.Text) != Paths.Normalize(prepared.Config.CloudRoot)) { await Prepare(); return; }
        if (MessageBox.Show("Criar a cópia de segurança, aplicar a prévia e iniciar?\n\nExclusões confirmadas serão propagadas diretamente. Conflitos serão preservados para escolha. As alterações do iCloud também chegam à pasta local.", Program.Product, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        busy = true; start.IsEnabled = false;
        try
        {
            status.Text = "Criando e verificando backup de migração…";
            string backup = await Task.Run(() => prepared.CreateMigrationBackup(plan)); report.AppendText("\nBackup verificado: " + backup);
            status.Text = "Aplicando a prévia…"; await Task.Run(() => prepared.Apply(plan));
            var config = prepared.Config with { StartWithWindows = startup.IsChecked == true };
            AppFiles.SaveConfig(config); prepared.Store.SetSetting("activated", DateTime.UtcNow.ToString("O"));
            if (Installer.IsInstalledExecutable) Installer.SetStartup(config.StartWithWindows);
            var engine = prepared; prepared = null; BeginWorker(engine, config);
        }
        catch (Exception ex) { status.Text = "Configuração interrompida: " + ex.Message; start.IsEnabled = true; }
        finally { busy = false; }
    }
    private void BeginWorker(SyncEngine engine, SyncConfig config)
    {
        activeConfig = config; worker = new SyncWorker(engine);
        worker.Status += s => Dispatcher.BeginInvoke(() => Update(s)); worker.Start();
        local.IsReadOnly = true; cloud.IsReadOnly = true; start.IsEnabled = false; status.Text = "Monitorando alterações. Fechar a janela mantém o ícone na bandeja.";
    }
    private void Update(WorkerStatus s)
    {
        status.Text = s.Message + $" · {s.Conflicts.Count} conflitos · {s.Pending.Count} pendências";
        conflicts.ItemsSource = s.Conflicts;
        report.Text = $"{DateTime.Now:g}\n{s.Message}\n\n" + string.Join("\n", s.Pending);
        tray.Text = $"Vault Sync · {s.Conflicts.Count} conflitos";
    }
    private async Task Resolve()
    {
        if (worker == null || activeConfig == null || conflicts.SelectedItem is not Conflict conflict) return;
        try
        {
            var dialog = new ConflictWindow(activeConfig, conflict) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Choice != null) await worker.ResolveAsync(conflict, dialog.Choice, dialog.MergeText);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Program.Product); if (worker != null) Update(await worker.GetStatusAsync()); }
    }
    private void ReviewIntegration()
    {
        var config = activeConfig ?? prepared?.Config;
        if (config == null) { MessageBox.Show("Verifique as pastas primeiro.", Program.Product); return; }
        try { new IntegrationWindow(config) { Owner = this }.ShowDialog(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, Program.Product); }
    }
    private static string Label(SyncAction action) => action switch { SyncAction.Upload => "Local → iCloud", SyncAction.Download => "iCloud → Local", SyncAction.DeleteLocal => "Excluir local", SyncAction.DeleteCloud => "Excluir iCloud", SyncAction.Conflict => "Comparar conflito", _ => "Igual" };
    private void PowerChanged(object? sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) worker?.RequestScan(); }
    private async void Exit()
    {
        if (busy) { MessageBox.Show("Aguarde a operação atual terminar antes de sair.", Program.Product); return; }
        exiting = true; SystemEvents.PowerModeChanged -= PowerChanged; tray.Visible = false; tray.Dispose();
        if (worker != null) await Task.Run(worker.Dispose); prepared?.Dispose(); Close(); System.Windows.Application.Current.Shutdown();
    }
}
