using System.Collections.Concurrent;

namespace VaultSync.Core;

public sealed record WorkerStatus(string Message, List<Conflict> Conflicts, List<string> Pending);
public sealed class SyncWorker : IDisposable
{
    private readonly SyncEngine engine;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ConcurrentDictionary<string, DateTime> dirty = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly CancellationTokenSource stop = new();
    private Task? loop;
    private volatile bool fullScan;
    private volatile bool reattach;
    private bool completeRead = true;
    private DateTime lastFull = DateTime.UtcNow;
    public bool Paused { get; private set; }
    public event Action<WorkerStatus>? Status;
    public SyncWorker(SyncEngine engine) => this.engine = engine;
    public void Start()
    {
        fullScan = true;
        loop = Task.Run(Run);
    }
    private void EnsureWatchers()
    {
        if (reattach)
        {
            foreach (var old in watchers) old.Dispose(); watchers.Clear(); reattach = false;
        }
        foreach (var missing in watchers.Where(w => !Directory.Exists(w.Path)).ToList())
        { missing.Dispose(); watchers.Remove(missing); fullScan = true; }
        foreach (var root in new[] { engine.Config.LocalRoot, engine.Config.CloudRoot })
        {
            if (!Directory.Exists(root) || watchers.Any(w => w.Path.Equals(root, StringComparison.OrdinalIgnoreCase))) continue;
            var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size, InternalBufferSize = 65536 };
            watcher.Changed += (_, e) => Changed(root, e.FullPath);
            watcher.Created += (_, e) => { if (Directory.Exists(e.FullPath)) fullScan = true; else Changed(root, e.FullPath); };
            watcher.Deleted += (_, e) => { Changed(root, e.FullPath); fullScan = true; };
            watcher.Renamed += (_, e) => { Changed(root, e.OldFullPath); Changed(root, e.FullPath); fullScan = true; };
            watcher.Error += (_, _) => { fullScan = true; reattach = true; };
            watcher.EnableRaisingEvents = true; watchers.Add(watcher);
            fullScan = true;
        }
    }
    private void Changed(string root, string full)
    {
        string relative = Path.GetRelativePath(root, full);
        if (!Scanner.IsInternal(relative)) dirty[relative] = DateTime.UtcNow;
    }
    public void Pause(bool pause) { Paused = pause; if (!pause) fullScan = true; }
    public void RequestScan() => fullScan = true;
    private void Report(string message) => Status?.Invoke(new(message, engine.Store.Conflicts(), engine.Store.PendingErrors()));
    private async Task Run()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stop.Token).ConfigureAwait(false))
        {
            if (Paused) continue;
            await gate.WaitAsync(stop.Token).ConfigureAwait(false);
            try
            {
                EnsureWatchers();
                var due = dirty.Where(p => DateTime.UtcNow - p.Value >= TimeSpan.FromSeconds(1)).ToArray();
                bool scan = fullScan || DateTime.UtcNow - lastFull >= TimeSpan.FromMinutes(5);
                if (scan)
                {
                    fullScan = false;
                    var plan = engine.Preview(stop.Token);
                    completeRead = plan.Complete;
                    if (!plan.Complete) { Report("Conferência incompleta; operações bloqueadas. " + string.Join(" | ", plan.Errors.Take(3))); lastFull = DateTime.UtcNow; continue; }
                    engine.Apply(plan, stop.Token); lastFull = DateTime.UtcNow;
                }
                else if (!completeRead) continue;
                else if (due.Length > 0) engine.ProcessPaths(due.Select(x => x.Key), stop.Token);
                foreach (var item in due) ((ICollection<KeyValuePair<string, DateTime>>)dirty).Remove(item);
                if (engine.Store.Due().Count > 0) engine.Retry(stop.Token);
                if (scan || due.Length > 0) Report("Conferência concluída entre as pastas deste PC. Entrega à nuvem depende do iCloud.");
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex) { Report("Aguardando nova tentativa: " + ex.Message); }
            finally { gate.Release(); }
        }
    }
    public async Task ResolveAsync(Conflict conflict, string choice, string? merge)
    {
        await gate.WaitAsync();
        try { await Task.Run(() => engine.Resolve(conflict, choice, merge)); Report("Conflito resolvido e conteúdo verificado nas duas pastas."); }
        finally { gate.Release(); }
    }
    public async Task<WorkerStatus> GetStatusAsync()
    {
        await gate.WaitAsync();
        try { return new(Paused ? "Pausado" : "Monitorando alterações", engine.Store.Conflicts(), engine.Store.PendingErrors()); }
        finally { gate.Release(); }
    }
    public void Dispose()
    {
        stop.Cancel(); foreach (var watcher in watchers) watcher.Dispose();
        try { loop?.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        engine.Dispose(); stop.Dispose(); gate.Dispose();
    }
}
