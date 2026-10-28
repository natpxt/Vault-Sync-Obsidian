using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using VaultSync.Core;
using VaultSync.Updates;

namespace VaultSync.App;

internal static class UpdateRuntimeTests
{
    internal static void Run(string candidate, string report)
    {
        candidate = Path.GetFullPath(candidate);
        var candidateVersion = UpdateVersion.Parse(FileVersionInfo.GetVersionInfo(candidate).ProductVersion!);
        if (candidateVersion.Preview) throw new IOException("A fixture requer um pacote estável de teste.");
        var results = new List<object>();
        foreach (var mode in new[] { ResumeState.Stopped, ResumeState.Running, ResumeState.Paused }) Scenario(mode, false);
        Scenario(ResumeState.Paused, true);
        Scenario(ResumeState.Paused, false, "after-install");
        Scenario(ResumeState.Stopped, false, "tampered");
        Scenario(ResumeState.Running, false, "restart");
        File.WriteAllText(report, JsonSerializer.Serialize(new { status = "passed", isolated = true, scenarios = results }, new JsonSerializerOptions { WriteIndented = true }));

        void Scenario(ResumeState mode, bool locked, string? failure = null)
        {
            string id = Guid.NewGuid().ToString("N"), root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VaultSyncBuild", "updater-tests", id);
            Directory.CreateDirectory(root); Installer.UseUpdateSandbox(root, id);
            string data = Path.Combine(root, "data"), local = Path.Combine(root, "local"), cloud = Path.Combine(root, "cloud");
            Directory.CreateDirectory(local); Directory.CreateDirectory(cloud); Directory.CreateDirectory(data);
            var config = new SyncConfig(local, cloud, StartWithWindows: false, MonitorOnLaunch: true, PolicyRevision: 1);
            AppFiles.SaveConfig(config, data); string configHash = Scanner.Read(Path.Combine(data, "config.json"))!.Hash;
            File.WriteAllText(Path.Combine(data, "ui-settings.json"), "{\"Theme\":\"Dark\",\"Language\":\"es\"}");
            string uiHash = Scanner.Read(Path.Combine(data, "ui-settings.json"))!.Hash;
            File.WriteAllText(Path.Combine(local, "note.md"), "old");
            using (var seed = new SyncEngine(config, data))
            {
                seed.Apply(seed.Preview()); seed.Store.SetSetting("update-test-preserve", "yes");
                File.WriteAllText(Path.Combine(local, "conflict.md"), "local"); File.WriteAllText(Path.Combine(cloud, "conflict.md"), "cloud");
                var tied = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(Path.Combine(local, "conflict.md"), tied); File.SetLastWriteTimeUtc(Path.Combine(cloud, "conflict.md"), tied); seed.Apply(seed.Preview());
            }
            File.WriteAllText(Path.Combine(local, "note.md"), "new"); File.SetLastWriteTimeUtc(Path.Combine(local, "note.md"), DateTime.UtcNow.AddMinutes(1));
            string backupSentinel = Path.Combine(data, config.PairId, "migration-backup", "sentinel.txt"); Directory.CreateDirectory(Path.GetDirectoryName(backupSentinel)!); File.WriteAllText(backupSentinel, "keep backup");
            Installer.Install(false, false); string oldHash = Scanner.Read(Installer.InstalledExe)!.Hash;
            string jobDir = Path.Combine(root, "updates", "jobs", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(jobDir);
            string packageName = $"VaultSync-Obsidian-{candidateVersion}-win-x64-Setup.exe", package = Path.Combine(jobDir, packageName), file = Path.Combine(jobDir, "request.json");
            File.Copy(candidate, package); File.Copy(Installer.InstalledExe, Path.Combine(jobDir, "helper.exe"));
            HandoffFile.Write(file, new(1, 1, 1, candidateVersion.ToString(), packageName, Scanner.Read(package)!.Hash, false, mode, false));
            if (failure == "after-install") File.WriteAllText(Path.Combine(jobDir, "inject-failure-after-install"), "yes");
            if (failure == "restart") File.WriteAllText(Path.Combine(jobDir, "inject-resume-start-failure"), "yes");
            if (failure == "tampered") File.AppendAllText(package, "tampered after verification");
            try
            {
                using var owner = Start(Installer.InstalledExe, "--self-test-update-owner", file);
                Until(() => File.Exists(Path.Combine(jobDir, "owner-ready")), "old owner ready");
                using var helper = Start(Path.Combine(jobDir, "helper.exe"), "--apply-update-test", file);
                if (failure == "tampered")
                {
                    if (!helper.WaitForExit(15000)) throw new TimeoutException("Invalid package was not refused.");
                    Assert(helper.ExitCode != 0 && !File.Exists(Path.Combine(jobDir, "helper-ready.json")) && !owner.HasExited && Scanner.Read(Installer.InstalledExe)!.Hash == oldHash, "Tampered package reached installation");
                    File.WriteAllText(Path.Combine(jobDir, "owner-exit"), "exit"); owner.WaitForExit(5000); CleanupBinaryFixtures(root); results.Add(new { mode = mode.ToString(), failure, result = "passed", root }); return;
                }
                Until(() => File.Exists(Path.Combine(jobDir, "helper-ready.json")), "helper ready");
                Thread.Sleep(300); Assert(!helper.HasExited && Scanner.Read(Installer.InstalledExe)!.Hash == oldHash, "Helper replaced a running app");
                if (mode == ResumeState.Stopped && failure == null)
                {
                    using var competing = Start(Path.Combine(jobDir, "helper.exe"), "--apply-update-test", file);
                    Assert(competing.WaitForExit(15000) && competing.ExitCode != 0 && !helper.HasExited, "Concurrent updater was not refused");
                }
                using (var held = locked ? new FileStream(Installer.InstalledExe, FileMode.Open, FileAccess.Read, FileShare.Read) : null)
                {
                    File.WriteAllText(Path.Combine(jobDir, "owner-exit"), "exit");
                    if (!helper.WaitForExit(90000)) throw new TimeoutException("Helper não concluiu a fixture.");
                    Assert(helper.ExitCode == 0, "Helper exit failed: " + ReadHelperError());
                    Until(() => File.Exists(Path.Combine(jobDir, "resumed.json")), "resumed proof", 30);
                }
                // The proof precedes clean shutdown; wait for the sandbox instance to release its mutex.
                Until(() => !SandboxRunning(root), "sandbox process exit", 15);
                var result = UpdateRuntime.ReadResult(file)!; using var proof = JsonDocument.Parse(File.ReadAllText(Path.Combine(jobDir, "resumed.json")));
                var value = proof.RootElement;
                bool rollback = locked || failure != null;
                Assert(result.Success != rollback, "Unexpected installation result");
                Assert(value.GetProperty("running").GetBoolean() == (mode != ResumeState.Stopped), "Monitor state lost");
                Assert(value.GetProperty("paused").GetBoolean() == (mode == ResumeState.Paused), "Pause state lost");
                Assert(value.GetProperty("version").GetString() == (rollback ? Program.Version : candidateVersion.ToString()), "Wrong resumed executable");
                Assert(value.GetProperty("theme").GetString() == "Dark" && value.GetProperty("language").GetString() == "es", "UI preferences lost");
                Assert(Scanner.Read(Path.Combine(data, "config.json"))!.Hash == configHash && Scanner.Read(Path.Combine(data, "ui-settings.json"))!.Hash == uiHash, "Configuration changed");
                Assert(File.ReadAllText(Path.Combine(cloud, "note.md")) == (mode == ResumeState.Running ? "new" : "old"), "Paused/stopped monitor copied before resume");
                Assert(File.ReadAllText(backupSentinel) == "keep backup", "Backup lost");
                using var state = new StateStore(Path.Combine(data, config.PairId)); Assert(state.HasSetting("update-test-preserve") && state.Conflicts().Count == 1, "Database or conflicts lost");
                using var startup = Registry.CurrentUser.OpenSubKey(Installer.StartupPath); Assert(startup?.GetValue("VaultSync") == null, "Startup preference changed");
                if (rollback) Assert(Scanner.Read(Installer.InstalledExe)!.Hash == oldHash, "Previous executable not restored");
                CleanupBinaryFixtures(root); results.Add(new { mode = mode.ToString(), locked, failure, result = "passed", root });
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(@"Software\VaultSync\UpdaterTests\" + id, false); Installer.ClearUpdateSandbox(); }
        }
    }
    internal static void Probe(string report)
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VaultSyncBuild", "update-network-probe", Guid.NewGuid().ToString("N"));
        var store = new UpdateStore(root); using var client = new GitHubUpdates(store);
        using var process = Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime; long before = process.PrivateMemorySize64;
        var clock = Stopwatch.StartNew(); var result = client.CheckAsync(Program.Version).GetAwaiter().GetResult(); process.Refresh();
        File.WriteAllText(report, JsonSerializer.Serialize(new { status = "passed", phase = result.Phase.ToString(), networkOnly = true, downloads = 0, elapsedMs = clock.ElapsedMilliseconds, cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds, privateDeltaMiB = (process.PrivateMemorySize64 - before) / 1048576.0, metadataBytes = new FileInfo(Path.Combine(root, "update-settings.json")).Length }));
    }
    private static string ReadHelperError() => File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VaultSyncBuild", "update-helper-error.txt")) ? File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VaultSyncBuild", "update-helper-error.txt")) : "no details";
    private static void CleanupBinaryFixtures(string root)
    {
        string parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VaultSyncBuild", "updater-tests");
        if (!Guid.TryParseExact(Path.GetFileName(root), "N", out _) || Directory.GetParent(root)?.FullName != parent) throw new IOException("Cleanup outside update fixture.");
        Walk(root);
        static void Walk(string directory)
        {
            UpdateStore.CheckPath(directory);
            foreach (string file in Directory.GetFileSystemEntries(directory))
            {
                UpdateStore.CheckPath(file);
                if (Directory.Exists(file)) Walk(file);
                else if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            }
        }
    }
    private static Process Start(string exe, params string[] args)
    { var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true }; foreach (string arg in args) start.ArgumentList.Add(arg); return Process.Start(start) ?? throw new IOException("Fixture process not started"); }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Until(Func<bool> predicate, string label, int seconds = 15)
    { var watch = Stopwatch.StartNew(); while (!predicate()) { if (watch.Elapsed > TimeSpan.FromSeconds(seconds)) throw new TimeoutException("Update fixture timeout: " + label); Thread.Sleep(100); } }
    private static bool SandboxRunning(string root)
    {
        foreach (var process in Process.GetProcessesByName("VaultSync"))
        { using (process) { try { if (process.MainModule?.FileName.StartsWith(root, StringComparison.OrdinalIgnoreCase) == true) return true; } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { } } }
        return false;
    }
}
