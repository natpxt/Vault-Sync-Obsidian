using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Automation;
using System.Windows.Data;
using System.Globalization;
using Microsoft.Win32;
using VaultSync.Core;
using Forms = System.Windows.Forms;

namespace VaultSync.App;

internal sealed partial class MainWindow : Window
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
    private readonly Button compare, refresh, conflictStop;
    private readonly WrapPanel monitorActions;
    private readonly List<Button> folderButtons = [];
    private readonly TextBlock comparisonStatus = Ui.Text("Selecione um arquivo e use Comparar; duplo clique ou Enter também abre.", 12, Ui.Muted);
    private readonly Forms.NotifyIcon tray;
    private System.Drawing.Icon? trayIcon;
    private string trayState = "stopped";
    private string trayTextSource = "Vault Sync · PARADO";
    private readonly TextBlock stateSymbol = new() { Text = "■", FontSize = 20, Foreground = Ui.Muted, Margin = new Thickness(0, 2, 8, 0) };
    private readonly UIElement previewEmpty = Ui.Empty("Nenhuma conferência realizada", "Use Verificar / prévia para inspecionar as diferenças antes de iniciar."), conflictEmpty = Ui.Empty("Nenhum conflito encontrado", "Arquivos que precisarem da sua escolha aparecerão aqui."), logEmpty = Ui.Empty("Nenhuma atividade nesta sessão", "Verificações, cópias e avisos aparecerão aqui.");
    private readonly ComboBox themeChoice = new() { MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ComboBox languageChoice = new() { MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left };
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
    private string reportSource = "";
    private string detailMessage = "Escolha as pastas e clique em Iniciar sincronização.";
    private string? detailPath;
    public MainWindow(bool testMode = false, string? dataRoot = null, bool evaluationMode = false)
    {
        this.testMode = testMode;
        this.dataRoot = dataRoot ?? (testMode ? Path.Combine(Path.GetTempPath(), "VaultSync-ui-" + Guid.NewGuid().ToString("N")) : AppFiles.DataRoot);
        Directory.CreateDirectory(this.dataRoot); log = new(this.dataRoot); log.Added += OnLog;
        Title = Program.Product; Width = 1100; Height = 780; MinWidth = 710; MinHeight = 520; Ui.Style(this); detail.Tag = "UserContent";
        tabs.Style = (Style)FindResource("Navigation");
        var root = new DockPanel(); Ui.Chrome(this, root, "Vault Sync", "Para seus vaults do Obsidian · " + Program.Version + (evaluationMode ? " · Avaliação isolada" : ""), async () => await Exit());
        var updateBanner = BuildUpdateBanner(); DockPanel.SetDock(updateBanner, Dock.Top); root.Children.Add(updateBanner);
        var state = new Border { Background = Ui.Surface, CornerRadius = new CornerRadius(7), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 6) };
        var statePanel = new StackPanel(); state.Child = statePanel; statePanel.Children.Add(Ui.Row(stateSymbol, stateLabel)); statePanel.Children.Add(detail); counts.Margin = new Thickness(0, 3, 20, 3); statePanel.Children.Add(Ui.Row(counts, heartbeat));
        AutomationProperties.SetLiveSetting(statePanel, AutomationLiveSetting.Polite);
        start = Ui.Primary(Ui.Button("Iniciar sincronização", async (_, _) => await RunJob(StartRequested)));
        stopButton = Ui.Button("■ Parar", async (_, _) => await RunJob(StopMonitoring));
        pause = Ui.Button("Pausar", (sender, _) => { if (worker == null) return; worker.Pause(!worker.Paused); ((Button)sender).Content = worker.Paused ? "Retomar" : "Pausar"; Strings.Apply(this); });
        check = Ui.Button("Conferir agora", (_, _) => worker?.RequestScan());
        monitorActions = Ui.Row(start, pause, check, stopButton); var statusHost = new StackPanel(); statusHost.Children.Add(state); statusHost.Children.Add(monitorActions); tabs.Tag = statusHost;
        tabs.SelectionChanged += (_, e) => { if (e.Source == tabs) monitorActions.Visibility = tabs.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed; };
        var foot = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
        var hide = Ui.Button("Minimizar para bandeja", (_, _) => { Hide(); log.Write("INFO", "Janela recolhida. O estado do monitor aparece no ícone da bandeja."); }); DockPanel.SetDock(hide, Dock.Right); foot.Children.Add(hide);
        var credits = new StackPanel();
        credits.Children.Add(Ui.Text("Cópia entre pastas deste PC. A entrega à nuvem depende do iCloud.", 11, Ui.Muted));
        var author = Ui.Text("Desenvolvido por Natan Peixoto Maciel", 11, Ui.Muted); author.VerticalAlignment = VerticalAlignment.Center;
        var github = Ui.Button("GitHub", (_, _) => OpenGitHub()); github.FontSize = 11; github.Padding = new Thickness(8, 3, 8, 3); github.Margin = new Thickness(12, 0, 0, 0);
        github.ToolTip = "Abrir projeto no GitHub"; AutomationProperties.SetName(github, "Abrir projeto no GitHub");
        credits.Children.Add(Ui.Row(author, github)); foot.Children.Add(credits); DockPanel.SetDock(foot, Dock.Bottom); root.Children.Add(foot); root.Children.Add(tabs);
        var folders = new StackPanel { Margin = new Thickness(10) }; var fields = new StackPanel(); folders.Children.Add(fields); preview.MaxHeight = 340;
        var pair = new System.Windows.Controls.Grid(); pair.ColumnDefinitions.Add(new()); pair.ColumnDefinitions.Add(new()); pair.RowDefinitions.Add(new()); pair.RowDefinitions.Add(new());
        var localPanel = new StackPanel(); localPanel.Children.Add(Ui.Text("Pasta local de trabalho", 12, Ui.Muted)); localPanel.Children.Add(FolderRow(local)); localPanel.Children.Add(Ui.Button("Abrir pasta local", (_, _) => OpenFolder(local.Text, false)));
        var cloudPanel = new StackPanel(); cloudPanel.Children.Add(Ui.Text("Pasta correspondente no iCloud", 12, Ui.Muted)); cloudPanel.Children.Add(FolderRow(cloud)); cloudPanel.Children.Add(Ui.Button("Abrir iCloud", (_, _) => OpenFolder(cloud.Text, false)));
        var localCard = Ui.Card(localPanel); var cloudCard = Ui.Card(cloudPanel); pair.Children.Add(localCard); pair.Children.Add(cloudCard);
        pair.SizeChanged += (_, _) => { bool wide = pair.ActualWidth >= 720; System.Windows.Controls.Grid.SetColumn(cloudCard, wide ? 1 : 0); System.Windows.Controls.Grid.SetRow(cloudCard, wide ? 0 : 1); System.Windows.Controls.Grid.SetColumnSpan(localCard, wide ? 1 : 2); System.Windows.Controls.Grid.SetColumnSpan(cloudCard, wide ? 1 : 2); localCard.Margin = new Thickness(0, 0, wide ? 8 : 0, 10); };
        fields.Children.Add(Ui.Text("Suas pastas · Local ⇄ iCloud", 15)); fields.Children.Add(pair);
        fields.Children.Add(Ui.Primary(Ui.Button("Verificar / prévia", async (_, _) => await RunJob(PrepareCore))));
        fields.Children.Add(Ui.Text("Regra: conteúdo diferente → prevalece a data de modificação mais recente, em qualquer pasta. Datas iguais e exclusão contra edição pedem escolha.", 11, Ui.Accent));
        fields.Children.Add(previewSummary); preview.Columns.Add(Column("Arquivo", "Path", true)); preview.Columns.Add(Column("Ação prevista", "Action")); folders.Children.Add(EmptyHost(preview, previewEmpty)); tabs.Items.Add(Page("Pastas e prévia", new ScrollViewer { Content = folders, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }));
        var conflictPanel = new DockPanel { Margin = new Thickness(10) }; var conflictHead = new StackPanel();
        conflictHead.Children.Add(Ui.Text("Datas iguais, exclusão contra edição e recuperações exigem escolha. Datas diferentes usam a versão mais recente ao iniciar.", 12, Ui.Muted));
        compare = Ui.Primary(Ui.Button("Comparar e resolver", async (_, _) => await RunJob(Resolve))); refresh = Ui.Button("Atualizar lista", async (_, _) => await RunJob(RefreshConflicts));
        conflictStop = Ui.Button("■ Parar", async (_, _) => await RunJob(StopMonitoring));
        conflictHead.Children.Add(Ui.Row(compare, refresh, conflictStop)); conflictHead.Children.Add(comparisonStatus);
        conflicts.MouseDoubleClick += async (_, _) => { if (conflicts.SelectedItem != null) await RunJob(Resolve); };
        conflicts.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await RunJob(Resolve); } };
        DockPanel.SetDock(conflictHead, Dock.Top); conflictPanel.Children.Add(conflictHead); conflictPanel.Children.Add(EmptyHost(conflicts, conflictEmpty)); tabs.Items.Add(Page("Conflitos", conflictPanel));
        logGrid.Columns.Add(Column("Hora", "Time")); logGrid.Columns.Add(Column("Estado", "Level")); logGrid.Columns.Add(Column("Execução", "Text", true)); logGrid.ItemsSource = logRows;
        var logPanel = new DockPanel { Margin = new Thickness(10) }; var logButtons = Ui.Row(Ui.Button("Abrir logs", (_, _) => OpenFolder(Path.Combine(this.dataRoot, "logs"), true)));
        DockPanel.SetDock(logButtons, Dock.Top); logPanel.Children.Add(logButtons); logPanel.Children.Add(EmptyHost(logGrid, logEmpty)); tabs.Items.Add(Page("Log de execução", logPanel));
        var backups = new StackPanel { Margin = new Thickness(10) }; backups.Children.Add(Ui.Text("Backups", 18));
        backups.Children.Add(Ui.Text("Backup é uma cópia avulsa para recuperação antes de alterações. É opcional: o monitor inicia e funciona sem ele.", 12, Ui.Muted));
        backups.Children.Add(Ui.Row(Ui.Button("Criar backup opcional", async (_, _) => await RunJob(Backup)), Ui.Button("Abrir dados e backups", (_, _) => OpenFolder(this.dataRoot, true))));
        report.MinHeight = 100; report.MaxHeight = 280; backups.Children.Add(report);
        tabs.Items.Add(Page("Backups", new ScrollViewer { Content = backups, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }));
        var options = new StackPanel { Margin = new Thickness(10) };
        var appearanceOptions = new StackPanel(); appearanceOptions.Children.Add(Ui.Text("Aparência", 18)); appearanceOptions.Children.Add(Ui.Text("Tema · acompanhe o Windows ou escolha uma aparência fixa.", 12, Ui.Muted));
        foreach (string label in new[] { "Sistema", "Claro", "Escuro" }) themeChoice.Items.Add(new ComboBoxItem { Content = label }); themeChoice.SelectedIndex = (int)Theme.Mode; AutomationProperties.SetName(themeChoice, "Tema do aplicativo");
        themeChoice.SelectionChanged += (_, _) => { try { Theme.Set((ThemeMode)themeChoice.SelectedIndex); } catch (Exception ex) { themeChoice.SelectedIndex = (int)Theme.Mode; ShowError(ex); } }; appearanceOptions.Children.Add(themeChoice); options.Children.Add(OptionGroup(appearanceOptions));
        var languageOptions = new StackPanel(); languageOptions.Children.Add(Ui.Text("Idioma", 18)); languageOptions.Children.Add(Ui.Text("A mudança de idioma é aplicada sem reiniciar o monitor.", 12, Ui.Muted));
        foreach (string name in new[] { "Português (Brasil)", "English", "Español" }) languageChoice.Items.Add(new ComboBoxItem { Content = name });
        languageChoice.SelectedIndex = Array.IndexOf(Strings.Codes, Strings.Code); AutomationProperties.SetName(languageChoice, "Idioma do aplicativo");
        languageChoice.SelectionChanged += (_, _) => { if (languageChoice.SelectedIndex < 0) return; try { Strings.Set(Strings.Codes[languageChoice.SelectedIndex]); } catch (Exception ex) { languageChoice.SelectedIndex = Array.IndexOf(Strings.Codes, Strings.Code); ShowError(ex); } }; languageOptions.Children.Add(languageChoice); options.Children.Add(OptionGroup(languageOptions));
        var syncOptions = new StackPanel(); syncOptions.Children.Add(Ui.Text("Sincronização", 18)); syncOptions.Children.Add(startup); syncOptions.Children.Add(layouts);
        syncOptions.Children.Add(Ui.Text("Layouts = abas, painéis e barras laterais (workspace*.json). Notas, temas, plugins e demais configurações continuam incluídos. Pare o monitor para mudar esta opção e inicie novamente.", 11, Ui.Muted));
        syncOptions.Children.Add(Ui.Button("Revisar referências Obsidian / Codex", (_, _) => ReviewIntegration())); options.Children.Add(OptionGroup(syncOptions));
        options.Children.Add(OptionGroup(BuildUpdatePanel(evaluationMode)));
        tabs.Items.Add(Page("⚙ Configurações", new ScrollViewer { Content = options, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }));
        local.Text = evaluationMode ? "" : testMode ? @"C:\Users\demo\Documents\MegaBrain" : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MegaBrain");
        cloud.Text = evaluationMode ? "" : testMode ? @"C:\Users\demo\iCloudDrive\iCloud~md~obsidian\MegaBrain" : FindCloud();
        trayIcon = Brand.TrayIcon(trayState); tray = new Forms.NotifyIcon { Text = "Vault Sync · PARADO", Icon = trayIcon, Visible = !testMode };
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add("Abrir", null, (_, _) => Dispatcher.BeginInvoke(Reveal));
        menu.Items.Add("Iniciar", null, (_, _) => Dispatcher.BeginInvoke(async () => await RunJob(StartRequested)));
        menu.Items.Add("Parar", null, (_, _) => Dispatcher.BeginInvoke(async () => await RunJob(StopMonitoring)));
        menu.Items.Add("Conferir agora", null, (_, _) => worker?.RequestScan()); menu.Items.Add("Sair", null, (_, _) => Dispatcher.BeginInvoke(async () => await Exit())); tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(Reveal);
        tray.BalloonTipClicked += (_, _) => Dispatcher.BeginInvoke(() => { Reveal(); ShowUpdateOffer(); });
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
        Closing += (_, e) => { if (!closed) { e.Cancel = true; _ = Exit(); } };
        if (!testMode) Loaded += (_, _) => LoadSavedConfiguration();
        SystemEvents.PowerModeChanged += PowerChanged;
        Theme.Changed += ThemeChanged; Strings.Changed += LanguageChanged; ThemeChanged();
        foreach (var entry in log.Snapshot()) AddLog(entry);
        UpdateButtons(); LanguageChanged();
    }
    private static TabItem Page(string name, UIElement content) { var tab = new TabItem { Header = new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap }, Content = content }; AutomationProperties.SetName(tab, name); return tab; }
    private static Border OptionGroup(UIElement content) => new() { Tag = "SettingsGroup", Child = content, Padding = new Thickness(0, 0, 0, 22), Margin = new Thickness(0, 0, 0, 22), BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Theme.Get("Border") };
    private void OpenGitHub() { try { Process.Start(new ProcessStartInfo(Program.GitHubUrl) { UseShellExecute = true }); } catch (Exception ex) { ShowError(ex); } }
    internal void Reveal()
    {
        if (closed) return;
        Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        RestoreTray(); Activate(); SingleInstance.RestoreWindow(new WindowInteropHelper(this).Handle);
    }
    private IntPtr WindowMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message == SingleInstance.TaskbarCreated) Dispatcher.BeginInvoke(RestoreTray);
        return IntPtr.Zero;
    }
    private void RestoreTray() { if (closed || testMode) return; tray.Visible = false; RefreshTray(); tray.Visible = true; }
    private void SetState(string symbol, string color, string iconState)
    {
        stateSymbol.Text = symbol; stateSymbol.Foreground = Theme.Get(color);
        if (trayState != iconState) { trayState = iconState; RefreshTray(); }
    }
    private void RefreshTray()
    {
        var old = trayIcon; trayIcon = Brand.TrayIcon(trayState); tray.Icon = trayIcon; old?.Dispose();
    }
    private void ThemeChanged()
    {
        RefreshTray();
        if (tray.ContextMenuStrip is { } menu)
        {
            var bg = Theme.Get("Surface").Color; var fg = Theme.Get("Ink").Color;
            menu.BackColor = System.Drawing.Color.FromArgb(bg.R, bg.G, bg.B); menu.ForeColor = System.Drawing.Color.FromArgb(fg.R, fg.G, fg.B);
            menu.RenderMode = Forms.ToolStripRenderMode.System;
        }
        if (themeChoice.SelectedIndex != (int)Theme.Mode) themeChoice.SelectedIndex = (int)Theme.Mode;
    }
    private void LanguageChanged()
    {
        Strings.Apply(this); logGrid.Items.Refresh(); preview.Items.Refresh(); report.Text = Strings.T(reportSource); SetDetail(detailMessage, detailPath);
        UpdateTrayText();
        if (languageChoice.SelectedIndex != Array.IndexOf(Strings.Codes, Strings.Code)) languageChoice.SelectedIndex = Array.IndexOf(Strings.Codes, Strings.Code);
        if (tray.ContextMenuStrip is { } menu) { string[] labels = ["Abrir", "Iniciar", "Parar", "Conferir agora", "Sair"]; for (int i = 0; i < labels.Length; i++) menu.Items[i].Text = Strings.T(labels[i]); }
    }
    private void UpdateTrayText(string? source = null)
    {
        if (source != null) trayTextSource = source;
        string value = Strings.T(trayTextSource); tray.Text = value.Length > 63 ? value[..63] : value;
    }
    private static System.Windows.Controls.Grid EmptyHost(UIElement content, UIElement empty)
    {
        var grid = new System.Windows.Controls.Grid { MinHeight = 150 };
        BindingOperations.SetBinding(content, UIElement.VisibilityProperty, new Binding("Visibility") { Source = empty, Converter = new InverseVisibility() });
        var scroll = new ScrollViewer { Content = empty, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroll.SetBinding(UIElement.VisibilityProperty, new Binding("Visibility") { Source = empty }); grid.Children.Add(content); grid.Children.Add(scroll); return grid;
    }
    private sealed class InverseVisibility : IValueConverter
    {
        public object Convert(object value, Type target, object parameter, CultureInfo culture) => value is Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type target, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
    private static DataGrid Grid() => new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, EnableRowVirtualization = true, HeadersVisibility = DataGridHeadersVisibility.Column };
    private static DataGridTextColumn Column(string header, string binding, bool stretch = false) => new() { Header = header, Binding = new System.Windows.Data.Binding(binding), Width = stretch ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto };
    private static Button WindowButton(string text, string tip, RoutedEventHandler action) { var button = Ui.Button(text, action); button.ToolTip = tip; button.Width = 31; button.Padding = new Thickness(2); button.Background = Ui.Surface; return button; }
    private UIElement FolderRow(TextBox input)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var browse = Ui.Button("Escolher pasta", (_, _) => { if (worker != null || busy) return; var dialog = new OpenFolderDialog { Title = Strings.T("Escolha a pasta") }; if (Directory.Exists(input.Text)) dialog.InitialDirectory = input.Text; if (dialog.ShowDialog(this) == true) input.Text = dialog.FolderName; }); browse.ToolTip = "Escolher pasta";
        var create = Ui.Button("Criar", (_, _) => { if (worker != null || busy) return; try { RequirePath(input.Text); Paths.CheckAncestors(input.Text); Directory.CreateDirectory(input.Text); log.Write("INFO", "Pasta criada: " + input.Text); } catch (Exception ex) { ShowError(ex); } });
        input.MinWidth = 0; input.VerticalContentAlignment = VerticalAlignment.Center; input.Margin = new Thickness(0, 3, 0, 5); input.ToolTip = input.Text;
        AutomationProperties.SetName(input, input == local ? "Caminho da pasta local" : "Caminho da pasta iCloud"); input.TextChanged += (_, _) => input.ToolTip = input.Text;
        browse.Margin = new Thickness(0, 3, 8, 3); create.Margin = new Thickness(0, 3, 0, 3);
        var controls = Ui.Row(browse, create); System.Windows.Controls.Grid.SetRow(controls, 1); row.Children.Add(input); row.Children.Add(controls);
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
            if (resumeState == VaultSync.Updates.ResumeState.Stopped) { resumeState = null; log.Write("INFO", "Configuração carregada. Monitor PARADO até clicar em Iniciar sincronização."); }
            else if (config.CanAutoStart) _ = RunJob(async () => { BeginWorker(new(config, dataRoot), config); await Task.CompletedTask; });
            else if (config.MonitorOnLaunch) { SetDetail("Nova regra: vence a data de modificação mais recente. Revise a prévia e clique em Iniciar sincronização para ativá-la."); log.Write("AVISO", detailMessage); }
            else log.Write("INFO", "Configuração carregada. Monitor PARADO até clicar em Iniciar sincronização.");
        }
        catch (Exception ex) { ShowError(ex); Show(); }
    }
    private SyncConfig ReadConfig()
    {
        if (string.IsNullOrWhiteSpace(local.Text) || string.IsNullOrWhiteSpace(cloud.Text)) throw new IOException("Selecione as duas pastas antes de verificar ou iniciar a sincronização.");
        return new(Paths.Normalize(local.Text), Paths.Normalize(cloud.Text), startup.IsChecked == true, false, layouts.IsChecked == true);
    }
    private static void RequirePath(string path) { if (string.IsNullOrWhiteSpace(path)) throw new IOException("Escolha uma pasta antes de criar ou abrir."); }
    private async Task RunJob(Func<Task> action)
    {
        if (updateInstalling) return;
        if (closed) return; if (busy) { comparisonStatus.Text = "Uma operação está em andamento. Aguarde a conclusão indicada no log."; return; } busy = true; operation = new(); UpdateButtons();
        try { await action(); }
        catch (OperationCanceledException) { log.Write("INFO", "Operação cancelada."); }
        catch (Exception ex) { ShowError(ex); }
        finally { busy = false; operation.Dispose(); operation = null; UpdateButtons(); Strings.Apply(this); if (exiting) await Exit(); }
    }
    private async Task PrepareCore()
    {
        if (worker != null) { worker.RequestScan(); return; }
        stateLabel.Text = "VERIFICANDO"; SetState("↻", "Accent", "active"); previewEmpty.Visibility = Visibility.Collapsed; SetDetail("Lendo arquivos para mostrar a prévia…"); log.Write("INFO", "Verificação das pastas iniciada.");
        prepared?.Dispose(); prepared = null; plan = null;
        var config = ReadConfig(); var validation = await Task.Run(() => Scanner.Validate(config, dataRoot, true));
        SetReport(string.Join("\n", validation.Errors.Select(x => "BLOQUEIO · " + x).Concat(validation.Warnings.Select(x => "AVISO · " + x))));
        if (!validation.CanContinue) throw new IOException(string.Join(" | ", validation.Errors));
        AppFiles.SaveConfig(config, dataRoot); prepared = new(config, dataRoot); prepared.Activity += log.Write;
        plan = await Task.Run(() => { prepared.RefreshConflictRecords(); return prepared.Preview(operation?.Token ?? default); });
        preview.ItemsSource = plan.Items.Where(p => p.Action != SyncAction.Equal).Select(p => new PreviewRow(p.Path, Label(p.Action))).ToList();
        previewEmpty.Visibility = preview.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (previewEmpty is StackPanel empty) { ((TextBlock)empty.Children[0]).Text = plan.Errors.Count == 0 ? "Nenhuma diferença na conferência" : "Conferência incompleta"; ((TextBlock)empty.Children[1]).Text = plan.Errors.Count == 0 ? "Os conteúdos lidos são iguais nas duas pastas." : "Há leituras adiadas. Consulte os avisos; isso não significa que tudo está sincronizado."; }
        SetReport("\n" + string.Join("\n", plan.Errors), true);
        previewSummary.Text = $"Prévia: {plan.Items.Count(p => p.Action is SyncAction.Upload or SyncAction.Download)} cópias · {plan.Items.Count(p => p.Action == SyncAction.Conflict)} conflitos · {plan.Errors.Count} leituras adiadas.";
        SetConflicts(PreviewConflicts()); counts.Text = $"{conflicts.Items.Count} conflitos na prévia · {plan.Errors.Count} leituras adiadas";
        stateLabel.Text = "PARADO · PRÉVIA PRONTA"; SetDetail("Clique em Iniciar sincronização para copiar e monitorar automaticamente.");
        SetState(plan.Errors.Count == 0 ? "■" : "!", plan.Errors.Count == 0 ? "Muted" : "Warning", "stopped");
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
    }
    private void BeginWorker(SyncEngine engine, SyncConfig config)
    {
        activeConfig = config; worker = new(engine, log); worker.Status += s => { if (!closed) Dispatcher.BeginInvoke(() => Update(s)); };
        if (resumeState == VaultSync.Updates.ResumeState.Paused) worker.Pause(true);
        resumeState = null;
        worker.Start();
        if (worker.Paused) { stateLabel.Text = "PAUSADO"; SetState("Ⅱ", "Warning", "paused"); SetDetail("O monitor está pausado. Nenhuma alteração será copiada."); }
        else { stateLabel.Text = "ATIVO · INICIANDO"; SetState("↻", "Accent", "active"); SetDetail("Primeira conferência em andamento. O monitor continuará ativo."); }
        UpdateButtons();
    }
    private async Task StopMonitoring()
    {
        var current = worker; worker = null;
        if (current != null) await Task.Run(current.Dispose);
        if (activeConfig != null) { activeConfig = activeConfig with { MonitorOnLaunch = false }; AppFiles.SaveConfig(activeConfig, dataRoot); }
        stateLabel.Text = "PARADO"; SetState("■", "Muted", "stopped"); SetDetail("Nenhum arquivo será copiado. Clique em Iniciar sincronização para retomar."); heartbeat.Text = "Monitor desativado"; UpdateTrayText("Vault Sync · PARADO");
    }
    private void Update(WorkerStatus s)
    {
        if (worker == null || closed) return;
        stateLabel.Text = s.Paused ? "PAUSADO" : "ATIVO · " + s.State.ToUpperInvariant(); SetDetail(s.Message);
        bool error = s.WatchingRoots < 2 || s.State == "Aguardando acesso" || s.State.Contains("erro", StringComparison.OrdinalIgnoreCase) || s.State.Contains("indispon", StringComparison.OrdinalIgnoreCase);
        bool attention = s.Conflicts.Count > 0 || s.Pending.Count > 0;
        SetState(s.Paused ? "Ⅱ" : error ? "!" : attention ? "!" : s.State == "Conferindo" ? "↻" : "✓", s.Paused ? "Warning" : error ? "Danger" : attention ? "Warning" : s.State == "Conferindo" ? "Accent" : "Success", s.Paused ? "paused" : error ? "error" : attention ? "paused" : "active");
        counts.Text = $"{s.Conflicts.Count} conflitos · {s.Pending.Count} pendências";
        heartbeat.Text = $"{s.WatchingRoots}/2 pastas observadas · última conferência {(s.LastCheck?.LocalDateTime.ToString("HH:mm:ss") ?? "em andamento")}";
        SetConflicts(s.Conflicts);
        string trayText = "Vault Sync · " + (s.Paused ? "PAUSADO" : error ? "ERRO" : attention ? "ATENÇÃO" : "ATIVO") + $" · {s.Conflicts.Count} conflitos · {s.Pending.Count} pend.";
        UpdateTrayText(trayText);
        pause.Content = s.Paused ? "Retomar" : "Pausar";
        previewEmpty.Visibility = preview.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (previewEmpty is StackPanel empty) { ((TextBlock)empty.Children[0]).Text = error ? "Conferência incompleta" : s.Paused ? "Monitor pausado" : "Prévia anterior ao início"; ((TextBlock)empty.Children[1]).Text = error ? "Uma pasta não está acessível. Consulte o log antes de prosseguir." : "Acompanhe as operações atuais no log. Esta lista mostra a conferência anterior ao início."; }
        Strings.Apply(this);
    }
    private void SetConflicts(List<Conflict> items)
    {
        conflictEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (tabs.Items.Count > 1) ((TextBlock)((TabItem)tabs.Items[1]).Header).Text = items.Count == 0 ? "Conflitos" : $"Conflitos · {items.Count}";
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
        SetReport("\nBackup: " + folder, true); stateLabel.Text = "PARADO"; SetDetail("Backup avulso concluído. Para sincronizar, clique em Iniciar sincronização.");
    }
    private void SetReport(string source, bool append = false) { reportSource = append ? reportSource + source : source; report.Text = Strings.T(reportSource); }
    private void SetDetail(string message, string? path = null) { detailMessage = message; detailPath = path; detail.Text = Strings.T(message) + (path == null ? "" : " · " + path); }
    private void OpenFolder(string path, bool create)
    {
        try
        {
            RequirePath(path); string full = Path.GetFullPath(path); if (create) Directory.CreateDirectory(full);
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
        SetState("!", "Danger", "error");
        UpdateTrayText("Vault Sync · ERRO");
        previewEmpty.Visibility = preview.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (previewEmpty is StackPanel empty) { ((TextBlock)empty.Children[0]).Text = "Verificação necessária"; ((TextBlock)empty.Children[1]).Text = "Consulte o log. A ausência de itens não confirma que as pastas estejam sincronizadas."; }
        log.Write("ERRO", ex.Message); SetDetail(ex.Message);
        if (worker == null) stateLabel.Text = "PARADO · VERIFIQUE O LOG";
        Strings.Apply(this);
    }
    private void OnLog(SyncEvent entry) { if (!closed) Dispatcher.BeginInvoke(() => AddLog(entry)); }
    private void AddLog(SyncEvent entry)
    {
        logEmpty.Visibility = Visibility.Collapsed;
        logRows.Add(new(entry.Time.LocalDateTime.ToString("HH:mm:ss"), entry.Level, entry.Message, entry.Path));
        if (worker != null && entry.Level is "OK" or "PENDENTE" or "CONFLITO") SetDetail(entry.Message, entry.Path);
        while (logRows.Count > 500) logRows.RemoveAt(0); if (logRows.Count > 0) logGrid.ScrollIntoView(logRows[^1]);
        Strings.Apply(this);
    }
    private void UpdateButtons()
    {
        tabs.IsEnabled = !updateInstalling;
        start.Visibility = worker == null ? Visibility.Visible : Visibility.Collapsed;
        pause.Visibility = worker == null ? Visibility.Collapsed : Visibility.Visible;
        monitorActions.Visibility = tabs.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        conflictStop.Visibility = worker == null ? Visibility.Collapsed : Visibility.Visible; conflictStop.IsEnabled = !busy && worker != null;
        start.IsEnabled = !busy && worker == null; stopButton.IsEnabled = !busy && worker != null; pause.IsEnabled = !busy && worker != null; check.IsEnabled = !busy && worker != null;
        compare.IsEnabled = refresh.IsEnabled = !busy; foreach (var button in folderButtons) button.IsEnabled = !busy && worker == null;
        local.IsReadOnly = cloud.IsReadOnly = worker != null || busy; startup.IsEnabled = layouts.IsEnabled = !busy && worker == null;
        if (updateInstalling) { start.IsEnabled = stopButton.IsEnabled = pause.IsEnabled = check.IsEnabled = compare.IsEnabled = refresh.IsEnabled = conflictStop.IsEnabled = false; foreach (var button in folderButtons) button.IsEnabled = false; }
    }
    private static string Label(SyncAction action) => action switch { SyncAction.Upload => "Local → iCloud", SyncAction.Download => "iCloud → Local", SyncAction.DeleteLocal => "Excluir local", SyncAction.DeleteCloud => "Excluir iCloud", SyncAction.Conflict => "Escolher versão", _ => "Igual" };
    private void PowerChanged(object? sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) worker?.RequestScan(); }
    private async Task Exit()
    {
        if (closed) return;
        StopUpdateChecks();
        if (busy) { exiting = true; operation?.Cancel(); SetDetail("Cancelando a operação para fechar…"); return; }
        closed = true; SystemEvents.PowerModeChanged -= PowerChanged; Theme.Changed -= ThemeChanged; Strings.Changed -= LanguageChanged; tray.Visible = false; tray.Dispose(); trayIcon?.Dispose();
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
    internal int SelectedPageSmoke => tabs.SelectedIndex;
    internal bool EmptyPathsSmoke => local.Text.Length == 0 && cloud.Text.Length == 0;
    internal void SetLanguageSmoke(string code) => languageChoice.SelectedIndex = Array.IndexOf(Strings.Codes, code);
    internal bool MonitorActionsVisibleSmoke => monitorActions.Visibility == Visibility.Visible;
    internal string PageTitleSmoke(int index) => ((TextBlock)((TabItem)tabs.Items[index]).Header).Text;
    internal Task EmptyPreviewSmokeAsync() { local.Text = ""; cloud.Text = ""; return RunJob(PrepareCore); }
    internal void AssertEmptyLayoutSmoke()
    {
        ((FrameworkElement)previewEmpty).BringIntoView(); Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); UpdateLayout();
        if (preview.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Tabela vazia continua sob a mensagem.");
        var panel = (StackPanel)previewEmpty;
        if (panel.Children.Cast<TextBlock>().Any(t => t.ActualHeight + t.Margin.Top + t.Margin.Bottom + .1 < t.DesiredSize.Height)) throw new InvalidOperationException("Texto do estado vazio cortado.");
    }
    internal void AssertKeyboardSmoke()
    {
        var tab = (TabItem)tabs.Items[tabs.SelectedIndex];
        if (!tab.Focus() || !tab.IsKeyboardFocusWithin) throw new InvalidOperationException("Navegação não recebeu foco de teclado.");
        if (string.IsNullOrWhiteSpace(AutomationProperties.GetName(tab))) throw new InvalidOperationException("Área sem nome acessível.");
    }
    internal void SetThemeSmoke(ThemeMode mode) => themeChoice.SelectedIndex = (int)mode;
    internal void StatusPresentationSmoke(WorkerStatus status) => Update(status);
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
            button.BringIntoView(); Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); UpdateLayout();
            var point = button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2), this);
            DependencyObject? hit = InputHitTest(point) as DependencyObject;
            while (hit != null && hit != button) hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
            if (hit != button) throw new InvalidOperationException("O campo de pasta cobre um botão.");
        }
        foreach (var input in new[] { local, cloud })
        {
            var button = folderButtons[input == local ? 0 : 2];
            if (input.TranslatePoint(new Point(0, input.ActualHeight), this).Y > button.TranslatePoint(new Point(), this).Y + 1) throw new InvalidOperationException("Campo e botão de pasta se sobrepõem.");
        }
    }
    private sealed record LogRow(string Time, string SourceLevel, string Message, string? Path)
    {
        public string Level => Strings.T(SourceLevel);
        public string Text => Strings.T(Message) + (Path == null ? "" : " · " + Path);
    }
    private sealed record PreviewRow(string Path, string SourceAction) { public string Action => Strings.T(SourceAction); }
}
