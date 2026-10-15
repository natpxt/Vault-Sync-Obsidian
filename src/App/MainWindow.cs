using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using Microsoft.Win32;
using VaultSync.Core;
using Forms = System.Windows.Forms;

namespace VaultSync.App;

internal sealed class MainWindow : Window
{
    private readonly TextBox local = new(), cloud = new();
    private readonly TextBlock stateLabel = Ui.Text("PARADO", 19, Ui.Accent), detail = Ui.Text("Escolha as pastas e clique em Iniciar sincronização.", 12, Ui.Muted);
    private readonly TextBlock counts = Ui.Text("0 conflitos · 0 pendências", 12), heartbeat = Ui.Text("Monitor desativado", 11, Ui.Muted);
    private readonly TextBlock previewSummary = Ui.Text("A prévia mostra o que será copiado antes de iniciar.", 12, Ui.Muted);
    private readonly TextBox report = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly DataGrid preview = Grid(), logGrid = Grid();
    private readonly ListBox conflicts = new() { DisplayMemberPath = "Path" };
    private readonly ObservableCollection<LogRow> logRows = [];
    private readonly CheckBox startup = new() { Content = "Abrir com o Windows", IsChecked = true, Foreground = Ui.Ink };
    private readonly CheckBox layouts = new() { Content = "Incluir layouts do Obsidian (workspace*.json)", Foreground = Ui.Ink, ToolTip = "Desativado por padrão: layouts são específicos de cada dispositivo e mudam constantemente." };
    private readonly Button start, stopButton, pause, check;
    private readonly Button compare, refresh;
    private readonly List<Button> folderButtons = [];
    private readonly TextBlock comparisonStatus = Ui.Text("Selecione um arquivo e use Comparar; duplo clique ou Enter também abre.", 12, Ui.Muted);
    private readonly Forms.NotifyIcon tray;
    private readonly ActivityLog log;
    private readonly string dataRoot;
    private readonly bool testMode;
    private readonly TabControl tabs = new() { Background = Ui.Surface, BorderThickness = new Thickness(0) };
    private SyncEngine? prepared;
    private SyncPlan? plan;
    private SyncWorker? worker;
    private SyncConfig? activeConfig;
    private CancellationTokenSource? operation;
    private bool exiting, closed, loaded, busy;
    private string conflictSignature = "";
    public MainWindow(bool testMode = false, string? dataRoot = null)
    {
        this.testMode = testMode;
        this.dataRoot = dataRoot ?? (testMode ? Path.Combine(Path.GetTempPath(), "VaultSync-ui-" + Guid.NewGuid().ToString("N")) : AppFiles.DataRoot);
        Directory.CreateDirectory(this.dataRoot); log = new(this.dataRoot); log.Added += OnLog;
        Title = Program.Product; Width = 860; Height = 650; MinWidth = 710; MinHeight = 520; Ui.Style(this);
        var root = new DockPanel(); Ui.Chrome(this, root, "Vault Sync", "Obsidian · " + Program.Version, async () => await Exit());
        var state = new Border { Background = Ui.Surface, CornerRadius = new CornerRadius(7), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 6) };
        var statePanel = new StackPanel(); state.Child = statePanel; statePanel.Children.Add(stateLabel); statePanel.Children.Add(detail); statePanel.Children.Add(Ui.Row(counts, new TextBlock { Text = "     ", Foreground = Ui.Muted }, heartbeat)); DockPanel.SetDock(state, Dock.Top); root.Children.Add(state);
        start = Ui.Button("▶ Iniciar sincronização", async (_, _) => await RunJob(StartRequested));
        stopButton = Ui.Button("■ Parar", async (_, _) => await RunJob(StopMonitoring));
        pause = Ui.Button("Pausar", (sender, _) => { if (worker == null) return; worker.Pause(!worker.Paused); ((Button)sender).Content = worker.Paused ? "Retomar" : "Pausar"; });
        check = Ui.Button("Conferir agora", (_, _) => worker?.RequestScan());
        var actions = Ui.Row(start, stopButton, pause, check); DockPanel.SetDock(actions, Dock.Top); root.Children.Add(actions);
        var foot = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
        var hide = Ui.Button("Minimizar para bandeja", (_, _) => { Hide(); log.Write("INFO", "Janela recolhida. O estado do monitor aparece no ícone da bandeja."); }); DockPanel.SetDock(hide, Dock.Right); foot.Children.Add(hide);
        foot.Children.Add(Ui.Text("Cópia entre pastas deste PC. A entrega à nuvem depende do iCloud.", 10, Ui.Muted)); DockPanel.SetDock(foot, Dock.Bottom); root.Children.Add(foot); root.Children.Add(tabs);
        var folders = new DockPanel { Margin = new Thickness(10) }; var fields = new StackPanel();
        var fieldsScroll = new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        folders.SizeChanged += (_, _) => fieldsScroll.MaxHeight = Math.Max(150, folders.ActualHeight - 70);
        DockPanel.SetDock(fieldsScroll, Dock.Top); folders.Children.Add(fieldsScroll);
        fields.Children.Add(Ui.Text("Pasta local de trabalho", 12)); fields.Children.Add(FolderRow(local)); fields.Children.Add(Ui.Text("Pasta correspondente no iCloud", 12)); fields.Children.Add(FolderRow(cloud));
        fields.Children.Add(Ui.Row(Ui.Button("Verificar / prévia", async (_, _) => await RunJob(PrepareCore)), Ui.Button("Abrir pasta local", (_, _) => OpenFolder(local.Text, false)), Ui.Button("Abrir iCloud", (_, _) => OpenFolder(cloud.Text, false))));
        fields.Children.Add(Ui.Text("Regra: conteúdo diferente → prevalece a data de modificação mais recente, em qualquer pasta. Datas iguais e exclusão contra edição pedem escolha.", 11, Ui.Accent));
        fields.Children.Add(previewSummary); preview.Columns.Add(Column("Arquivo", "Path", true)); preview.Columns.Add(Column("Ação prevista", "Action")); folders.Children.Add(preview); tabs.Items.Add(new TabItem { Header = "Pastas e prévia", Content = folders });
        var conflictPanel = new DockPanel { Margin = new Thickness(10) }; var conflictHead = new StackPanel();
        conflictHead.Children.Add(Ui.Text("Datas iguais, exclusão contra edição e recuperações exigem escolha. Datas diferentes usam a versão mais recente ao iniciar.", 12, Ui.Muted));
        compare = Ui.Button("Comparar e resolver", async (_, _) => await RunJob(Resolve)); refresh = Ui.Button("Atualizar lista", async (_, _) => await RunJob(RefreshConflicts));
        conflictHead.Children.Add(Ui.Row(compare, refresh)); conflictHead.Children.Add(comparisonStatus);
        conflicts.MouseDoubleClick += async (_, _) => { if (conflicts.SelectedItem != null) await RunJob(Resolve); };
        conflicts.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await RunJob(Resolve); } };
        DockPanel.SetDock(conflictHead, Dock.Top); conflictPanel.Children.Add(conflictHead); conflictPanel.Children.Add(conflicts); tabs.Items.Add(new TabItem { Header = "Conflitos", Content = conflictPanel });
        logGrid.Columns.Add(Column("Hora", "Time")); logGrid.Columns.Add(Column("Estado", "Level")); logGrid.Columns.Add(Column("Execução", "Text", true)); logGrid.ItemsSource = logRows;
        var logPanel = new DockPanel { Margin = new Thickness(10) }; var logButtons = Ui.Row(Ui.Button("Abrir logs", (_, _) => OpenFolder(Path.Combine(this.dataRoot, "logs"), true)));
        DockPanel.SetDock(logButtons, Dock.Top); logPanel.Children.Add(logButtons); logPanel.Children.Add(logGrid); tabs.Items.Add(new TabItem { Header = "Log de execução", Content = logPanel });
        var settings = new DockPanel { Margin = new Thickness(10) }; var options = new StackPanel(); DockPanel.SetDock(options, Dock.Top); settings.Children.Add(options);
        options.Children.Add(startup); options.Children.Add(layouts);
        options.Children.Add(Ui.Text("Layouts = abas, painéis e barras laterais (workspace*.json). Notas, temas, plugins e demais configurações continuam incluídos. Pare o monitor para mudar esta opção e inicie novamente.", 11, Ui.Muted));
        options.Children.Add(Ui.Text("Backup é uma cópia avulsa para recuperação antes de alterações. É opcional: o monitor inicia e funciona sem ele.", 12, Ui.Muted));
        options.Children.Add(Ui.Row(Ui.Button("Criar backup opcional", async (_, _) => await RunJob(Backup)), Ui.Button("Abrir dados e backups", (_, _) => OpenFolder(this.dataRoot, true))));
        options.Children.Add(Ui.Button("Revisar referências Obsidian / Codex", (_, _) => ReviewIntegration()));
        settings.Children.Add(report); tabs.Items.Add(new TabItem { Header = "Opções e backups", Content = settings });
        local.Text = testMode ? @"C:\Users\demo\Documents\MegaBrain" : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MegaBrain");
        cloud.Text = testMode ? @"C:\Users\demo\iCloudDrive\iCloud~md~obsidian\MegaBrain" : FindCloud();
        tray = new Forms.NotifyIcon { Text = "Vault Sync · PARADO", Icon = System.Drawing.SystemIcons.Application, Visible = !testMode };
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add("Abrir", null, (_, _) => Dispatcher.BeginInvoke(() => { Show(); Activate(); }));
        menu.Items.Add("Iniciar", null, (_, _) => Dispatcher.BeginInvoke(async () => await RunJob(StartRequested)));
        menu.Items.Add("Parar", null, (_, _) => Dispatcher.BeginInvoke(async () => await RunJob(StopMonitoring)));
        menu.Items.Add("Conferir agora", null, (_, _) => worker?.RequestScan()); menu.Items.Add("Sair", null, (_, _) => Dispatcher.BeginInvoke(async () => await Exit())); tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(() => { Show(); Activate(); });
        Closing += (_, e) => { if (!closed) { e.Cancel = true; _ = Exit(); } };
        if (!testMode) Loaded += (_, _) => LoadSavedConfiguration();
        SystemEvents.PowerModeChanged += PowerChanged;
        foreach (var entry in log.Snapshot()) AddLog(entry);
        UpdateButtons();
    }
    private static DataGrid Grid() => new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, EnableRowVirtualization = true, HeadersVisibility = DataGridHeadersVisibility.Column };
    private static DataGridTextColumn Column(string header, string binding, bool stretch = false) => new() { Header = header, Binding = new System.Windows.Data.Binding(binding), Width = stretch ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto };
    private static Button WindowButton(string text, string tip, RoutedEventHandler action) { var button = Ui.Button(text, action); button.ToolTip = tip; button.Width = 31; button.Padding = new Thickness(2); button.Background = Ui.Surface; return button; }
    private UIElement FolderRow(TextBox input)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var browse = Ui.Button("Escolher pasta", (_, _) => { if (worker != null || busy) return; var dialog = new OpenFolderDialog { Title = "Escolha a pasta" }; if (Directory.Exists(input.Text)) dialog.InitialDirectory = input.Text; if (dialog.ShowDialog(this) == true) input.Text = dialog.FolderName; }); browse.ToolTip = "Escolher pasta";
        var create = Ui.Button("Criar", (_, _) => { if (worker != null || busy) return; try { Paths.CheckAncestors(input.Text); Directory.CreateDirectory(input.Text); log.Write("INFO", "Pasta criada: " + input.Text); } catch (Exception ex) { ShowError(ex); } });
        input.MinWidth = 0; input.VerticalContentAlignment = VerticalAlignment.Center; input.Margin = new Thickness(0, 3, 8, 3);
        browse.Margin = new Thickness(0, 3, 8, 3); create.Margin = new Thickness(0, 3, 0, 3);
        System.Windows.Controls.Grid.SetColumn(browse, 1); System.Windows.Controls.Grid.SetColumn(create, 2); row.Children.Add(input); row.Children.Add(browse); row.Children.Add(create);
        folderButtons.Add(browse); folderButtons.Add(create); return row;
    }
    private static string FindCloud()
    {
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var p in new[] { "iCloudDrive", "iCloud Drive" }) { string candidate = Path.Combine(user, p, "iCloud~md~obsidian", "MegaBrain"); if (Directory.Exists(candidate)) return candidate; }
        return "";
    }
    public void LoadSavedConfiguration()
    {
        if (loaded) return; loaded = true;
        try
        {
            var config = AppFiles.LoadConfig(dataRoot); if (config == null) { log.Write("INFO", "Aplicativo aberto. Monitor PARADO: configure as pastas e clique em Iniciar sincronização."); return; }
            local.Text = config.LocalRoot; cloud.Text = config.CloudRoot; startup.IsChecked = config.StartWithWindows; layouts.IsChecked = config.IncludeWorkspaceLayouts;
            if (config.CanAutoStart) _ = RunJob(async () => { BeginWorker(new(config, dataRoot), config); await Task.CompletedTask; });
            else if (config.MonitorOnLaunch) { detail.Text = "Nova regra: vence a data de modificação mais recente. Revise a prévia e clique em Iniciar sincronização para ativá-la."; log.Write("AVISO", detail.Text); }
            else log.Write("INFO", "Configuração carregada. Monitor PARADO até clicar em Iniciar sincronização.");
        }
        catch (Exception ex) { ShowError(ex); Show(); }
    }
    private SyncConfig ReadConfig() => new(Paths.Normalize(local.Text), Paths.Normalize(cloud.Text), startup.IsChecked == true, false, layouts.IsChecked == true);
    private async Task RunJob(Func<Task> action)
    {
        if (closed) return; if (busy) { comparisonStatus.Text = "Uma operação está em andamento. Aguarde a conclusão indicada no log."; return; } busy = true; operation = new(); UpdateButtons();
        try { await action(); }
        catch (OperationCanceledException) { log.Write("INFO", "Operação cancelada."); }
        catch (Exception ex) { ShowError(ex); }
        finally { busy = false; operation.Dispose(); operation = null; UpdateButtons(); if (exiting) await Exit(); }
    }
    private async Task PrepareCore()
    {
        if (worker != null) { worker.RequestScan(); return; }
        stateLabel.Text = "VERIFICANDO"; detail.Text = "Lendo arquivos para mostrar a prévia…"; log.Write("INFO", "Verificação das pastas iniciada.");
        prepared?.Dispose(); prepared = null; plan = null;
        var config = ReadConfig(); var validation = await Task.Run(() => Scanner.Validate(config, dataRoot, true));
        report.Text = string.Join("\n", validation.Errors.Select(x => "BLOQUEIO · " + x).Concat(validation.Warnings.Select(x => "AVISO · " + x)));
        if (!validation.CanContinue) throw new IOException(string.Join(" | ", validation.Errors));
        AppFiles.SaveConfig(config, dataRoot); prepared = new(config, dataRoot); prepared.Activity += log.Write;
        plan = await Task.Run(() => { prepared.RefreshConflictRecords(); return prepared.Preview(operation?.Token ?? default); });
        preview.ItemsSource = plan.Items.Where(p => p.Action != SyncAction.Equal).Select(p => new { p.Path, Action = Label(p.Action) }).ToList();
        report.AppendText("\n" + string.Join("\n", plan.Errors));
        previewSummary.Text = $"Prévia: {plan.Items.Count(p => p.Action is SyncAction.Upload or SyncAction.Download)} cópias · {plan.Items.Count(p => p.Action == SyncAction.Conflict)} conflitos · {plan.Errors.Count} leituras adiadas.";
        SetConflicts(PreviewConflicts()); counts.Text = $"{conflicts.Items.Count} conflitos na prévia · {plan.Errors.Count} leituras adiadas";
        stateLabel.Text = "PARADO · PRÉVIA PRONTA"; detail.Text = "Clique em Iniciar sincronização para copiar e monitorar automaticamente.";
        log.Write("INFO", previewSummary.Text); if (!plan.RootsAvailable) throw new IOException("Uma das pastas não está acessível. Corrija e tente iniciar novamente.");
    }
    private List<Conflict> PreviewConflicts()
    {
        var decisions = plan?.Items.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);
        var list = prepared?.Store.Conflicts().Where(c => !Scanner.IsInternal(c.Path, prepared.Config.IncludeWorkspaceLayouts) &&
            (decisions == null || !decisions.TryGetValue(c.Path, out var decision) || (decision.Action != SyncAction.Equal && (c.RequiresExplicitResolution || decision.Action == SyncAction.Conflict)))).ToList() ?? [];
        foreach (var item in plan?.Items.Where(p => p.Action == SyncAction.Conflict) ?? [])
            if (!list.Any(c => c.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase))) list.Add(new(item.Path, item.Local, item.Cloud, "", "Diferença da prévia: escolha uma versão antes ou depois de iniciar."));
        return list;
    }
    private async Task StartRequested()
    {
        if (worker != null) return;
        await PrepareCore(); operation?.Token.ThrowIfCancellationRequested();
        if (prepared == null || plan?.RootsAvailable != true) return;
        var config = prepared.Config with { MonitorOnLaunch = true, PolicyRevision = 1 }; AppFiles.SaveConfig(config, dataRoot);
        prepared.Store.SetSetting("activated", DateTimeOffset.Now.ToString("O"));
        var engine = prepared; engine.Activity -= log.Write; prepared = null; plan = null; BeginWorker(engine, config);
        if (!testMode && Installer.IsInstalledExecutable) Installer.SetStartup(config.StartWithWindows);
        tabs.SelectedIndex = 2;
    }
    private void BeginWorker(SyncEngine engine, SyncConfig config)
    {
        activeConfig = config; worker = new(engine, log); worker.Status += s => { if (!closed) Dispatcher.BeginInvoke(() => Update(s)); }; worker.Start();
        stateLabel.Text = "ATIVO · INICIANDO"; detail.Text = "Primeira conferência em andamento. O monitor continuará ativo."; UpdateButtons();
    }
    private async Task StopMonitoring()
    {
        var current = worker; worker = null;
        if (current != null) await Task.Run(current.Dispose);
        if (activeConfig != null) { activeConfig = activeConfig with { MonitorOnLaunch = false }; AppFiles.SaveConfig(activeConfig, dataRoot); }
        stateLabel.Text = "PARADO"; detail.Text = "Nenhum arquivo será copiado. Clique em Iniciar sincronização para retomar."; heartbeat.Text = "Monitor desativado"; tray.Text = "Vault Sync · PARADO";
    }
    private void Update(WorkerStatus s)
    {
        if (worker == null || closed) return;
        stateLabel.Text = s.Paused ? "PAUSADO" : "ATIVO · " + s.State.ToUpperInvariant(); detail.Text = s.Message;
        counts.Text = $"{s.Conflicts.Count} conflitos · {s.Pending.Count} pendências";
        heartbeat.Text = $"{s.WatchingRoots}/2 pastas observadas · última conferência {(s.LastCheck?.LocalDateTime.ToString("HH:mm:ss") ?? "em andamento")}";
        SetConflicts(s.Conflicts); tray.Text = "Vault Sync · " + (s.Paused ? "PAUSADO" : "ATIVO") + $" · {s.Conflicts.Count} conflitos";
        pause.Content = s.Paused ? "Retomar" : "Pausar";
    }
    private void SetConflicts(List<Conflict> items)
    {
        string signature = string.Join("|", items.Select(c => c.Path + c.Local + c.Cloud + c.Reason));
        if (signature == conflictSignature) return; conflictSignature = signature;
        var selected = (conflicts.SelectedItem as Conflict)?.Path; conflicts.ItemsSource = items;
        if (selected != null) conflicts.SelectedItem = items.FirstOrDefault(c => c.Path == selected);
        if (conflicts.SelectedItem == null && items.Count > 0) conflicts.SelectedIndex = 0;
    }
    private async Task RefreshConflicts()
    {
        if (worker != null) Update(await worker.GetStatusAsync(refreshConflicts: true));
        else { if (prepared == null) await PrepareCore(); else { plan = await Task.Run(() => { prepared.RefreshConflictRecords(); return prepared.Preview(operation?.Token ?? default); }); SetConflicts(PreviewConflicts()); } }
    }
    private async Task Resolve()
    {
        if (conflicts.SelectedItem is not Conflict conflict) { comparisonStatus.Text = "Nenhum conflito selecionado. Selecione um arquivo na lista."; return; }
        var config = activeConfig ?? prepared?.Config; if (config == null) return;
        comparisonStatus.Text = "Preparando versões atuais; aguardando a operação de sincronização em andamento…";
        try
        {
            var snapshot = worker != null ? await worker.PrepareComparisonAsync(conflict) : await Task.Run(() => prepared!.PrepareComparison(conflict));
            if (snapshot == null) { await RefreshConflicts(); comparisonStatus.Text = "Sincronizado: conteúdo idêntico nas duas pastas. Nenhum conflito para resolver."; return; }
            comparisonStatus.Text = "Comparação aberta com as versões atuais.";
            var dialog = new ConflictWindow(snapshot) { Owner = this };
            if (testMode && ComparisonOpenedSmoke != null) dialog.Loaded += (_, _) => ComparisonOpenedSmoke(dialog);
            if (dialog.ShowDialog() != true || dialog.Choice == null) return;
            if (worker != null) await worker.ResolveAsync(snapshot.Conflict, dialog.Choice, dialog.MergeText);
            else if (prepared != null) await Task.Run(() => prepared.Resolve(snapshot.Conflict, dialog.Choice, dialog.MergeText));
            await RefreshConflicts(); comparisonStatus.Text = "Escolha aplicada e verificada nas duas pastas.";
        }
        catch (Exception ex) { comparisonStatus.Text = ex.Message; if (!testMode) ThemedDialog.Info(this, ex.Message); throw; }
    }
    private async Task Backup()
    {
        if (worker != null) { log.Write("AVISO", "Pare o monitor para criar uma cópia avulsa. Isso não é necessário para sincronizar."); return; }
        if (prepared == null) await PrepareCore();
        if (prepared == null) return;
        stateLabel.Text = "BACKUP OPCIONAL";
        var currentPlan = await Task.Run(() => prepared.Preview(operation?.Token ?? default));
        string folder = await Task.Run(() => prepared.CreateMigrationBackup(currentPlan, operation?.Token ?? default));
        report.AppendText("\nBackup: " + folder); stateLabel.Text = "PARADO"; detail.Text = "Backup avulso concluído. Para sincronizar, clique em Iniciar sincronização.";
    }
    private void OpenFolder(string path, bool create)
    {
        try
        {
            string full = Path.GetFullPath(path); if (create) Directory.CreateDirectory(full);
            if (!Directory.Exists(full)) throw new IOException("A pasta ainda não existe: " + full);
            Process.Start(new ProcessStartInfo { FileName = full, UseShellExecute = true, Verb = "open" });
            log.Write("INFO", "Pasta aberta: " + full);
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void ReviewIntegration()
    {
        var config = activeConfig ?? prepared?.Config;
        if (config == null) { log.Write("INFO", "Verifique as pastas antes de revisar as referências externas."); return; }
        try { new IntegrationWindow(config) { Owner = this }.ShowDialog(); } catch (Exception ex) { ShowError(ex); }
    }
    private void ShowError(Exception ex)
    {
        log.Write("ERRO", ex.Message); detail.Text = ex.Message;
        if (worker == null) stateLabel.Text = "PARADO · VERIFIQUE O LOG";
    }
    private void OnLog(SyncEvent entry) { if (!closed) Dispatcher.BeginInvoke(() => AddLog(entry)); }
    private void AddLog(SyncEvent entry)
    {
        logRows.Add(new(entry.Time.LocalDateTime.ToString("HH:mm:ss"), entry.Level, entry.Message + (entry.Path == null ? "" : " · " + entry.Path)));
        if (worker != null && entry.Level is "OK" or "PENDENTE" or "CONFLITO") detail.Text = entry.Message + (entry.Path == null ? "" : " · " + entry.Path);
        while (logRows.Count > 500) logRows.RemoveAt(0); if (logRows.Count > 0) logGrid.ScrollIntoView(logRows[^1]);
    }
    private void UpdateButtons()
    {
        start.IsEnabled = !busy && worker == null; stopButton.IsEnabled = !busy && worker != null; pause.IsEnabled = !busy && worker != null; check.IsEnabled = !busy && worker != null;
        compare.IsEnabled = refresh.IsEnabled = !busy; foreach (var button in folderButtons) button.IsEnabled = !busy && worker == null;
        local.IsReadOnly = cloud.IsReadOnly = worker != null || busy; startup.IsEnabled = layouts.IsEnabled = !busy && worker == null;
    }
    private static string Label(SyncAction action) => action switch { SyncAction.Upload => "Local → iCloud", SyncAction.Download => "iCloud → Local", SyncAction.DeleteLocal => "Excluir local", SyncAction.DeleteCloud => "Excluir iCloud", SyncAction.Conflict => "Escolher versão", _ => "Igual" };
    private void PowerChanged(object? sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) worker?.RequestScan(); }
    private async Task Exit()
    {
        if (closed) return;
        if (busy) { exiting = true; operation?.Cancel(); detail.Text = "Cancelando a operação para fechar…"; return; }
        closed = true; SystemEvents.PowerModeChanged -= PowerChanged; tray.Visible = false; tray.Dispose();
        if (worker != null) { var current = worker; worker = null; await Task.Run(current.Dispose); }
        prepared?.Dispose(); log.Added -= OnLog; Close(); if (!testMode) System.Windows.Application.Current.Shutdown();
    }
    internal void CloseSmoke() { Exit().GetAwaiter().GetResult(); }
    internal async Task StartSmokeAsync(SyncConfig config)
    {
        local.Text = config.LocalRoot; cloud.Text = config.CloudRoot; layouts.IsChecked = config.IncludeWorkspaceLayouts;
        await RunJob(StartRequested);
        if (worker == null) throw new InvalidOperationException("O botão Iniciar não ativou o monitor.");
    }
    internal Task StopSmokeAsync() => RunJob(StopMonitoring);
    internal Task<WorkerStatus> StatusSmokeAsync() => worker?.GetStatusAsync() ?? Task.FromResult(new WorkerStatus("Parado", [], [], false, State: "Parado"));
    internal void PauseSmoke(bool value) => worker?.Pause(value);
    internal void SelectTabSmoke(int index) => tabs.SelectedIndex = index;
    internal void DemoPathsSmoke() { local.Text = @"C:\Users\demo\Documents\MegaBrain"; cloud.Text = @"C:\Users\demo\iCloudDrive\iCloud~md~obsidian\MegaBrain"; }
    internal bool IsMonitoringSmoke => worker != null;
    internal string StateText => stateLabel.Text;
    internal string ComparisonStatusSmoke => comparisonStatus.Text;
    internal Action<Window>? ComparisonOpenedSmoke { get; set; }
    internal Task OpenComparisonSmokeAsync() => RunJob(Resolve);
    internal void AssertFolderLayoutSmoke()
    {
        UpdateLayout();
        foreach (var button in folderButtons)
        {
            var point = button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2), this);
            DependencyObject? hit = InputHitTest(point) as DependencyObject;
            while (hit != null && hit != button) hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
            if (hit != button) throw new InvalidOperationException("O campo de pasta cobre um botão.");
        }
        foreach (var input in new[] { local, cloud })
        {
            var button = folderButtons[input == local ? 0 : 2];
            if (input.TranslatePoint(new Point(input.ActualWidth, 0), this).X >= button.TranslatePoint(new Point(), this).X) throw new InvalidOperationException("Campo e botão de pasta se sobrepõem.");
        }
    }
    private sealed record LogRow(string Time, string Level, string Text);
}
