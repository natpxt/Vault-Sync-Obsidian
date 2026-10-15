using System.Diagnostics;
using System.Text.Json;
using VaultSync.Core;

internal static class Performance
{
    public static void Run(string report)
    {
        string root = Path.Combine(Path.GetTempPath(), "VaultSync-benchmark-" + Guid.NewGuid().ToString("N"));
        string local = Path.Combine(root, "local"), cloud = Path.Combine(root, "cloud"); Directory.CreateDirectory(local); Directory.CreateDirectory(cloud);
        for (int i = 0; i < 500; i++) File.WriteAllText(Path.Combine(local, $"nota-{i:D4}.md"), "# Nota\n" + new string('a', 2048));
        using var engine = new SyncEngine(new(local, cloud), Path.Combine(root, "state"));
        var watch = Stopwatch.StartNew(); var initial = engine.Preview(); double previewMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart(); engine.Apply(initial); double initialMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart(); engine.Apply(engine.Preview()); double reconciliationMs = watch.Elapsed.TotalMilliseconds;
        using var worker = new SyncWorker(new SyncEngine(new(local, cloud), Path.Combine(root, "state")));
        using var settled = new ManualResetEventSlim(); worker.Status += _ => settled.Set(); worker.Start();
        if (!settled.Wait(TimeSpan.FromSeconds(45))) throw new IOException("A conferência inicial não terminou antes da amostra de repouso.");
        _ = worker.GetStatusAsync().GetAwaiter().GetResult();
        Thread.Sleep(1500);
        var process = Process.GetCurrentProcess(); process.Refresh(); var cpu = process.TotalProcessorTime; watch.Restart(); Thread.Sleep(6000); process.Refresh();
        double idleCpu = (process.TotalProcessorTime - cpu).TotalMilliseconds / watch.Elapsed.TotalMilliseconds * 100 / Environment.ProcessorCount;
        long workingSet = process.WorkingSet64;
        watch.Restart(); File.WriteAllText(Path.Combine(local, "nota-0000.md"), "# Editada\nOlá");
        bool delivered = SpinWait.SpinUntil(() => { try { return File.ReadAllText(Path.Combine(cloud, "nota-0000.md")) == "# Editada\nOlá"; } catch (IOException) { return false; } }, 10000);
        double eventLatencyMs = watch.Elapsed.TotalMilliseconds;
        var result = new { files = 500, bytesPerFile = 2055, previewMs, initialMs, reconciliationMs, idleSampleSeconds = 6, idleCpuPercentMachine = idleCpu, workingSetMiB = workingSet / 1048576.0, eventLatencyMs, delivered, environment = ".NET console worker, NTFS temporary folders; excludes iCloud and WPF" };
        File.WriteAllText(report, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(JsonSerializer.Serialize(result));
        if (!delivered) throw new IOException("Monitor não entregou a edição no benchmark.");
    }
}
