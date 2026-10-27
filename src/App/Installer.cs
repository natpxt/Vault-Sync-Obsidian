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
    private sealed record TestPaths(string Root, string RegistryRoot, string Mutex);
    private static TestPaths? testPaths;
    public static string InstallRoot => testPaths == null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "VaultSync") : Path.Combine(testPaths.Root, "app");
    public static string InstalledExe => Path.Combine(InstallRoot, "VaultSync.exe");
    private static string UninstallKey => testPaths == null ? @"Software\Microsoft\Windows\CurrentVersion\Uninstall\VaultSync" : testPaths.RegistryRoot + @"\Uninstall";
    private static string StartupKey => testPaths == null ? @"Software\Microsoft\Windows\CurrentVersion\Run" : testPaths.RegistryRoot + @"\Run";
    private static string MutexName => testPaths?.Mutex ?? "Local\\VaultSync-" + Environment.UserName;
    public static bool IsInstalledExecutable => Paths.Normalize(Environment.ProcessPath!).Equals(Paths.Normalize(InstalledExe), StringComparison.OrdinalIgnoreCase);
    private static string Shortcut => testPaths == null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Program.Product + ".lnk") : Path.Combine(testPaths.Root, "VaultSync-test.lnk");
    public static void Install(bool startup, bool launch)
    {
        using var running = new Mutex(false, MutexName);
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
            foreach (string resource in assembly.GetManifestResourceNames().Where(x => x.Contains(".Resources.", StringComparison.Ordinal) && x.EndsWith(".txt", StringComparison.Ordinal)))
            {
                using var stream = assembly.GetManifestResourceStream(resource)!; using var reader = new StreamReader(stream);
                notices.AppendLine(resource); notices.AppendLine(reader.ReadToEnd());
            }
            File.WriteAllText(Path.Combine(InstallRoot, "ThirdPartyNotices.txt"), notices.ToString());
            using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", Program.Product); key.SetValue("DisplayVersion", Program.Version);
                key.SetValue("Publisher", "Natan Peixoto Maciel"); key.SetValue("InstallLocation", InstallRoot);
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
        using var run = Registry.CurrentUser.CreateSubKey(StartupKey);
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
        string id = Guid.NewGuid().ToString("N");
        var sandbox = new TestPaths(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VaultSyncBuild", "installer-tests", id), @"Software\VaultSync\InstallerTests\" + id, "Local\\VaultSync-Test-" + id);
        if (testPaths != null) throw new InvalidOperationException("Verificação de instalação já em andamento.");
        testPaths = sandbox;
        var results = new List<string>();
        try
        {
        Directory.CreateDirectory(sandbox.Root);
        Install(false, false);
        if (!File.Exists(InstalledExe) || !File.Exists(Shortcut)) throw new IOException("Instalação incompleta.");
        using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey))
            if (key?.GetValue("DisplayName") as string != Program.Product) throw new IOException("Registro de desinstalação ausente.");
        results.Add("install-per-user");
        if (Scanner.Read(Environment.ProcessPath!)!.Hash != Scanner.Read(InstalledExe)!.Hash) throw new IOException("Hash instalado divergente.");
        results.Add("installed-sha256");
        SetStartup(true);
        using (var key = Registry.CurrentUser.OpenSubKey(StartupKey))
            if ((key?.GetValue("VaultSync") as string)?.Contains("--background", StringComparison.Ordinal) != true) throw new IOException("Inicialização automática ausente.");
        results.Add("startup");
        Install(false, false); results.Add("update");
        string data = Path.Combine(sandbox.Root, "data"); Directory.CreateDirectory(data);
        string sentinel = Path.Combine(data, "preserve.txt"); File.WriteAllText(sentinel, "preserve user data");
        SetStartup(false); Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); File.Delete(Shortcut);
        Cleanup(["--remove-install", "-1", InstallRoot]);
        if (!File.Exists(sentinel) || File.Exists(InstalledExe)) throw new IOException("Desinstalação não preservou dados ou não removeu o executável.");
        results.Add("uninstall-preserves-data");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(new { status = "passed", isolated = true, root = sandbox.Root, results }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            // The context is created internally with a GUID; never accept test roots from command-line input.
            Registry.CurrentUser.DeleteSubKeyTree(sandbox.RegistryRoot, false);
            if (File.Exists(Shortcut)) File.Delete(Shortcut);
            Cleanup(["--remove-install", "-1", InstallRoot]);
            testPaths = null;
        }
    }
    public static void Uninstall()
    {
        using var running = new Mutex(false, "Local\\VaultSync-" + Environment.UserName);
        if (!running.WaitOne(0)) { ThemedDialog.Info(null, "Feche o aplicativo pelo ícone da bandeja antes de desinstalar."); return; }
        try
        {
            int conflicts = 0, pending = 0;
            var config = AppFiles.LoadConfig();
            if (config != null)
            {
                using var state = new StateStore(Path.Combine(AppFiles.DataRoot, config.PairId));
                conflicts = state.Conflicts().Count; pending = state.PendingErrors().Count;
            }
            if (!ThemedDialog.Confirm(null, $"Desinstalar {Program.Product}?\n\nConflitos preservados: {conflicts}\nPendências registradas: {pending}\n\nOs vaults, backups e estado local serão preservados em:\n{AppFiles.DataRoot}")) return;
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
        var panel = new StackPanel { Margin = new Thickness(14) }; Ui.Chrome(this, new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, "Instalar · Vault Sync");
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 18) }; brand.Children.Add(Brand.Logo(72));
        var signature = new StackPanel(); signature.Children.Add(Ui.Text("Vault Sync", 30)); signature.Children.Add(Ui.Text("Para seus vaults do Obsidian", 13, Ui.Muted)); brand.Children.Add(signature); panel.Children.Add(brand);
        panel.Children.Add(Ui.Text("Local primeiro. Sincronização visível.", 18));
        panel.Children.Add(Ui.Text("Instalação para este usuário, sem administrador. O aplicativo inclui seu runtime e permite escolher as pastas no primeiro uso."));
        panel.Children.Add(Ui.Text("Destino: " + Installer.InstallRoot, 12, Ui.Muted));
        var startup = new CheckBox { Content = "Iniciar com o Windows", IsChecked = true, Foreground = Ui.Ink, Margin = new Thickness(0, 12, 0, 12) }; panel.Children.Add(startup);
        panel.Children.Add(Ui.Text("O aplicativo não é afiliado à Obsidian ou à Apple. A instalação não migra seus arquivos automaticamente.", 12, Ui.Muted));
        panel.Children.Add(Ui.Text("Desenvolvido por Natan Peixoto Maciel", 11, Ui.Muted));
        panel.Children.Add(Ui.Primary(Ui.Button("Instalar e abrir", (_, _) =>
        {
            try { Installer.Install(startup.IsChecked == true, true); Close(); }
            catch (Exception ex) { ThemedDialog.Info(this, ex.Message); }
        })));
    }
}
