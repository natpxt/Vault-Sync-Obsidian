using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using VaultSync.Core;

namespace VaultSync.App;

internal static class Installer
{
    public static string InstallRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "VaultSync");
    public static string InstalledExe => Path.Combine(InstallRoot, "VaultSync.exe");
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\VaultSync";
    public static bool IsInstalledExecutable => Paths.Normalize(Environment.ProcessPath!).Equals(Paths.Normalize(InstalledExe), StringComparison.OrdinalIgnoreCase);
    private static string Shortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Program.Product + ".lnk");
    public static void Install(bool startup, bool launch)
    {
        using var running = new Mutex(false, "Local\\VaultSync-" + Environment.UserName);
        if (!running.WaitOne(0)) throw new IOException("Feche o Vault Sync pelo ícone da bandeja antes de atualizar.");
        try
        {
            Directory.CreateDirectory(InstallRoot);
            string source = Environment.ProcessPath!;
            if (!source.Equals(InstalledExe, StringComparison.OrdinalIgnoreCase))
            {
                string temp = InstalledExe + ".new"; File.Copy(source, temp, true);
                if (Scanner.Read(source)!.Hash != Scanner.Read(temp)!.Hash) throw new IOException("Instalador copiado com conteúdo divergente.");
                File.Move(temp, InstalledExe, true);
            }
            CreateShortcut();
            var notices = new System.Text.StringBuilder("Vault Sync - Obsidian · avisos de terceiros\n.NET / WPF / Windows Forms / Microsoft.Data.Sqlite: Microsoft e contribuidores, MIT. SQLite: domínio público. SQLitePCLRaw: Apache-2.0.\n\n");
            var assembly = typeof(Installer).Assembly;
            foreach (string resource in assembly.GetManifestResourceNames().Where(x => x.Contains(".Resources.", StringComparison.Ordinal)))
            {
                using var stream = assembly.GetManifestResourceStream(resource)!; using var reader = new StreamReader(stream);
                notices.AppendLine(resource); notices.AppendLine(reader.ReadToEnd());
            }
            File.WriteAllText(Path.Combine(InstallRoot, "ThirdPartyNotices.txt"), notices.ToString());
            using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", Program.Product); key.SetValue("DisplayVersion", "0.1.0");
                key.SetValue("Publisher", "Projeto pessoal Vault Sync"); key.SetValue("InstallLocation", InstallRoot);
                key.SetValue("UninstallString", "\"" + InstalledExe + "\" --uninstall");
                key.SetValue("DisplayIcon", InstalledExe); key.SetValue("NoModify", 1); key.SetValue("NoRepair", 1);
                key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024));
            }
            SetStartup(startup);
        }
        finally { running.ReleaseMutex(); }
        if (launch) Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, Arguments = "--app" });
    }
    public static void SetStartup(bool enabled)
    {
        using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) run.SetValue("VaultSync", "\"" + InstalledExe + "\" --app --background");
        else run.DeleteValue("VaultSync", false);
    }
    private static void CreateShortcut()
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Não foi possível criar o atalho do menu Iniciar.");
        object shell = Activator.CreateInstance(type)!;
        object shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [Shortcut])!;
        try
        {
            shortcut.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, [InstalledExe]);
            shortcut.GetType().InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, ["--app"]);
            shortcut.GetType().InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, [InstallRoot]);
            shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }
        finally { Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell); }
    }
    internal static void TestLifecycle(string report)
    {
        if (Directory.Exists(InstallRoot) || Registry.CurrentUser.OpenSubKey(UninstallKey) != null)
            throw new IOException("Teste de instalação requer ausência de instalação anterior; nenhuma instalação existente será alterada.");
        var results = new List<string>();
        Install(false, false);
        if (!File.Exists(InstalledExe) || !File.Exists(Shortcut)) throw new IOException("Instalação incompleta.");
        using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey))
            if (key?.GetValue("DisplayName") as string != Program.Product) throw new IOException("Registro de desinstalação ausente.");
        results.Add("install-per-user");
        if (Scanner.Read(Environment.ProcessPath!)!.Hash != Scanner.Read(InstalledExe)!.Hash) throw new IOException("Hash instalado divergente.");
        results.Add("installed-sha256");
        SetStartup(true);
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            if ((key?.GetValue("VaultSync") as string)?.Contains("--background", StringComparison.Ordinal) != true) throw new IOException("Inicialização automática ausente.");
        results.Add("startup");
        Install(false, false); results.Add("update");
        Directory.CreateDirectory(AppFiles.DataRoot);
        string sentinel = Path.Combine(AppFiles.DataRoot, ".installation-check-" + Guid.NewGuid().ToString("N")); File.WriteAllText(sentinel, "preserve user data");
        SetStartup(false); Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); File.Delete(Shortcut);
        Paths.CheckAncestors(InstalledExe); File.Delete(InstalledExe); File.Delete(Path.Combine(InstallRoot, "ThirdPartyNotices.txt")); Directory.Delete(InstallRoot, false);
        if (!File.Exists(sentinel)) throw new IOException("Dados locais não preservados."); File.Delete(sentinel);
        results.Add("uninstall-preserves-data");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(new { status = "passed", results }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
    public static void Uninstall()
    {
        using var running = new Mutex(false, "Local\\VaultSync-" + Environment.UserName);
        if (!running.WaitOne(0)) { MessageBox.Show("Feche o aplicativo pelo ícone da bandeja antes de desinstalar.", Program.Product); return; }
        try
        {
            int conflicts = 0, pending = 0;
            var config = AppFiles.LoadConfig();
            if (config != null)
            {
                using var state = new StateStore(Path.Combine(AppFiles.DataRoot, config.PairId));
                conflicts = state.Conflicts().Count; pending = state.PendingErrors().Count;
            }
            if (MessageBox.Show($"Desinstalar {Program.Product}?\n\nConflitos preservados: {conflicts}\nPendências registradas: {pending}\n\nOs vaults, backups e estado local serão preservados em:\n{AppFiles.DataRoot}", Program.Product, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            SetStartup(false); Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            if (File.Exists(Shortcut)) File.Delete(Shortcut);
            string helper = Path.Combine(Path.GetTempPath(), "VaultSync-uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(Environment.ProcessPath!, helper);
            Process.Start(new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--remove-install", Environment.ProcessId.ToString(), InstallRoot } });
        }
        finally { running.ReleaseMutex(); }
    }
    public static void Cleanup(string[] args)
    {
        if (args.Length != 3 || !int.TryParse(args[1], out int pid) || !Paths.Normalize(args[2]).Equals(Paths.Normalize(InstallRoot), StringComparison.OrdinalIgnoreCase)) return;
        try { Process.GetProcessById(pid).WaitForExit(30000); } catch (ArgumentException) { }
        // Only application-owned executable files; never recursively delete an installation root.
        foreach (string name in new[] { "VaultSync.exe", "VaultSync.exe.new", "ThirdPartyNotices.txt" })
        {
            string file = Path.Combine(InstallRoot, name);
            try { Paths.CheckAncestors(file); if (File.Exists(file)) File.Delete(file); } catch (IOException) { }
        }
        try { Directory.Delete(InstallRoot, false); } catch (IOException) { }
    }
}
internal sealed class SetupWindow : Window
{
    public SetupWindow()
    {
        Title = "Instalar · " + Program.Product; Width = 670; Height = 480; ResizeMode = ResizeMode.NoResize; Ui.Style(this);
        var panel = new StackPanel { Margin = new Thickness(32) }; Content = panel;
        panel.Children.Add(Ui.Text("VAULT SYNC", 32, Ui.Accent));
        panel.Children.Add(Ui.Text("Obsidian · Local primeiro. Sincronização visível.", 18));
        panel.Children.Add(Ui.Text("Instalação para este usuário, sem administrador. O aplicativo inclui seu runtime e permite escolher as pastas no primeiro uso."));
        panel.Children.Add(Ui.Text("Destino: " + Installer.InstallRoot, 12, Ui.Muted));
        var startup = new CheckBox { Content = "Iniciar com o Windows", IsChecked = true, Foreground = Ui.Ink, Margin = new Thickness(0, 12, 0, 12) }; panel.Children.Add(startup);
        panel.Children.Add(Ui.Text("O aplicativo não é afiliado à Obsidian ou à Apple. A instalação não migra seus arquivos automaticamente.", 12, Ui.Muted));
        panel.Children.Add(Ui.Button("Instalar e abrir", (_, _) =>
        {
            try { Installer.Install(startup.IsChecked == true, true); Close(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, Program.Product); }
        }));
    }
}
