using System.Security.Cryptography;
using System.Text.Json;

namespace VaultSync.Core;

public sealed class SyncEngine : IDisposable
{
    public SyncConfig Config { get; }
    public StateStore Store { get; }
    private readonly Scanner scanner = new();
    public Action<PlanItem>? BeforeApply { get; set; } // deterministic fault/race injection for tests
    public SyncEngine(SyncConfig config, string stateRoot)
    {
        Paths.ValidatePair(config, stateRoot); Config = config;
        Store = new StateStore(Path.Combine(stateRoot, config.PairId));
    }
    public static SyncAction Decide(bool known, FileStamp? baseline, FileStamp? local, FileStamp? cloud)
    {
        if (Scanner.Same(local, cloud)) return SyncAction.Equal;
        if (!known) return local == null ? SyncAction.Download : cloud == null ? SyncAction.Upload : SyncAction.Conflict;
        bool lc = !Scanner.Same(local, baseline), cc = !Scanner.Same(cloud, baseline);
        if (lc && cc) return SyncAction.Conflict;
        if (lc) return local == null ? SyncAction.DeleteCloud : SyncAction.Upload;
        if (cc) return cloud == null ? SyncAction.DeleteLocal : SyncAction.Download;
        return SyncAction.Conflict;
    }
    public SyncPlan Preview(CancellationToken ct = default)
    {
        var l = scanner.Scan(Config.LocalRoot, ct); var c = scanner.Scan(Config.CloudRoot, ct);
        var errors = l.Errors.Concat(c.Errors).ToList(); var baseline = Store.Baseline();
        var paths = l.Files.Keys.Concat(c.Files.Keys).Concat(baseline.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
        var items = paths.Select(p =>
        {
            bool known = baseline.TryGetValue(p, out var b); l.Files.TryGetValue(p, out var ls); c.Files.TryGetValue(p, out var cs);
            return new PlanItem(p, Decide(known, b, ls, cs), ls, cs);
        }).ToList();
        return new(items, errors);
    }
    public string CreateMigrationBackup(SyncPlan plan, CancellationToken ct = default)
    {
        if (!plan.Complete) throw new IOException("Leitura incompleta; cópia de segurança e início bloqueados.");
        string root = Path.Combine(Store.Root, "migration-backup", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(root);
        foreach (var item in plan.Items)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var side in new[] { ("local", Config.LocalRoot, item.Local), ("icloud", Config.CloudRoot, item.Cloud) })
            {
                if (side.Item3 == null) continue;
                string output = Paths.Resolve(Path.Combine(root, side.Item1), item.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                CopyVerified(Paths.Resolve(side.Item2, item.Path), output, side.Item3);
            }
        }
        File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));
        Store.SetSetting("migration-backup", root); return root;
    }
    public void Apply(SyncPlan plan, CancellationToken ct = default)
    {
        if (!plan.Complete) throw new IOException("Leitura incompleta; operações bloqueadas. " + string.Join("; ", plan.Errors.Take(5)));
        CheckRoots(); RecoverInterrupted();
        var baseline = Store.Baseline();
        var conflicts = Store.Conflicts().Select(c => c.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan.Items)
        {
            ct.ThrowIfCancellationRequested();
            if (conflicts.Contains(item.Path)) continue;
            if (item.Action == SyncAction.Equal)
            {
                // The scan already observed identical contents. Advancing that observed baseline does
                // not mutate user files, and later edits will be detected against it on the next pass.
                if (baseline.TryGetValue(item.Path, out var previous) && Scanner.Same(previous, item.Local)) Store.CompleteUnchanged(item.Path);
                else Store.Acknowledge(item.Path, item.Local);
                continue;
            }
            Store.Queue(item.Path);
            try { ApplyOne(item); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Store.Failed(item.Path, ex.Message); }
        }
    }
    public void ProcessPaths(IEnumerable<string> relativePaths, CancellationToken ct = default)
    {
        CheckRoots(); RecoverInterrupted(); var baseline = Store.Baseline();
        var conflicted = Store.Conflicts().Select(c => c.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in relativePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            if (Scanner.IsInternal(p) || conflicted.Contains(p)) continue;
            Store.Queue(p);
            try
            {
                var l = Scanner.Read(Paths.Resolve(Config.LocalRoot, p)); var c = Scanner.Read(Paths.Resolve(Config.CloudRoot, p));
                bool known = baseline.TryGetValue(p, out var b);
                ApplyOne(new(p, Decide(known, b, l, c), l, c));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Store.Failed(p, ex.Message); }
        }
    }
    public void Retry(CancellationToken ct = default) => ProcessPaths(Store.Due(), ct);
    private void CheckRoots()
    {
        Paths.ValidatePair(Config, Directory.GetParent(Store.Root)!.FullName);
        foreach (var root in new[] { Config.LocalRoot, Config.CloudRoot })
        {
            if (!Directory.Exists(root)) throw new IOException("Raiz indisponível; nada será excluído: " + root);
            var attrs = File.GetAttributes(root);
            if (Paths.NeedsDownload(attrs)) throw new IOException("Raiz ainda não disponível localmente: " + root);
            // Force an actual enumeration; Directory.Exists can hide permission errors.
            _ = Directory.EnumerateFileSystemEntries(root).FirstOrDefault();
        }
    }
    private (string l, string c) Files(string relative) => (Paths.Resolve(Config.LocalRoot, relative), Paths.Resolve(Config.CloudRoot, relative));
    private void Revalidate(PlanItem item)
    {
        CheckRoots(); var (l, c) = Files(item.Path);
        if (!Scanner.Same(Scanner.Read(l), item.Local) || !Scanner.Same(Scanner.Read(c), item.Cloud))
            throw new IOException("Arquivo mudou desde a comparação; será conferido novamente: " + item.Path);
    }
    private void ApplyOne(PlanItem item)
    {
        BeforeApply?.Invoke(item); Revalidate(item); var (l, c) = Files(item.Path);
        switch (item.Action)
        {
            case SyncAction.Equal: Store.Acknowledge(item.Path, item.Local); return;
            case SyncAction.Conflict: SaveConflict(item.Path, "Edições concorrentes ou divergência inicial"); return;
            case SyncAction.Upload: ReplaceOne(item, l, c, item.Local!, item.Cloud); break;
            case SyncAction.Download: ReplaceOne(item, c, l, item.Cloud!, item.Local); break;
            case SyncAction.DeleteLocal: DeleteOne(item, l, item.Local!); break;
            case SyncAction.DeleteCloud: DeleteOne(item, c, item.Cloud!); break;
        }
        var ls = Scanner.Read(l); var cs = Scanner.Read(c);
        if (!Scanner.Same(ls, cs)) { SaveConflict(item.Path, "Alteração concorrente durante a operação"); return; }
        Store.Acknowledge(item.Path, ls);
    }
    private void ReplaceOne(PlanItem item, string source, string target, FileStamp desired, FileStamp? expectedTarget)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!); Paths.CheckAncestors(target);
        string stage = Path.Combine(Path.GetDirectoryName(target)!, ".megabrainsync-stage-" + Guid.NewGuid().ToString("N"));
        string previous = stage + ".previous";
        var journal = new OperationJournal(item.Path, stage, target, previous, expectedTarget, "replace");
        string record = WriteJournal(journal);
        try
        {
            CopyVerified(source, stage, desired); Revalidate(item);
            if (expectedTarget == null) File.Move(stage, target, false);
            else
            {
                File.Replace(stage, target, previous, true);
                // File.Replace atomically captures the bytes actually replaced, including an atomic-save race.
                var replaced = Scanner.Read(previous);
                if (!Scanner.Same(replaced, expectedTarget))
                {
                    SaveConflict(item.Path, "Outra aplicação substituiu o arquivo durante a gravação", previous);
                    throw new IOException("Versão concorrente preservada no conflito: " + item.Path);
                }
            }
            if (!Scanner.Same(Scanner.Read(target), desired)) throw new IOException("Verificação da cópia falhou: " + item.Path);
            if (File.Exists(previous)) File.Delete(previous);
            File.Delete(record);
        }
        catch
        {
            // Keep the write-ahead journal and any replaced bytes; recovery blocks this file after restart.
            throw;
        }
    }
    private void DeleteOne(PlanItem item, string target, FileStamp expected)
    {
        Revalidate(item);
        string previous = Path.Combine(Path.GetDirectoryName(target)!, ".megabrainsync-delete-" + Guid.NewGuid().ToString("N"));
        var journal = new OperationJournal(item.Path, "", target, previous, expected, "delete");
        string record = WriteJournal(journal);
        File.Move(target, previous, false);
        if (!Scanner.Same(Scanner.Read(previous), expected))
        {
            if (!File.Exists(target)) File.Move(previous, target, false);
            SaveConflict(item.Path, "Edição concorrente com exclusão", File.Exists(previous) ? previous : null);
            File.Delete(record); throw new IOException("Exclusão interrompida: versão concorrente preservada.");
        }
        File.Delete(previous); File.Delete(record);
    }
    private sealed record OperationJournal(string Relative, string Stage, string Target, string Previous, FileStamp? Expected, string Kind);
    private string WriteJournal(OperationJournal entry)
    {
        string dir = Path.Combine(Store.Root, "operations"); Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".json");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        JsonSerializer.Serialize(stream, entry); stream.Flush(true); return path;
    }
    private void RecoverInterrupted()
    {
        string dir = Path.Combine(Store.Root, "operations"); if (!Directory.Exists(dir)) return;
        foreach (var path in Directory.GetFiles(dir, "*.json"))
        {
            var entry = JsonSerializer.Deserialize<OperationJournal>(File.ReadAllText(path)) ?? throw new IOException("Registro de recuperação inválido.");
            var (l, c) = Files(entry.Relative);
            if (!entry.Target.Equals(l, StringComparison.OrdinalIgnoreCase) && !entry.Target.Equals(c, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Registro de operação fora das raízes; recuperação bloqueada.");
            var parent = Path.GetDirectoryName(entry.Target)!;
            foreach (var temp in new[] { entry.Stage, entry.Previous }.Where(p => p.Length > 0))
                if (!Path.GetDirectoryName(temp)!.Equals(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(temp).StartsWith(".megabrainsync-", StringComparison.Ordinal))
                    throw new IOException("Temporário inválido no registro de recuperação.");
            SaveConflict(entry.Relative, "Operação interrompida; revise antes de continuar", File.Exists(entry.Previous) ? entry.Previous : null);
            // A complete staged source is retained with the conflict as an additional recovery artifact.
            var conflict = Store.Conflicts().First(x => x.Path.Equals(entry.Relative, StringComparison.OrdinalIgnoreCase));
            if (File.Exists(entry.Stage)) File.Copy(entry.Stage, Path.Combine(conflict.Folder, "staged.bin"), true);
            if (File.Exists(entry.Stage)) File.Delete(entry.Stage);
            if (File.Exists(entry.Previous)) File.Delete(entry.Previous);
            File.Delete(path);
        }
    }
    private void SaveConflict(string relative, string reason, string? additional = null)
    {
        var (l, c) = Files(relative);
        var ls = Scanner.Read(l); var cs = Scanner.Read(c);
        var existing = Store.Conflicts().FirstOrDefault(x => x.Path.Equals(relative, StringComparison.OrdinalIgnoreCase));
        string folder = existing?.Folder ?? Path.Combine(Store.Root, "conflicts", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string revision = Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff")); Directory.CreateDirectory(revision);
        if (ls != null) CopyVerified(l, Path.Combine(revision, "local.bin"), ls);
        if (cs != null) CopyVerified(c, Path.Combine(revision, "icloud.bin"), cs);
        if (additional != null) File.Copy(additional, Path.Combine(revision, "interrupted-version.bin"), false);
        File.WriteAllText(Path.Combine(revision, "metadata.json"), JsonSerializer.Serialize(new { relative, local = ls, cloud = cs, machine = Environment.MachineName, reason }));
        Store.SetConflict(new(relative, ls, cs, folder, reason));
    }
    public void Resolve(Conflict conflict, string choice, string? merged = null)
    {
        CheckRoots(); var (l, c) = Files(conflict.Path);
        if (!Scanner.Same(Scanner.Read(l), conflict.Local) || !Scanner.Same(Scanner.Read(c), conflict.Cloud))
        { SaveConflict(conflict.Path, "O arquivo mudou desde que o conflito foi aberto"); throw new IOException("As versões mudaram. Reabra o conflito para comparar novamente."); }
        if (choice is not ("local" or "cloud" or "merge")) throw new ArgumentException("Escolha inválida.");
        SaveConflict(conflict.Path, "Resolução em andamento"); // preserve both until fully acknowledged
        var item = new PlanItem(conflict.Path, SyncAction.Conflict, conflict.Local, conflict.Cloud);
        if (choice == "merge")
        {
            if (!conflict.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) throw new IOException("Combinação disponível apenas para Markdown.");
            string temp = Path.Combine(Store.Root, "merge-" + Guid.NewGuid().ToString("N")); File.WriteAllText(temp, merged ?? "");
            try
            {
                var stamp = Scanner.Read(temp)!;
                ReplaceOne(item, temp, l, stamp, conflict.Local);
                var second = item with { Local = stamp };
                ReplaceOne(second, temp, c, stamp, conflict.Cloud);
            }
            finally { File.Delete(temp); }
        }
        else
        {
            var chosen = choice == "local" ? conflict.Local : conflict.Cloud;
            string source = choice == "local" ? l : c, target = choice == "local" ? c : l;
            var other = choice == "local" ? conflict.Cloud : conflict.Local;
            if (chosen == null) { if (other != null) DeleteOne(item, target, other); }
            else ReplaceOne(item, source, target, chosen, other);
        }
        var current = Scanner.Read(l);
        if (!Scanner.Same(current, Scanner.Read(c))) throw new IOException("Nova alteração durante resolução; conflito continua preservado.");
        Store.Acknowledge(conflict.Path, current);
        // Only resolved conflict artifacts are removed. Migration backup is managed separately.
        if (Paths.Within(conflict.Folder, Path.Combine(Store.Root, "conflicts"))) Directory.Delete(conflict.Folder, true);
    }
    private static void CopyVerified(string source, string target, FileStamp expected)
    {
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.SequentialScan))
        using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.WriteThrough))
        { input.CopyTo(output); output.Flush(true); }
        File.SetLastWriteTimeUtc(target, new DateTime(expected.ModifiedUtcTicks, DateTimeKind.Utc));
        if (!Scanner.Same(Scanner.Read(target), expected)) throw new IOException("Arquivo mudou durante a cópia: " + source);
    }
    public void Dispose() => Store.Dispose();
}
