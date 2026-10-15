using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using VaultSync.Core;

namespace VaultSync.App;

public static class Program
{
    public const string Product = "Vault Sync - Obsidian";
    [STAThread]
    public static void Main(string[] args)
    {
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
        app.DispatcherUnhandledException += (_, e) => { MessageBox.Show(e.Exception.Message, Product); e.Handled = true; };
        if (args.Contains("--uninstall")) { Installer.Uninstall(); return; }
        if (!args.Contains("--app") && !args.Contains("--portable") && !Installer.IsInstalledExecutable)
        { app.ShutdownMode = ShutdownMode.OnMainWindowClose; app.Run(new SetupWindow()); return; }
        using var mutex = new Mutex(true, "Local\\VaultSync-" + Environment.UserName, out bool created);
        if (!created) { if (!args.Contains("--background")) MessageBox.Show("O aplicativo já está em execução. Abra pelo ícone na bandeja.", Product); return; }
        var window = new MainWindow(); app.MainWindow = window;
        if (!args.Contains("--background")) window.Show();
        else window.LoadSavedConfiguration();
        app.Run();
    }
}
