using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VaultSync.Core;

namespace VaultSync.App;

internal static class UiSmoke
{
    public static void Run(string? output)
    {
        output ??= Path.Combine(Path.GetTempPath(), "vaultsync-ui-smoke"); Directory.CreateDirectory(output);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        var setup = new SetupWindow(); Capture(setup, Path.Combine(output, "installer.png")); setup.Close();
        var main = new MainWindow(testMode: true); Capture(main, Path.Combine(output, "main.png")); main.CloseSmoke();
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
        RunWithDispatcher(Workflow(output));
        if (ConflictWindow.MarkdownPreview("# Teste\n<script>alert(1)</script>\n![imagem](https://invalid.example/a)").Blocks.Count != 3) throw new InvalidOperationException("Preview inesperado.");
        using var process = System.Diagnostics.Process.GetCurrentProcess(); process.Refresh();
        File.WriteAllText(Path.Combine(output, "result.json"), System.Text.Json.JsonSerializer.Serialize(new { status = "passed", screens = 11, workflow = "start, preview-conflicts, refresh-and-open-comparison, two-way-copy, newest-both-directions, pause, resume, stop, restart, persistent-log, folder-hit-testing, compact-work-area, themed-confirmation, themed-options, policy-upgrade-stays-stopped", networkRenderer = false, workingSetMiB = process.WorkingSet64 / 1048576.0 }));
        app.Shutdown();
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
                await window.StartSmokeAsync(new(local, cloud));
                await Until(() => File.Exists(Path.Combine(local, "inicial.md")));
                var status = await window.StatusSmokeAsync(); if (!status.Running || status.Conflicts.Count != 1) throw new InvalidOperationException("Monitor ou lista de conflitos não refletiu o início real.");
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
    private static void Capture(Window window, string path)
    {
        if (!window.IsVisible) window.Show();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        window.Measure(new Size(window.Width, window.Height)); window.Arrange(new Rect(0, 0, window.Width, window.Height)); window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32); image.Render(window);
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
