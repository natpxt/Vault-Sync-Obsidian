using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using VaultSync.Core;
using VaultSync.Updates;

namespace VaultSync.App;

// All paths are derived from the current user's application directory. A request cannot select an installation target.
internal static class UpdateRuntime
{
    internal static string Root => Path.Combine(AppFiles.DataRoot, "updates");
    private sealed record Context(string Updates, string Data, bool Test);
    internal sealed record ResumeRequest(string File, UpdateHandoff Job, string DataRoot, bool Test);
    private sealed record RegistryEntry(string Name, RegistryValueKind Kind, string Value);
    private sealed record Snapshot(List<RegistryEntry>? Uninstall, List<RegistryEntry> Startup, bool Exe, bool Notices, bool Shortcut);
    internal static ResumeRequest? Resume { get; private set; }

    internal static bool IsApplying()
    {
        string file = Path.Combine(Root, "apply.lock");
        try { UpdateStore.CheckPath(file); if (!File.Exists(file)) return false; using var access = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return false; }
        catch (IOException) { return true; }
    }
    private static Context Validate(string file, bool test)
    {
        file = Path.GetFullPath(file); UpdateStore.CheckPath(file);
        var jobDir = Directory.GetParent(file) ?? throw new IOException("Diretório da atualização inválido.");
        if (Path.GetFileName(file) != "request.json" || !Guid.TryParseExact(jobDir.Name, "N", out _) || jobDir.Parent?.Name != "jobs") throw new IOException("Solicitação fora dos diretórios de atualização.");
        string updates = jobDir.Parent.Parent!.FullName, data = AppFiles.DataRoot;
        if (test)
        {
            string sandbox = Directory.GetParent(updates)!.FullName;
            string parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VaultSyncBuild", "updater-tests");
            if (Path.GetFileName(updates) != "updates" || !Guid.TryParseExact(Path.GetFileName(sandbox), "N", out _) || !Directory.GetParent(sandbox)!.FullName.Equals(parent, StringComparison.OrdinalIgnoreCase)) throw new IOException("Sandbox de atualização inválido.");
            Installer.UseUpdateSandbox(sandbox, Path.GetFileName(sandbox)); data = Path.Combine(sandbox, "data");
        }
        else if (!updates.Equals(Root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Solicitação fora do diretório deste usuário.");
        UpdateStore.CheckPath(Installer.InstallRoot); UpdateStore.CheckPath(Installer.InstalledExe);
        return new(updates, data, test);
    }
    internal static bool Handle(string[] args)
    {
        string mode = args.FirstOrDefault() ?? "";
        if (mode is not ("--apply-update" or "--install-update" or "--resume-update" or "--apply-update-test" or "--install-update-test" or "--resume-update-test")) return false;
        try
        {
            if (args.Length != 2) throw new IOException("Parâmetros de atualização inválidos.");
            string file = Path.GetFullPath(args[1]); bool test = mode.EndsWith("-test", StringComparison.Ordinal);
            var context = Validate(file, test); var job = HandoffFile.Read(file);
            if (mode.StartsWith("--resume-", StringComparison.Ordinal))
            {
                if (!Paths.Normalize(Environment.ProcessPath!).Equals(Paths.Normalize(Installer.InstalledExe), StringComparison.OrdinalIgnoreCase)) throw new IOException("A retomada deve usar o aplicativo instalado.");
                Resume = new(file, job, context.Data, test); return false;
            }
            if (mode.StartsWith("--install-", StringComparison.Ordinal)) InstallPackage(file, job, test);
            else Apply(file, job, context);
            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            // Never write to the unvalidated command-line path on failure.
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VaultSyncBuild");
            Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "update-helper-error.txt"), ex.ToString());
        }
        return true;
    }
    private static void VerifyPackage(string file, UpdateHandoff job)
    {
        UpdateStore.CheckPath(file);
        if (new FileInfo(file).Length > GitHubUpdates.MaximumPackageSize) throw new IOException("Pacote de atualização muito grande.");
        if (!Scanner.Read(file)!.Hash.Equals(job.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("O instalador mudou após a validação.");
        var version = UpdateVersion.Parse(FileVersionInfo.GetVersionInfo(file).ProductVersion ?? "");
        if (version.Preview || version.CompareTo(UpdateVersion.Parse(job.Version)) != 0) throw new IOException("Versão do instalador divergente.");
    }
    private static void InstallPackage(string file, UpdateHandoff job, bool test)
    {
        string expected = Path.Combine(Path.GetDirectoryName(file)!, job.PackageName);
        if (!Paths.Normalize(Environment.ProcessPath!).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("O pacote deve ser executado no diretório verificado.");
        VerifyPackage(expected, job); Installer.Install(job.Startup, false);
        VerifyPackage(Installer.InstalledExe, job);
        if (test && File.Exists(Path.Combine(Path.GetDirectoryName(file)!, "inject-failure-after-install"))) throw new IOException("Falha simulada após substituição.");
    }
    private static void Apply(string file, UpdateHandoff job, Context context)
    {
        string directory = Path.GetDirectoryName(file)!, package = Path.Combine(directory, job.PackageName), helper = Path.Combine(directory, "helper.exe");
        if (!Paths.Normalize(Environment.ProcessPath!).Equals(helper, StringComparison.OrdinalIgnoreCase)) throw new IOException("Auxiliar fora do diretório verificado.");
        if (File.Exists(Path.Combine(directory, "result.json"))) throw new IOException("Solicitação de atualização já concluída.");
        VerifyPackage(package, job);
        UpdateStore.CheckPath(context.Updates);
        using var applyLock = new FileStream(Path.Combine(context.Updates, "apply.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        bool installationStarted = false;
        try
        {
            bool ready = false;
            try
            {
                using var old = Process.GetProcessById(job.OldPid);
                if (!old.HasExited)
                {
                    if (old.StartTime.ToUniversalTime().Ticks != job.OldProcessTicks || !Paths.Normalize(old.MainModule!.FileName).Equals(Installer.InstalledExe, StringComparison.OrdinalIgnoreCase)) throw new IOException("O processo anterior não corresponde ao aplicativo instalado.");
                    File.WriteAllText(Path.Combine(directory, "helper-ready.json"), "{\"ready\":true}"); ready = true;
                    if (!old.WaitForExit(60000)) throw new IOException("O aplicativo ainda está concluindo uma operação. Atualização adiada.");
                }
            }
            catch (ArgumentException) { /* Already exited. */ }
            if (!ready) File.WriteAllText(Path.Combine(directory, "helper-ready.json"), "{\"ready\":true}");
            string backup = Path.Combine(directory, "backup");
            var snapshot = Backup(backup);
            installationStarted = true;
            try
            {
                Exception? failure = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    VerifyPackage(package, job);
                    var command = new ProcessStartInfo(package) { UseShellExecute = false, CreateNoWindow = true };
                    command.ArgumentList.Add(context.Test ? "--install-update-test" : "--install-update"); command.ArgumentList.Add(file);
                    using var installer = Process.Start(command) ?? throw new IOException("Não foi possível iniciar o instalador.");
                    installer.WaitForExit();
                    if (installer.ExitCode == 0) { failure = null; break; }
                    failure = new IOException("O Windows não concluiu a instalação. A versão anterior será preservada."); Thread.Sleep(1000 * (attempt + 1));
                }
                if (failure != null) throw failure;
                VerifyPackage(Installer.InstalledExe, job);
                WriteResult(directory, true, "Atualização instalada e verificada.");
            }
            catch (Exception ex)
            {
                Restore(backup, snapshot); WriteResult(directory, false, "A atualização falhou; a versão anterior foi restaurada. " + ex.Message);
            }
            // Release the installation lock before the app reopens.
            applyLock.Dispose();
            try
            {
                if (context.Test && File.Exists(Path.Combine(directory, "inject-resume-start-failure"))) throw new IOException("Falha simulada ao reabrir a versão nova.");
                StartResumed(file, context.Test);
            }
            catch (Exception ex)
            {
                using (var rollback = new FileStream(Path.Combine(context.Updates, "apply.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                { Restore(backup, snapshot); WriteResult(directory, false, "A atualização falhou; a versão anterior foi restaurada. " + ex.Message); }
                StartResumed(file, context.Test);
            }
        }
        catch (Exception ex)
        {
            WriteResult(directory, false, ex.Message); applyLock.Dispose();
            bool oldExited;
            try { using var old = Process.GetProcessById(job.OldPid); oldExited = old.HasExited; } catch (ArgumentException) { oldExited = true; }
            if (!installationStarted && oldExited) StartResumed(file, context.Test);
            throw;
        }
    }
    private static void StartResumed(string file, bool test)
    {
        var resume = new ProcessStartInfo(Installer.InstalledExe) { UseShellExecute = false, CreateNoWindow = true };
        resume.ArgumentList.Add(test ? "--resume-update-test" : "--resume-update"); resume.ArgumentList.Add(file);
        _ = Process.Start(resume) ?? throw new IOException("Não foi possível reabrir o aplicativo.");
    }
    internal static void CleanupResumed()
    {
        var resume = Resume; if (resume == null || ReadResult(resume.File)?.Success != true) return;
        try
        {
            var context = Validate(resume.File, resume.Test); string current = Path.GetDirectoryName(resume.File)!;
            // Keep this job's previous executable and metadata. Package/helper are no longer needed.
            foreach (string name in new[] { resume.Job.PackageName, "helper.exe" })
            { string path = Path.Combine(current, name); UpdateStore.CheckPath(path); try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            foreach (string directory in Directory.GetDirectories(Path.Combine(context.Updates, "jobs")))
            {
                if (directory == current || !Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) continue;
                string result = Path.Combine(directory, "result.json"); UpdateStore.CheckPath(result);
                if (!File.Exists(result) || JsonSerializer.Deserialize<UpdateResult>(File.ReadAllText(result))?.Success != true) continue;
                CheckTree(directory);
                try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { /* Preserve diagnostics when a file is still busy. */ }
    }
    private static void CheckTree(string directory)
    {
        UpdateStore.CheckPath(directory);
        foreach (string path in Directory.GetFileSystemEntries(directory))
        { UpdateStore.CheckPath(path); if (Directory.Exists(path)) CheckTree(path); }
    }
    private static List<RegistryEntry> ReadRegistry(string path, string? only = null)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValueNames().Where(n => only == null || n == only).Select(n => new RegistryEntry(n, key.GetValueKind(n), JsonSerializer.Serialize(key.GetValue(n, null, RegistryValueOptions.DoNotExpandEnvironmentNames)))).ToList() ?? [];
    }
    private static Snapshot Backup(string backup)
    {
        Directory.CreateDirectory(backup); UpdateStore.CheckPath(backup);
        using var uninstall = Registry.CurrentUser.OpenSubKey(Installer.RegistryPath);
        var snapshot = new Snapshot(uninstall == null ? null : ReadRegistry(Installer.RegistryPath), ReadRegistry(Installer.StartupPath, "VaultSync"), File.Exists(Installer.InstalledExe), File.Exists(Path.Combine(Installer.InstallRoot, "ThirdPartyNotices.txt")), File.Exists(Installer.ShortcutPath));
        foreach (var pair in OwnFiles(backup)) if (File.Exists(pair.Target)) { UpdateStore.CheckPath(pair.Target); File.Copy(pair.Target, pair.Backup, false); }
        File.WriteAllText(Path.Combine(backup, "metadata.json"), JsonSerializer.Serialize(snapshot)); return snapshot;
    }
    private static IEnumerable<(string Target, string Backup)> OwnFiles(string root) => new[] {
        (Installer.InstalledExe, Path.Combine(root, "VaultSync.exe")),
        (Path.Combine(Installer.InstallRoot, "ThirdPartyNotices.txt"), Path.Combine(root, "ThirdPartyNotices.txt")),
        (Installer.ShortcutPath, Path.Combine(root, "shortcut.lnk")) };
    private static void RestoreRegistry(string path, List<RegistryEntry>? entries, bool startup = false)
    {
        if (!startup) Registry.CurrentUser.DeleteSubKeyTree(path, false);
        if (entries == null) return;
        using var key = Registry.CurrentUser.CreateSubKey(path);
        if (startup) key.DeleteValue("VaultSync", false);
        foreach (var entry in entries)
        {
            object value = entry.Kind switch {
                RegistryValueKind.DWord => JsonSerializer.Deserialize<int>(entry.Value), RegistryValueKind.QWord => JsonSerializer.Deserialize<long>(entry.Value),
                RegistryValueKind.Binary => JsonSerializer.Deserialize<byte[]>(entry.Value)!, RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(entry.Value)!,
                _ => JsonSerializer.Deserialize<string>(entry.Value)! };
            key.SetValue(entry.Name, value, entry.Kind);
        }
    }
    private static void Restore(string backup, Snapshot snapshot)
    {
        using var running = new Mutex(false, Installer.InstanceMutex);
        bool acquired; try { acquired = running.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new IOException("Outra instância está aberta. O backup foi preservado para recuperação.");
        try
        {
            int index = 0; bool[] existed = [snapshot.Exe, snapshot.Notices, snapshot.Shortcut];
            foreach (var pair in OwnFiles(backup))
            {
                UpdateStore.CheckPath(pair.Target);
                if (File.Exists(pair.Backup) && (!File.Exists(pair.Target) || Scanner.Read(pair.Backup)?.Hash != Scanner.Read(pair.Target)?.Hash))
                {
                    string stage = pair.Target + ".vaultsync-rollback"; UpdateStore.CheckPath(stage);
                    for (int attempt = 0; ; attempt++)
                    {
                        try
                        {
                            File.Copy(pair.Backup, stage, true);
                            if (Scanner.Read(stage)?.Hash != Scanner.Read(pair.Backup)?.Hash) throw new IOException("Backup de instalação divergente.");
                            File.Move(stage, pair.Target, true); break;
                        }
                        catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 2) { Thread.Sleep(500 * (attempt + 1)); }
                    }
                }
                else if (!existed[index] && File.Exists(pair.Target)) File.Delete(pair.Target);
                index++;
            }
            RestoreRegistry(Installer.RegistryPath, snapshot.Uninstall); RestoreRegistry(Installer.StartupPath, snapshot.Startup, true);
        }
        finally { running.ReleaseMutex(); }
    }
    private static void WriteResult(string directory, bool success, string message) => File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new UpdateResult(success, message)));
    internal static string Prepare(VerifiedUpdate update, ResumeState resume, bool visible, bool startup)
    {
        if (!Installer.IsInstalledExecutable) throw new IOException("A atualização automática requer a instalação padrão.");
        long required = new FileInfo(Installer.InstalledExe).Length * 2 + new FileInfo(update.Path).Length + 10485760;
        if (new DriveInfo(Path.GetPathRoot(Installer.InstallRoot)!).AvailableFreeSpace < required) throw new IOException("Espaço insuficiente para preservar a instalação anterior.");
        string file = Path.Combine(Path.GetDirectoryName(update.Path)!, "request.json");
        Validate(file, false); using var current = Process.GetCurrentProcess();
        HandoffFile.Write(file, new(1, current.Id, current.StartTime.ToUniversalTime().Ticks, update.Release.Version, update.Release.Package.Name, update.Sha256, startup, resume, visible));
        File.Copy(Installer.InstalledExe, Path.Combine(Path.GetDirectoryName(file)!, "helper.exe"), false);
        return file;
    }
    internal static async Task LaunchAsync(string file, CancellationToken ct)
    {
        var start = new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(file)!, "helper.exe")) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(file);
        using var helper = Process.Start(start) ?? throw new IOException("Não foi possível iniciar o auxiliar de atualização.");
        string ready = Path.Combine(Path.GetDirectoryName(file)!, "helper-ready.json");
        var watch = Stopwatch.StartNew();
        while (!File.Exists(ready))
        {
            if (helper.HasExited || watch.Elapsed > TimeSpan.FromSeconds(15)) throw new IOException("O auxiliar não confirmou a preparação. A versão atual foi preservada.");
            await Task.Delay(100, ct);
        }
    }
    internal static UpdateResult? ReadResult(string file)
    { string path = Path.Combine(Path.GetDirectoryName(file)!, "result.json"); return File.Exists(path) ? JsonSerializer.Deserialize<UpdateResult>(File.ReadAllText(path)) : null; }
    internal static void AbandonUnstarted(string directory)
    {
        Validate(Path.Combine(directory, "request.json"), false);
        if (!Directory.Exists(directory) || File.Exists(Path.Combine(directory, "helper-ready.json")) || File.Exists(Path.Combine(directory, "result.json"))) return;
        CheckTree(directory); Directory.Delete(directory, true);
    }
    internal static async void TestResumed(MainWindow window)
    {
        try
        {
            await Task.Delay(1300);
            var status = await window.StatusSmokeAsync();
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Resume!.File)!, "resumed.json"), JsonSerializer.Serialize(new { version = Program.Version, running = status.Running, paused = status.Paused, theme = Theme.Mode.ToString(), language = Strings.Code, conflicts = status.Conflicts.Count }));
            await window.ExitSmokeAsync(); System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(Path.GetDirectoryName(Resume!.File)!, "resume-error.txt"), ex.ToString()); System.Windows.Application.Current.Shutdown(1); }
    }
    internal static void TestOwner(string file)
    {
        Validate(file, true); var job = HandoffFile.Read(file); using var process = Process.GetCurrentProcess();
        using var instance = new SingleInstance(Installer.InstanceMutex);
        if (!instance.IsOwner) throw new IOException("Mutex da fixture já adquirido.");
        HandoffFile.Write(file, job with { OldPid = process.Id, OldProcessTicks = process.StartTime.ToUniversalTime().Ticks });
        string directory = Path.GetDirectoryName(file)!; File.WriteAllText(Path.Combine(directory, "owner-ready"), "ready");
        var watch = Stopwatch.StartNew(); while (!File.Exists(Path.Combine(directory, "owner-exit"))) { if (watch.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Fixture não recebeu liberação."); Thread.Sleep(100); }
    }
}
