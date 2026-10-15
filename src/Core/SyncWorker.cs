using System.Collections.Concurrent;

namespace VaultSync.Core;

public sealed record WorkerStatus(string Message, List<Conflict> Conflicts, List<string> Pending,
    bool Running = true, bool Paused = false, DateTimeOffset? LastCheck = null, int WatchingRoots = 0, string State = "Monitorando");

public sealed class SyncWorker : IDisposable
{
    private readonly SyncEngine engine;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ConcurrentDictionary<string, DateTime> dirty = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly CancellationTokenSource stop = new();
    private readonly ActivityLog? log;
    private Task? loop;
    private volatile bool fullScan;
    private volatile bool reattach;
    private DateTime lastFull = DateTime.MinValue;
    private DateTimeOffset? lastCheck;
    private string message = "Aguardando primeira conferência";
    private string state = "Iniciando";
    private int disposed;
    public bool Paused { get; private set; }
    public event Action<WorkerStatus>? Status;
    public SyncWorker(SyncEngine engine, ActivityLog? log = null)
    {
        this.engine = engine; this.log = log;
        if (log != null) engine.Activity += log.Write;
    }
    public void Start()
    {
        if (loop != null) throw new InvalidOperationException("Monitor já iniciado.");
        fullScan = true; log?.Write("INFO", "Monitor iniciado. Observando alterações Local ↔ iCloud.");
        loop = Task.Run(Run);
    }
    private void EnsureWatchers()
    {
        if (reattach) { foreach (var old in watchers) old.Dispose(); watchers.Clear(); reattach = false; }
        foreach (var missing in watchers.Where(w => !Directory.Exists(w.Path)).ToList())
        { missing.Dispose(); watchers.Remove(missing); fullScan = true; }
        foreach (var root in new[] { engine.Config.LocalRoot, engine.Config.CloudRoot })
        {
            if (!Directory.Exists(root) || watchers.Any(w => w.Path.Equals(root, StringComparison.OrdinalIgnoreCase))) continue;
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size, InternalBufferSize = 65536
            };
            watcher.Changed += (_, e) => { if (!Directory.Exists(e.FullPath)) Changed(root, e.FullPath); };
            watcher.Created += (_, e) => { if (Ignored(root, e.FullPath)) return; if (Directory.Exists(e.FullPath)) fullScan = true; else Changed(root, e.FullPath); };
            watcher.Deleted += (_, e) => { if (Ignored(root, e.FullPath)) return; Changed(root, e.FullPath); fullScan = true; };
            watcher.Renamed += (_, e) => { if (Ignored(root, e.OldFullPath) && Ignored(root, e.FullPath)) return; Changed(root, e.OldFullPath); Changed(root, e.FullPath); if (Directory.Exists(e.FullPath)) fullScan = true; };
            watcher.Error += (_, _) => { fullScan = true; reattach = true; log?.Write("AVISO", "Monitor perdeu eventos; conferência completa solicitada."); };
            watcher.EnableRaisingEvents = true; watchers.Add(watcher); fullScan = true;
        }
    }
    private bool Ignored(string root, string full) => Scanner.IsInternal(Path.GetRelativePath(root, full), engine.Config.IncludeWorkspaceLayouts);
    private void Changed(string root, string full)
    {
        string relative = Path.GetRelativePath(root, full);
        if (!Scanner.IsInternal(relative, engine.Config.IncludeWorkspaceLayouts)) dirty[relative] = DateTime.UtcNow;
    }
    public void Pause(bool pause)
    {
        Paused = pause; if (!pause) fullScan = true;
        log?.Write("INFO", pause ? "Monitor pausado pelo usuário." : "Monitor retomado pelo usuário.");
    }
    public void RequestScan() { fullScan = true; log?.Write("INFO", "Conferência manual solicitada."); }
    private WorkerStatus Snapshot() => new(message,
        engine.Store.Conflicts().Where(c => !Scanner.IsInternal(c.Path, engine.Config.IncludeWorkspaceLayouts)).ToList(),
        engine.Store.PendingErrors(), !stop.IsCancellationRequested, Paused, lastCheck, watchers.Count, Paused ? "Pausado" : state);
    private void Publish() => Status?.Invoke(Snapshot());
    private async Task Run()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stop.Token).ConfigureAwait(false))
            {
                await gate.WaitAsync(stop.Token).ConfigureAwait(false);
                try
                {
                    if (Paused) { state = "Pausado"; message = "O monitor está pausado. Nenhuma alteração será copiada."; Publish(); continue; }
                    EnsureWatchers();
                    var due = dirty.Where(p => DateTime.UtcNow - p.Value >= TimeSpan.FromSeconds(1)).ToArray();
                    bool scan = fullScan || DateTime.UtcNow - lastFull >= TimeSpan.FromMinutes(5);
                    if (scan)
                    {
                        fullScan = false; state = "Conferindo"; message = "Conferindo arquivos; o monitor está ativo."; Publish();
                        log?.Write("INFO", "Conferência completa iniciada.");
                        var plan = engine.Preview(stop.Token);
                        if (!plan.RootsAvailable) throw new IOException("Uma das pastas está indisponível. Nenhuma exclusão será aplicada. " + string.Join(" | ", plan.Errors.Take(2)));
                        engine.Apply(plan, stop.Token); lastFull = DateTime.UtcNow; lastCheck = DateTimeOffset.Now;
                        log?.Write("INFO", $"Conferência concluída: {plan.Items.Count} arquivos; {plan.Errors.Count} leituras adiadas.");
                    }
                    else if (due.Length > 0) engine.ProcessPaths(due.Select(x => x.Key), stop.Token);
                    foreach (var item in due) ((ICollection<KeyValuePair<string, DateTime>>)dirty).Remove(item);
                    if (engine.Store.Due().Count > 0) engine.Retry(stop.Token);
                    if (due.Length > 0) lastCheck = DateTimeOffset.Now;
                    state = "Monitorando"; message = "Ativo · aguardando alterações nas duas pastas."; Publish();
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    state = "Aguardando acesso"; message = "Monitor ativo, nova tentativa automática: " + ex.Message;
                    log?.Write("PENDENTE", ex.Message); lastFull = DateTime.UtcNow; Publish();
                }
                finally { gate.Release(); }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
    public async Task ResolveAsync(Conflict conflict, string choice, string? merge)
    {
        await gate.WaitAsync();
        try { await Task.Run(() => engine.Resolve(conflict, choice, merge)); message = "Conflito resolvido e verificado."; Publish(); }
        finally { gate.Release(); }
    }
    public async Task<ComparisonSnapshot?> PrepareComparisonAsync(Conflict conflict)
    {
        await gate.WaitAsync();
        try { return await Task.Run(() => engine.PrepareComparison(conflict)); }
        finally { gate.Release(); }
    }
    public async Task<WorkerStatus> GetStatusAsync(bool refreshConflicts = false)
    {
        await gate.WaitAsync(); try { if (refreshConflicts) await Task.Run(engine.RefreshConflictRecords); return Snapshot(); } finally { gate.Release(); }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel();
        try { loop?.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        foreach (var watcher in watchers) watcher.Dispose();
        log?.Write("INFO", "Monitor parado. Nenhum arquivo será sincronizado até iniciar novamente.");
        if (log != null) engine.Activity -= log.Write;
        engine.Dispose(); stop.Dispose(); gate.Dispose();
    }
}
