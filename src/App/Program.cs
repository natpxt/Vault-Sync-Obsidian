using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using VaultSync.Core;

namespace VaultSync.App;

public static class Program
{
    public const string Product = "Vault Sync - Obsidian";
    public const string Version = "1.1.0";
    internal const string GitHubUrl = "https://github.com/natpxt/Vault-Sync-Obsidian";
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.FirstOrDefault() == "--export-brand") { Brand.ExportIcon(args[1]); return; }
        if (args.FirstOrDefault() == "--recover-window") { Environment.ExitCode = SingleInstance.RestoreLegacy(args.Contains("--evaluate")) ? 0 : 2; return; }
        if (args.FirstOrDefault() == "--close-legacy-window") { Environment.ExitCode = SingleInstance.RestoreLegacy(args.Contains("--evaluate"), true) ? 0 : 2; return; }
        if (args.FirstOrDefault() == "--self-test-activate")
        {
            string name = args.ElementAtOrDefault(1) ?? "";
            if (!name.StartsWith("Local\\VaultSync-Test-", StringComparison.Ordinal)) { Environment.ExitCode = 2; return; }
            using var sender = new SingleInstance(name); Environment.ExitCode = !sender.IsOwner && sender.RequestActivation(false, false) ? 0 : 2; return;
        }
        if (args.FirstOrDefault() == "--test-installation")
        {
            try { Installer.TestLifecycle(args.ElementAtOrDefault(1) ?? Path.Combine(Path.GetTempPath(), "vaultsync-installation.json")); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(Path.GetTempPath(), "vaultsync-installation-error.txt"), ex.ToString()); Environment.ExitCode = 1; }
            return;
        }
        if (args.FirstOrDefault() == "--install-only") { Installer.Install(true, false); return; }
        if (args.FirstOrDefault() == "--remove-install") { Installer.Cleanup(args); return; }
        if (args.FirstOrDefault() == "--self-test")
        {
            try { UiSmoke.Run(args.ElementAtOrDefault(1)); Environment.ExitCode = 0; }
            catch (Exception ex) { File.WriteAllText(Path.Combine(Path.GetTempPath(), "vaultsync-ui-error.txt"), ex.ToString()); Environment.ExitCode = 1; }
            return;
        }
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        bool evaluation = args.Contains("--evaluate") || Path.GetFileNameWithoutExtension(Environment.ProcessPath!).EndsWith("-Avaliacao", StringComparison.OrdinalIgnoreCase);
        string? evaluationRoot = evaluation ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VaultSyncBuild", "evaluation-data") : null;
        Theme.Initialize(evaluationRoot); app.Exit += (_, _) => Theme.Shutdown();
        app.DispatcherUnhandledException += (_, e) => { ThemedDialog.Info(null, e.Exception.Message); e.Handled = true; };
        if (args.Contains("--uninstall")) { Installer.Uninstall(); return; }
        if (!evaluation && !args.Contains("--app") && !args.Contains("--portable") && !Installer.IsInstalledExecutable)
        { app.ShutdownMode = ShutdownMode.OnMainWindowClose; app.Run(new SetupWindow()); return; }
        using var instance = new SingleInstance("Local\\VaultSync-" + (evaluation ? "Evaluation-" : "") + Environment.UserName);
        if (!instance.IsOwner)
        {
            if (!args.Contains("--background") && !instance.RequestActivation(evaluation)) ThemedDialog.Info(null, "Não foi possível reabrir a janela existente. Encerre o Vault Sync pelo Gerenciador de Tarefas e tente novamente.");
            app.Shutdown(); return;
        }
        var window = new MainWindow(dataRoot: evaluationRoot, evaluationMode: evaluation); app.MainWindow = window;
        instance.Listen(() => { if (!app.Dispatcher.HasShutdownStarted) app.Dispatcher.BeginInvoke(window.Reveal); });
        if (!args.Contains("--background")) window.Show();
        else window.LoadSavedConfiguration();
        app.Run();
    }
}
