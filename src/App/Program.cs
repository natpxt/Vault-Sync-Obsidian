using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using VaultSync.Core;
using VaultSync.Updates;
using System.Reflection;

namespace VaultSync.App;

public static class Program
{
    public const string Product = "Vault Sync - Obsidian";
    public static readonly string Version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
    internal const string GitHubUrl = "https://github.com/natpxt/Vault-Sync-Obsidian";
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.FirstOrDefault() == "--self-test-update-owner") { try { UpdateRuntime.TestOwner(args[1]); } catch { Environment.ExitCode = 1; } return; }
        if (args.FirstOrDefault() == "--test-updates") { try { UpdateRuntimeTests.Run(args[1], args[2]); } catch (Exception ex) { File.WriteAllText(Path.Combine(Path.GetTempPath(), "vaultsync-update-test-error.txt"), ex.ToString()); Environment.ExitCode = 1; } return; }
        if (args.FirstOrDefault() == "--test-update-network") { try { UpdateRuntimeTests.Probe(args[1]); } catch (Exception ex) { File.WriteAllText(Path.Combine(Path.GetTempPath(), "vaultsync-update-network-error.txt"), ex.ToString()); Environment.ExitCode = 1; } return; }
        if (UpdateRuntime.Handle(args)) return;
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
        string? evaluationRoot = UpdateRuntime.Resume?.DataRoot ?? (evaluation ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VaultSyncBuild", "evaluation-data") : null);
        Theme.Initialize(evaluationRoot); app.Exit += (_, _) => Theme.Shutdown();
        app.DispatcherUnhandledException += (_, e) => { ThemedDialog.Info(null, e.Exception.Message); e.Handled = true; };
        if (args.Contains("--uninstall")) { Installer.Uninstall(); return; }
        if (!evaluation && !args.Contains("--app") && !args.Contains("--portable") && !Installer.IsInstalledExecutable)
        { app.ShutdownMode = ShutdownMode.OnMainWindowClose; app.Run(new SetupWindow()); return; }
        if (!evaluation && UpdateRuntime.IsApplying()) { ThemedDialog.Info(null, "Uma atualização está em andamento. Aguarde a conclusão."); return; }
        using var instance = new SingleInstance(evaluation ? "Local\\VaultSync-Evaluation-" + Environment.UserName : Installer.InstanceMutex);
        if (!instance.IsOwner)
        {
            if (!args.Contains("--background") && !instance.RequestActivation(evaluation)) ThemedDialog.Info(null, "Não foi possível reabrir a janela existente. Encerre o Vault Sync pelo Gerenciador de Tarefas e tente novamente.");
            app.Shutdown(); return;
        }
        var window = new MainWindow(testMode: UpdateRuntime.Resume?.Test == true, dataRoot: evaluationRoot, evaluationMode: evaluation); app.MainWindow = window;
        if (UpdateRuntime.Resume is { } resume) window.ApplyUpdateResume(resume.Job.Resume, UpdateRuntime.ReadResult(resume.File));
        instance.Listen(() => { if (!app.Dispatcher.HasShutdownStarted) app.Dispatcher.BeginInvoke(window.Reveal); });
        if (!args.Contains("--background") && UpdateRuntime.Resume?.Job.Visible != false) window.Show();
        else window.LoadSavedConfiguration();
        if (UpdateRuntime.Resume?.Test == true) { window.LoadSavedConfiguration(); window.Dispatcher.BeginInvoke(() => UpdateRuntime.TestResumed(window)); }
        window.Dispatcher.BeginInvoke(window.StartUpdateChecks);
        if (UpdateRuntime.Resume != null) window.Dispatcher.BeginInvoke(() => _ = System.Threading.Tasks.Task.Run(UpdateRuntime.CleanupResumed));
        app.Run();
    }
}
