using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VaultSync.Core;
using VaultSync.Updates;

namespace VaultSync.App;

internal static class UiSmoke
{
    public static void Run(string? output)
    {
        output ??= Path.Combine(Path.GetTempPath(), "vaultsync-ui-smoke"); Directory.CreateDirectory(output);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Theme.Initialize(Path.Combine(output, "ui-preferences")); Theme.Set(ThemeMode.Dark, false);
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        var setup = new SetupWindow(); Capture(setup, Path.Combine(output, "installer.png")); setup.Close();
        var main = new MainWindow(testMode: true); Capture(main, Path.Combine(output, "main.png"));
        foreach (var command in Descendants(main).OfType<System.Windows.Controls.Button>().Where(b => Equals(b.Tag, "WindowCommand")))
        {
            if (command.FocusVisualStyle == null || command.Template.Triggers.OfType<Trigger>().Any(t => t.Property == UIElement.IsKeyboardFocusedProperty)) throw new InvalidOperationException("Contorno persistente por foco do mouse nos controles da janela.");
        }
        main.CloseSmoke();
        string fixtures = Path.Combine(output, "fixtures"); Directory.CreateDirectory(Path.Combine(fixtures, "local")); Directory.CreateDirectory(Path.Combine(fixtures, "cloud"));
        string l = Path.Combine(fixtures, "local", "Exemplo.md"), c = Path.Combine(fixtures, "cloud", "Exemplo.md");
        File.WriteAllText(l, "# Projeto\n\nPrazo: sexta-feira\n\n- Revisar orçamento\n"); File.WriteAllText(c, "# Projeto\n\nPrazo: segunda-feira\n\n- Revisar orçamento\n- Consultar equipe\n");
        string common = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"Registro {i}: conteúdo compartilhado para conferir rolagem.")); File.AppendAllText(l, common); File.AppendAllText(c, common);
        var conflict = new Conflict("Exemplo.md", Scanner.Read(l), Scanner.Read(c), fixtures, "Edições offline nos dois computadores");
        using var comparisonEngine = new SyncEngine(new(Path.GetDirectoryName(l)!, Path.GetDirectoryName(c)!), Path.Combine(output, "comparison-state"));
        var dialog = new ConflictWindow(comparisonEngine.PrepareComparison(conflict)!, "Windows de trabalho");
        Capture(dialog, Path.Combine(output, "conflict.png")); AssertScrollbars(dialog); dialog.Close();
        var compactDialog = new ConflictWindow(comparisonEngine.PrepareComparison(conflict)!); compactDialog.Show(); Ui.FitWindow(compactDialog, new Rect(0, 0, 800, 560));
        if (compactDialog.Top < 0 || compactDialog.Height + compactDialog.Top > 560 || compactDialog.Width + compactDialog.Left > 800) throw new InvalidOperationException("Comparador fora da área útil.");
        Capture(compactDialog, Path.Combine(output, "conflict-compact.png")); compactDialog.Close();
        var confirmation = new ThemedDialog("Aplicar esta escolha às duas pastas? O conteúdo será revalidado antes da alteração.", true); Capture(confirmation, Path.Combine(output, "confirmation.png")); confirmation.Close();
        ThemeAndScreens(output, comparisonEngine.PrepareComparison(conflict)!);
        LocalizationAndLayout(output, comparisonEngine.PrepareComparison(conflict)!);
        UpdateScreens(output);
        RunWithDispatcher(Activation(output));
        RunWithDispatcher(Workflow(output));
        if (ConflictWindow.MarkdownPreview("# Teste\n<script>alert(1)</script>\n![imagem](https://invalid.example/a)").Blocks.Count != 3) throw new InvalidOperationException("Preview inesperado.");
        using var process = System.Diagnostics.Process.GetCurrentProcess(); process.Refresh();
        File.WriteAllText(Path.Combine(output, "result.json"), System.Text.Json.JsonSerializer.Serialize(new { status = "passed", screens = Directory.GetFiles(output, "*.png", SearchOption.AllDirectories).Length, workflow = "start, retained-navigation, preview-conflicts, refresh-and-open-comparison, choose-local-with-confirmation, two-way-copy, newest-both-directions, pause, resume, stop, restart, persistent-log, folder-hit-testing, compact-work-area, themed-confirmation, themed-options, policy-upgrade-stays-stopped", themes = "system/light/dark, persistence, live shared brushes, simulated system change", languages = "pt-BR/en/es; five pages in both themes; persistence; switching while monitoring; separate backup/settings; contextual actions; empty validation at three scales; keyboard-only focus style", dpi = "96/144/192 render DPI and corresponding logical work areas; physical monitor switching requires manual verification", networkRenderer = false, workingSetMiB = process.WorkingSet64 / 1048576.0 }));
        Theme.Shutdown(); app.Shutdown();
    }
    private static async Task Activation(string output)
    {
        using var instance = new SingleInstance("Local\\VaultSync-Test-" + Guid.NewGuid().ToString("N"));
        if (!instance.IsOwner) throw new InvalidOperationException("Instância de teste não adquiriu o mutex isolado.");
        var window = new MainWindow(true, Path.Combine(output, "activation-state"), true); window.Show();
        instance.Listen(() => window.Dispatcher.BeginInvoke(window.Reveal));
        try
        {
            window.Hide(); await Send(); await Until(() => window.IsVisible);
            window.WindowState = WindowState.Minimized; await Send(); await Until(() => window.WindowState == WindowState.Normal);
            Capture(window, Path.Combine(output, "reopened-instance.png"));
        }
        finally { window.CloseSmoke(); }
        async Task Send()
        {
            var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "VaultSync.dll"));
            start.ArgumentList.Add("--self-test-activate"); start.ArgumentList.Add(instanceName());
            using var sender = System.Diagnostics.Process.Start(start) ?? throw new IOException("Não foi possível iniciar o teste de segunda abertura.");
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await sender.WaitForExitAsync(limit.Token);
            if (sender.ExitCode != 0) throw new InvalidOperationException("Segunda abertura não encontrou a instância existente.");
        }
        string instanceName() => instance.Name;
    }
    private static void UpdateScreens(string output)
    {
        string dir = Path.Combine(output, "updates"); Directory.CreateDirectory(dir);
        var release = new UpdateRelease("1.3.0", "# Novidades\n\nAtualização de exemplo.\n<script>não executado</script>\n![imagem](https://invalid.example/not-loaded.png)", GitHubUpdates.ReleasePage + "/tag/v1.3.0",
            new("VaultSync-Obsidian-1.3.0-win-x64-Setup.exe", "", 164000000, null), new("SHA256SUMS.txt", "", 109, null));
        foreach (string language in Strings.Codes)
        foreach (var theme in new[] { ThemeMode.Dark, ThemeMode.Light })
        {
            Strings.Set(language, false); Theme.Set(theme, false);
            var window = new MainWindow(true, Path.Combine(dir, language + theme)); window.Show(); window.SelectTabSmoke(4); window.UpdateAvailableSmoke(release);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var heading = Descendants(window).OfType<System.Windows.Controls.TextBlock>().First(t => t.Text == Strings.T("Atualizações"));
            ((FrameworkElement)heading.Parent).BringIntoView();
            Capture(window, Path.Combine(dir, $"{language}-{theme}-settings.png"));
            if (!Descendants(window).OfType<System.Windows.Controls.TextBlock>().Any(t => t.Text == Strings.T("Nova versão disponível: 1.3.0"))) throw new InvalidOperationException("Aviso de atualização não traduzido.");
            var dialog = new UpdateWindow(release, true); Capture(dialog, Path.Combine(dir, $"{language}-{theme}-offer.png"));
            if (!Descendants(dialog).OfType<System.Windows.Controls.Button>().Any(b => Equals(b.Content, Strings.T("Atualizar agora")))) throw new InvalidOperationException("Ação de atualização não traduzida.");
            dialog.Close(); window.CloseSmoke();
        }
        Strings.Set("pt-BR", false); Theme.Set(ThemeMode.Dark, false);
    }
    private static void ThemeAndScreens(string output, ComparisonSnapshot snapshot)
    {
        var savedSystem = Theme.SystemDark;
        Theme.SystemDark = () => true; Theme.Set(ThemeMode.System); if (!Theme.IsDark) throw new InvalidOperationException("Tema do sistema não aplicado.");
        Theme.SystemDark = () => false; Theme.RefreshSystem(); if (Theme.IsDark) throw new InvalidOperationException("Mudança do tema do sistema não aplicada.");
        var shared = Theme.Get("Ink"); Theme.Set(ThemeMode.Dark); var previous = shared.Color;
        Theme.Set(ThemeMode.Light); if (!ReferenceEquals(shared, Theme.Get("Ink")) || previous == shared.Color) throw new InvalidOperationException("Janelas abertas não compartilham as cores.");
        Theme.Initialize(Path.Combine(output, "ui-preferences")); if (Theme.Mode != ThemeMode.Light) throw new InvalidOperationException("Tema não persistido.");
        File.WriteAllText(Path.Combine(output, "ui-preferences", "ui-settings.json"), "{invalid"); Theme.Initialize(Path.Combine(output, "ui-preferences")); if (Theme.Mode != ThemeMode.System) throw new InvalidOperationException("Preferência inválida bloqueou o padrão.");
        Theme.SystemDark = savedSystem;
        foreach (var mode in new[] { ThemeMode.Dark, ThemeMode.Light })
        {
            Theme.Set(mode, false); string dir = Path.Combine(output, mode.ToString().ToLowerInvariant()); Directory.CreateDirectory(dir);
            var main = new MainWindow(true, Path.Combine(dir, "state")); main.Show();
            for (int i = 0; i < 5; i++) { main.SelectTabSmoke(i); Capture(main, Path.Combine(dir, $"page-{i}.png")); main.AssertKeyboardSmoke(); }
            main.SelectTabSmoke(0);
            main.SetThemeSmoke(mode == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark); if (Theme.IsDark == (mode == ThemeMode.Dark)) throw new InvalidOperationException("Seletor de tema não aplicou a troca."); main.SetThemeSmoke(mode);
            foreach (double scale in new[] { 1.0, 1.5, 2.0 })
            {
                main.Width = 1100; main.Height = 780; Ui.FitWindow(main, new Rect(0, 0, 1366 / scale, 900 / scale));
                main.UpdateLayout(); foreach (var scroll in Descendants(main).OfType<System.Windows.Controls.ScrollViewer>()) scroll.ScrollToTop(); main.UpdateLayout();
                Capture(main, Path.Combine(dir, $"layout-{scale * 100:0}.png"), scale); main.AssertFolderLayoutSmoke();
            }
            main.CloseSmoke();
            var setup = new SetupWindow(); Capture(setup, Path.Combine(dir, "installer.png")); setup.Close();
            var comparison = new ConflictWindow(snapshot, "PC de exemplo"); Capture(comparison, Path.Combine(dir, "comparison.png")); AssertScrollbars(comparison); comparison.Close();
            var integration = new IntegrationWindow(new(@"C:\demo\local", @"C:\demo\cloud"), true); Capture(integration, Path.Combine(dir, "integration.png")); integration.Close();
            var confirmation = new ThemedDialog("Confirme a escolha após comparar as versões.", true); Capture(confirmation, Path.Combine(dir, "confirmation.png")); confirmation.Close();
        }
        Theme.Set(ThemeMode.Dark, false);
        var evaluation = new MainWindow(true, Path.Combine(output, "evaluation-state"), true);
        if (!evaluation.EmptyPathsSmoke || evaluation.IsMonitoringSmoke) throw new InvalidOperationException("Avaliação importou pastas ou iniciou o monitor."); evaluation.CloseSmoke();
    }
    private static void LocalizationAndLayout(string output, ComparisonSnapshot snapshot)
    {
        string dir = Path.Combine(output, "languages"); Directory.CreateDirectory(dir);
        var main = new MainWindow(true, Path.Combine(output, "language-state")); main.Show();
        foreach (string language in Strings.Codes)
        {
            main.SetLanguageSmoke(language);
            if (language == "en" && Strings.T("Pasta criada: Conflitos") != "Folder created: Conflitos") throw new InvalidOperationException("Tradução modificou o nome de uma pasta.");
            if (Strings.Code != language) throw new InvalidOperationException("Idioma não aplicado pelo seletor.");
            if (main.PageTitleSmoke(4) != Strings.T("⚙ Configurações") || main.PageTitleSmoke(3) != Strings.T("Backups")) throw new InvalidOperationException("Navegação não traduzida.");
            foreach (var theme in new[] { ThemeMode.Dark, ThemeMode.Light })
            {
                Theme.Set(theme);
                for (int page = 0; page < 5; page++)
                {
                    main.SelectTabSmoke(page);
                    if (main.MonitorActionsVisibleSmoke != (page == 0)) throw new InvalidOperationException("Comandos globais aparecem fora de Pastas.");
                    Capture(main, Path.Combine(dir, $"{language}-{theme}-page-{page}.png"));
                    var credit = Descendants(main).OfType<System.Windows.Controls.TextBlock>().Single(t => t.Text == Strings.T("Desenvolvido por Natan Peixoto Maciel"));
                    if (!credit.IsVisible || credit.TranslatePoint(new Point(), main).Y >= main.ActualHeight) throw new InvalidOperationException("Crédito não está permanentemente visível.");
                    if (page == 4 && Descendants(main).OfType<System.Windows.Controls.Border>().Where(b => Equals(b.Tag, "SettingsGroup")).Any(b => b.Padding.Bottom < 20 || b.Margin.Bottom < 20)) throw new InvalidOperationException("Espaçamento das configurações insuficiente.");
                }
            }
            var comparison = new ConflictWindow(snapshot, "PC de exemplo"); Capture(comparison, Path.Combine(dir, $"{language}-comparison.png")); comparison.Close();
            var setup = new SetupWindow(); Capture(setup, Path.Combine(dir, $"{language}-installer.png")); setup.Close();
            Theme.Initialize(Path.Combine(output, "ui-preferences")); if (Strings.Code != language) throw new InvalidOperationException("Idioma não foi preservado com o tema.");
        }
        main.SetLanguageSmoke("pt-BR"); main.SelectTabSmoke(0); RunWithDispatcher(main.EmptyPreviewSmokeAsync());
        foreach (double scale in new[] { 1.0, 1.5, 2.0 })
        {
            main.Width = 1100; main.Height = 780; Ui.FitWindow(main, new Rect(0, 0, 1366 / scale, 900 / scale));
            main.AssertEmptyLayoutSmoke(); Capture(main, Path.Combine(dir, $"empty-validation-{scale * 100:0}.png"), scale);
        }
        main.CloseSmoke(); Theme.Set(ThemeMode.Dark, false);
    }
    private static async Task Workflow(string output)
    {
        string root = Path.Combine(output, "workflow-" + Guid.NewGuid().ToString("N")), local = Path.Combine(root, "local"), cloud = Path.Combine(root, "cloud"), state = Path.Combine(root, "state");
        Directory.CreateDirectory(local); Directory.CreateDirectory(cloud); Directory.CreateDirectory(Path.Combine(cloud, ".obsidian"));
        File.WriteAllText(Path.Combine(local, "conflito.md"), "local"); File.WriteAllText(Path.Combine(cloud, "conflito.md"), "cloud");
        var tied = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc); File.SetLastWriteTimeUtc(Path.Combine(local, "conflito.md"), tied); File.SetLastWriteTimeUtc(Path.Combine(cloud, "conflito.md"), tied);
        File.WriteAllText(Path.Combine(cloud, "inicial.md"), "inicial"); File.WriteAllText(Path.Combine(cloud, ".obsidian", "workspace.json"), "layout em gravação");
        var window = new MainWindow(testMode: true, dataRoot: state); window.Show();
        try
        {
            using (var held = new FileStream(Path.Combine(cloud, ".obsidian", "workspace.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                window.SelectTabSmoke(0); await window.StartSmokeAsync(new(local, cloud));
                if (window.SelectedPageSmoke != 0) throw new InvalidOperationException("Iniciar deslocou a navegação.");
                await Until(() => File.Exists(Path.Combine(local, "inicial.md")));
                var status = await window.StatusSmokeAsync(); if (!status.Running || status.Conflicts.Count != 1) throw new InvalidOperationException("Monitor ou lista de conflitos não refletiu o início real.");
                foreach (string language in Strings.Codes) { window.SetLanguageSmoke(language); if (!window.IsMonitoringSmoke || window.SelectedPageSmoke != 0) throw new InvalidOperationException("Idioma interrompeu o monitor ou mudou a página."); }
                window.SetLanguageSmoke("pt-BR");
                int index = 0;
                foreach (var example in new[] {
                    new WorkerStatus("Aguardando alterações nas duas pastas.", [], [], WatchingRoots: 2, LastCheck: DateTimeOffset.Now),
                    new WorkerStatus("Monitor pausado.", [], [], Paused: true, WatchingRoots: 2),
                    new WorkerStatus("Conferência em andamento.", [], [], WatchingRoots: 2, State: "Conferindo"),
                    new WorkerStatus("Arquivo ocupado; nova tentativa pendente.", [], ["exemplo.md"], WatchingRoots: 2),
                    status,
                    new WorkerStatus("Pasta indisponível. A leitura não confirma exclusão.", [], [], WatchingRoots: 1, State: "Erro") })
                { foreach (var theme in new[] { ThemeMode.Dark, ThemeMode.Light }) { Theme.Set(theme, false); window.StatusPresentationSmoke(example); Capture(window, Path.Combine(output, $"state-{index}-{theme}.png")); } index++; }
                Theme.Set(ThemeMode.Dark, false);
                window.StatusPresentationSmoke(status);
                window.DemoPathsSmoke(); window.SelectTabSmoke(0); Capture(window, Path.Combine(output, "monitoring.png"));
                window.SelectTabSmoke(1); Capture(window, Path.Combine(output, "conflicts-list.png"));
                window.PauseSmoke(true); _ = await window.StatusSmokeAsync();
                File.WriteAllText(Path.Combine(cloud, "conflito.md"), "versão atualizada enquanto a lista estava aberta");
                bool opened = false; window.ComparisonOpenedSmoke = w => { opened = true; Capture(w, Path.Combine(output, "refreshed-comparison.png")); w.Close(); };
                await window.OpenComparisonSmokeAsync(); if (!opened) throw new InvalidOperationException("Comparar não abriu a versão atualizada: " + window.ComparisonStatusSmoke);
                window.ComparisonOpenedSmoke = null;
                File.WriteAllText(Path.Combine(local, "conflito.md"), File.ReadAllText(Path.Combine(cloud, "conflito.md")));
                await window.OpenComparisonSmokeAsync();
                if (!(await window.StatusSmokeAsync()).Conflicts.Any() && !window.ComparisonStatusSmoke.StartsWith("Sincronizado:", StringComparison.Ordinal)) throw new InvalidOperationException("Igualdade não foi apresentada como sincronizada.");
                if ((await window.StatusSmokeAsync()).Conflicts.Count != 0) throw new InvalidOperationException("Conteúdo idêntico continuou na lista de conflitos.");
                window.PauseSmoke(false); await Until(() => File.ReadAllText(Path.Combine(local, "conflito.md")) == File.ReadAllText(Path.Combine(cloud, "conflito.md")));
                File.WriteAllText(Path.Combine(cloud, "evento-icloud.md"), "recebido"); await Until(() => File.Exists(Path.Combine(local, "evento-icloud.md")));
                _ = await window.StatusSmokeAsync();
                File.WriteAllText(Path.Combine(local, "evento-local.md"), "enviado"); await Until(() => File.Exists(Path.Combine(cloud, "evento-local.md")));
                _ = await window.StatusSmokeAsync();
                File.WriteAllText(Path.Combine(cloud, "evento-local.md"), "mais recente no iCloud"); await Until(() => File.ReadAllText(Path.Combine(local, "evento-local.md")) == "mais recente no iCloud"); _ = await window.StatusSmokeAsync();
                File.WriteAllText(Path.Combine(local, "evento-local.md"), "mais recente local"); await Until(() => File.ReadAllText(Path.Combine(cloud, "evento-local.md")) == "mais recente local"); _ = await window.StatusSmokeAsync();
                window.PauseSmoke(true); await Task.Delay(1200); File.WriteAllText(Path.Combine(cloud, "pausado.md"), "esperar"); await Task.Delay(1500);
                if (File.Exists(Path.Combine(local, "pausado.md"))) throw new InvalidOperationException("Monitor copiou durante pausa.");
                window.PauseSmoke(false); await Until(() => File.Exists(Path.Combine(local, "pausado.md"))); _ = await window.StatusSmokeAsync();
                window.SelectTabSmoke(2); Capture(window, Path.Combine(output, "execution-log.png"));
            }
            File.WriteAllText(Path.Combine(local, "escolha.md"), "manter versão local"); File.WriteAllText(Path.Combine(cloud, "escolha.md"), "outra versão"); File.SetLastWriteTimeUtc(Path.Combine(local, "escolha.md"), tied); File.SetLastWriteTimeUtc(Path.Combine(cloud, "escolha.md"), tied);
            var resolutionWait = System.Diagnostics.Stopwatch.StartNew();
            while (!(await window.StatusSmokeAsync()).Conflicts.Any(c => c.Path == "escolha.md"))
            { if (resolutionWait.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Conflito da fixture não apareceu."); await Task.Delay(100); }
            window.SelectTabSmoke(1); window.StatusPresentationSmoke(await window.StatusSmokeAsync());
            ThemedDialog.ConfirmationSmoke = d => d.Dispatcher.BeginInvoke(() => d.DialogResult = true);
            window.ComparisonOpenedSmoke = w => w.Dispatcher.BeginInvoke(() => ((ConflictWindow)w).ChooseSmoke("local"));
            try { await window.OpenComparisonSmokeAsync(); } finally { ThemedDialog.ConfirmationSmoke = null; window.ComparisonOpenedSmoke = null; }
            await Until(() => File.ReadAllText(Path.Combine(cloud, "escolha.md")) == "manter versão local");
            await window.StopSmokeAsync(); if (window.IsMonitoringSmoke || window.StateText != "PARADO") throw new InvalidOperationException("Botão Parar não parou o monitor.");
            await window.StartSmokeAsync(new(local, cloud)); await Task.Delay(1300); await window.StopSmokeAsync();
            if (AppFiles.LoadConfig(state)?.MonitorOnLaunch != false) throw new InvalidOperationException("Parada não persistida.");
            if (!File.Exists(Path.Combine(state, "logs", "execution.jsonl"))) throw new InvalidOperationException("Log de execução ausente.");
            if (Directory.GetDirectories(state, "migration-backup", SearchOption.AllDirectories).Length != 0) throw new InvalidOperationException("Início ainda dependeu de backup.");
            window.SelectTabSmoke(3); Capture(window, Path.Combine(output, "options.png"));
            window.SelectTabSmoke(0); window.Width = window.MinWidth; window.Height = window.MinHeight; Capture(window, Path.Combine(output, "compact.png")); window.AssertFolderLayoutSmoke();
            AppFiles.SaveConfig(new(local, cloud, MonitorOnLaunch: true), state);
            var upgraded = new MainWindow(testMode: true, dataRoot: state); upgraded.LoadSavedConfiguration(); if (upgraded.IsMonitoringSmoke) throw new InvalidOperationException("Atualização iniciou nova política sem revisão."); upgraded.CloseSmoke();
        }
        finally { if (window.IsMonitoringSmoke) await window.StopSmokeAsync(); window.CloseSmoke(); }
    }
    private static async Task Until(Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try { if (condition()) return; } catch (IOException) { /* Atomic publication can briefly hold the fixture file. */ }
            if (watch.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Fluxo da interface não sincronizou automaticamente."); await Task.Delay(100);
        }
    }
    private static void RunWithDispatcher(Task task)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        task.ContinueWith(_ => System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.FromCurrentSynchronizationContext());
        System.Windows.Threading.Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
    }
    private static void Capture(Window window, string path, double scale = 1)
    {
        if (!window.IsVisible) window.Show();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        window.Measure(new Size(window.Width, window.Height)); window.Arrange(new Rect(0, 0, window.Width, window.Height)); window.UpdateLayout();
        var image = new RenderTargetBitmap((int)(window.Width * scale), (int)(window.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void AssertScrollbars(DependencyObject root)
    {
        var bars = Descendants(root).OfType<System.Windows.Controls.Primitives.ScrollBar>().Where(b => b.IsVisible && b.Orientation == System.Windows.Controls.Orientation.Vertical).ToList();
        if (bars.Count < 2) throw new InvalidOperationException("Comparação não expôs rolagem nas duas versões.");
        foreach (var bar in bars)
        {
            if (bar.ActualWidth > 10.1 || bar.Template.FindName("PART_Track", bar) is not System.Windows.Controls.Primitives.Track track || track.Thumb == null) throw new InvalidOperationException("Barra de rolagem fora do tema.");
            double initial = bar.Value; bar.Value = bar.Maximum; bar.UpdateLayout();
            if (bar.Value != bar.Maximum) throw new InvalidOperationException("Rolagem não alcança o final."); bar.Value = initial;
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
}
