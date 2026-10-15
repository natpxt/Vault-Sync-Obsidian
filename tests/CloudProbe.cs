using System.Diagnostics;
using System.Text.Json;
using VaultSync.Core;

internal static class CloudProbe
{
    public static void Run(string cloudParent, string report)
    {
        string cloud = Path.Combine(Paths.Normalize(cloudParent), "VaultSync-MVP-Test-" + Guid.NewGuid().ToString("N"));
        string localBase = Path.Combine(Path.GetTempPath(), "VaultSync-provider-" + Guid.NewGuid().ToString("N")), local = Path.Combine(localBase, "local");
        if (!Directory.Exists(cloudParent) || Directory.Exists(cloud)) throw new IOException("Selecione uma raiz iCloud existente para criar uma fixture isolada.");
        Directory.CreateDirectory(cloud); Directory.CreateDirectory(local);
        var log = new ActivityLog(Path.Combine(localBase, "state"));
        var engine = new SyncEngine(new(local, cloud), Path.Combine(localBase, "state"));
        var results = new List<string>(); var warnings = new List<string>();
        using (var worker = new SyncWorker(engine, log))
        {
            worker.Start();
            File.WriteAllText(Path.Combine(cloud, "cloud.md"), "synthetic cloud note"); Until(() => File.Exists(Path.Combine(local, "cloud.md"))); Settled(); results.Add("icloud-to-local-create");
            File.WriteAllText(Path.Combine(local, "local.md"), "synthetic local note"); Until(() => File.Exists(Path.Combine(cloud, "local.md"))); Settled(); results.Add("local-to-icloud-create");
            File.WriteAllText(Path.Combine(cloud, "cloud.md"), "cloud edit"); Until(() => Read(Path.Combine(local, "cloud.md")) == "cloud edit"); Settled(); results.Add("icloud-to-local-edit");
            File.WriteAllText(Path.Combine(local, "local.md"), "local edit"); Until(() => Read(Path.Combine(cloud, "local.md")) == "local edit"); Settled(); results.Add("local-to-icloud-edit");
            File.Move(Path.Combine(cloud, "cloud.md"), Path.Combine(cloud, "renamed.md")); Until(() => File.Exists(Path.Combine(local, "renamed.md"))); Settled(); results.Add("renamed-file-copied");
            if (File.Exists(Path.Combine(cloud, "cloud.md")))
            {
                var attrs = File.GetAttributes(Path.Combine(cloud, "cloud.md"));
                if (Paths.NeedsDownload(attrs)) warnings.Add("iCloud recreated an unavailable placeholder for the old name; deletion remains deferred, not falsely treated as absent.");
                else Until(() => !File.Exists(Path.Combine(local, "cloud.md")));
            }
            else Until(() => !File.Exists(Path.Combine(local, "cloud.md")));
            File.WriteAllText(Path.Combine(cloud, "after-pending.md"), "still syncing"); Until(() => File.Exists(Path.Combine(local, "after-pending.md"))); Settled(); results.Add("healthy-file-after-provider-pending");
            File.Delete(Path.Combine(local, "local.md"));
            bool deleted = SpinWait.SpinUntil(() => !File.Exists(Path.Combine(cloud, "local.md")), 10000); Settled();
            if (deleted) results.Add("deletion-observed"); else warnings.Add("Deletion awaits the iCloud provider; inspect the persistent pending log.");
            var status = worker.GetStatusAsync().GetAwaiter().GetResult();
            if (status.Conflicts.Count != 0) throw new IOException("Probe de arquivos sem edições concorrentes gerou conflito indevido.");
            if (status.Pending.Count > 0) warnings.Add($"{status.Pending.Count} provider-related pending entries were preserved for retry.");
            void Settled() { _ = worker.GetStatusAsync().GetAwaiter().GetResult(); Thread.Sleep(1100); }
        }
        string outcome = warnings.Count == 0 ? "passed" : "passed-with-provider-pending";
        File.WriteAllText(report, JsonSerializer.Serialize(new { status = outcome, environment = "Real iCloud Windows filesystem provider; synthetic isolated notes; remote delivery not measured", results, warnings }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { status = outcome, results, warnings, fixture = cloud }));
        // Remove only the exclusively created synthetic fixture after verifying its parent/name.
        if (Directory.GetParent(cloud)?.FullName.Equals(Paths.Normalize(cloudParent), StringComparison.OrdinalIgnoreCase) == true && Path.GetFileName(cloud).StartsWith("VaultSync-MVP-Test-", StringComparison.Ordinal))
        { Paths.CheckAncestors(cloud); Directory.Delete(cloud, true); }
    }
    private static string? Read(string file) { try { return File.ReadAllText(file); } catch (IOException) { return null; } }
    private static void Until(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew(); while (!condition()) { if (watch.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("O provedor não convergiu durante o teste."); Thread.Sleep(100); }
    }
}
