using System.Security.Cryptography;
using System.Text.Json;

namespace VaultSync.Core;

public sealed class SyncEngine : IDisposable
{
    public SyncConfig Config { get; }
    public StateStore Store { get; }
    private readonly Scanner scanner = new();
    public event Action<SyncEvent>? Activity;
    private void Log(string level, string message, string? path = null) => Activity?.Invoke(new(DateTimeOffset.Now, level, message, path));
    public Action<PlanItem>? BeforeApply { get; set; } // deterministic fault/race injection for tests
    public Action<PlanItem>? BeforePublish { get; set; }
    public SyncEngine(SyncConfig config, string stateRoot)
    {
        Paths.ValidatePair(config, stateRoot); Config = config;
        Store = new StateStore(Path.Combine(stateRoot, config.PairId));
    }
    public static SyncAction Decide(bool known, FileStamp? baseline, FileStamp? local, FileStamp? cloud, bool preferNewest = false)
    {
        if (Scanner.Same(local, cloud)) return SyncAction.Equal;
        if (preferNewest && local != null && cloud != null)
            return local.ModifiedUtcTicks == cloud.ModifiedUtcTicks ? SyncAction.Conflict :
                local.ModifiedUtcTicks > cloud.ModifiedUtcTicks ? SyncAction.Upload : SyncAction.Download;
        if (!known) return local == null ? SyncAction.Download : cloud == null ? SyncAction.Upload : SyncAction.Conflict;
        bool lc = !Scanner.Same(local, baseline), cc = !Scanner.Same(cloud, baseline);
        if (lc && cc) return SyncAction.Conflict;
        if (lc) return local == null ? SyncAction.DeleteCloud : SyncAction.Upload;
        if (cc) return cloud == null ? SyncAction.DeleteLocal : SyncAction.Download;
        return SyncAction.Conflict;
    }
    public SyncPlan Preview(CancellationToken ct = default)
    {
        var l = scanner.Scan(Config.LocalRoot, ct, Config.IncludeWorkspaceLayouts); var c = scanner.Scan(Config.CloudRoot, ct, Config.IncludeWorkspaceLayouts);
        var errors = l.Errors.Concat(c.Errors).ToList(); var baseline = Store.Baseline();
        var blocked = (l.BlockedPaths ?? []).Concat(c.BlockedPaths ?? []).ToList();
        var paths = l.Files.Keys.Concat(c.Files.Keys).Concat(baseline.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
        var items = paths.Where(p => !Scanner.IsInternal(p, Config.IncludeWorkspaceLayouts) && !blocked.Any(b => b.Length == 0 || p.Equals(b, StringComparison.OrdinalIgnoreCase) || p.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))).Select(p =>
        {
            bool known = baseline.TryGetValue(p, out var b); l.Files.TryGetValue(p, out var ls); c.Files.TryGetValue(p, out var cs);
            return new PlanItem(p, Decide(known, b, ls, cs, Config.PreferNewest), ls, cs);
        }).ToList();
        return new(items, errors, !blocked.Contains(""), blocked);
    }
    public string CreateMigrationBackup(SyncPlan plan, CancellationToken ct = default)
    {
        if (!plan.RootsAvailable) throw new IOException("Raiz indisponível; cópia de segurança adiada.");
        string root = Path.Combine(Store.Root, "migration-backup", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(root);
        var copied = new List<object>(); var skipped = new List<string>(plan.Errors);
        Log("INFO", "Cópia de segurança opcional iniciada.");
        foreach (var item in plan.Items)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var side in new[] { ("local", Config.LocalRoot, item.Local), ("icloud", Config.CloudRoot, item.Cloud) })
            {
                if (side.Item3 == null) continue;
                string output = Paths.Resolve(Path.Combine(root, side.Item1), item.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                try
                {
                    var current = Scanner.Read(Paths.Resolve(side.Item2, item.Path));
                    if (current == null) { skipped.Add(item.Path + ": removido durante backup"); continue; }
                    CopyVerified(Paths.Resolve(side.Item2, item.Path), output, current);
                    copied.Add(new { side = side.Item1, item.Path, stamp = current });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (File.Exists(output)) File.Delete(output);
                    skipped.Add(item.Path + ": " + ex.Message); Log("AVISO", "Backup adiou arquivo em uso: " + ex.Message, item.Path);
                }
            }
        }
        File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(new { complete = skipped.Count == 0, copied, skipped }, new JsonSerializerOptions { WriteIndented = true }));
        Store.SetSetting("migration-backup", root); Log("INFO", $"Backup concluído: {copied.Count} cópias verificadas; {skipped.Count} arquivos/leituras adiados. Consulte manifest.json."); return root;
    }
    public void Apply(SyncPlan plan, CancellationToken ct = default)
    {
        if (!plan.RootsAvailable) throw new IOException("Raiz indisponível; operações bloqueadas. " + string.Join("; ", plan.Errors.Take(5)));
        CheckRoots(); RecoverInterrupted();
        foreach (var error in plan.Errors) Log("AVISO", "Arquivo/subpasta adiado; os demais continuam. " + error);
        foreach (var blockedPath in plan.BlockedPaths ?? [])
        {
            if (blockedPath.Length == 0 || Scanner.IsInternal(blockedPath, Config.IncludeWorkspaceLayouts)) continue;
            Store.Queue(blockedPath); Store.Failed(blockedPath, "Leitura adiada: arquivo em uso, indisponível ou pasta não acessível.");
        }
        var baseline = Store.Baseline();
        var conflicts = Store.Conflicts().ToDictionary(c => c.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan.Items)
        {
            ct.ThrowIfCancellationRequested();
            if (conflicts.TryGetValue(item.Path, out var conflict))
            {
                if (item.Action == SyncAction.Equal) { CompleteConvergedConflict(conflict, item.Local); continue; }
                if (conflict.RequiresExplicitResolution) continue;
                if (!Config.PreferNewest || item.Action is not (SyncAction.Upload or SyncAction.Download)) continue;
            }
            if (item.Action == SyncAction.Equal)
            {
                // The scan already observed identical contents. Advancing that observed baseline does
                // not mutate user files, and later edits will be detected against it on the next pass.
                if (baseline.TryGetValue(item.Path, out var previous) && Scanner.Same(previous, item.Local)) Store.CompleteUnchanged(item.Path);
                else Store.Acknowledge(item.Path, item.Local);
                continue;
            }
            Store.Queue(item.Path);
            try { ApplyOne(item); if (conflict != null && !Store.Conflicts().Any(c => c.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase))) CleanupConflict(conflict); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Store.Failed(item.Path, ex.Message); Log("PENDENTE", "Nova tentativa automática: " + ex.Message, item.Path); }
        }
    }
    public void ProcessPaths(IEnumerable<string> relativePaths, CancellationToken ct = default)
    {
        CheckRoots(); RecoverInterrupted(); var baseline = Store.Baseline();
        var conflicted = Store.Conflicts().Select(c => c.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in relativePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            if (Scanner.IsInternal(p, Config.IncludeWorkspaceLayouts)) continue;
            Store.Queue(p);
            try
            {
                var (localPath, cloudPath) = Files(p);
                if (Directory.Exists(localPath) || Directory.Exists(cloudPath))
                {
                    var plan = Preview(ct); Apply(plan, ct);
                    if (!(plan.BlockedPaths ?? []).Any(b => b.Length == 0 || p.Equals(b, StringComparison.OrdinalIgnoreCase) || p.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) Store.CompleteUnchanged(p);
                    continue;
                }
                var l = Scanner.Read(Paths.Resolve(Config.LocalRoot, p)); var c = Scanner.Read(Paths.Resolve(Config.CloudRoot, p));
                bool known = baseline.TryGetValue(p, out var b);
                var action = Decide(known, b, l, c, Config.PreferNewest);
                Conflict? conflict = null;
                if (conflicted.Contains(p))
                {
                    conflict = Store.Conflicts().FirstOrDefault(x => x.Path.Equals(p, StringComparison.OrdinalIgnoreCase));
                    if (conflict != null)
                    {
                        if (action == SyncAction.Equal) { CompleteConvergedConflict(conflict, l); continue; }
                        if (conflict.RequiresExplicitResolution || !Config.PreferNewest || action is not (SyncAction.Upload or SyncAction.Download)) { Store.CompleteUnchanged(p); continue; }
                    }
                }
                ApplyOne(new(p, action, l, c));
                if (conflict != null && !Store.Conflicts().Any(c => c.Path.Equals(p, StringComparison.OrdinalIgnoreCase))) CleanupConflict(conflict);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Store.Failed(p, ex.Message); Log("PENDENTE", "Nova tentativa automática: " + ex.Message, p); }
        }
    }
    public void Retry(CancellationToken ct = default) => ProcessPaths(Store.Due(), ct);
    private bool CompleteConvergedConflict(Conflict conflict, FileStamp? stamp)
    {
        try
        {
            CheckRoots(); var (l, c) = Files(conflict.Path);
            if (!Scanner.Same(Scanner.Read(l), stamp) || !Scanner.Same(Scanner.Read(c), stamp)) throw new IOException("O conteúdo mudou durante a conferência de igualdade.");
            if (conflict.RequiresExplicitResolution && Directory.Exists(conflict.Folder))
            {
                File.WriteAllText(Path.Combine(conflict.Folder, "converged.json"), JsonSerializer.Serialize(new { conflict.Path, hash = stamp?.Hash, verifiedUtc = DateTimeOffset.UtcNow, note = "Conteúdo atual idêntico nas duas pastas. Artefatos de recuperação preservados; nenhum vencedor precisa ser escolhido." }));
                Log("RECUPERAÇÃO", "Conteúdo atual idêntico. Versões de recuperação preservadas em: " + conflict.Folder, conflict.Path);
            }
            Store.Acknowledge(conflict.Path, stamp);
            if (!conflict.RequiresExplicitResolution) CleanupConflict(conflict);
            Log("OK", "Sincronizado: os dois lados têm conteúdo idêntico; conflito encerrado.", conflict.Path); return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Store.Queue(conflict.Path); Store.Failed(conflict.Path, ex.Message); Log("PENDENTE", "Igualdade será conferida novamente: " + ex.Message, conflict.Path); return false; }
    }
    public void RefreshConflictRecords()
    {
        foreach (var conflict in Store.Conflicts())
        {
            if (Scanner.IsInternal(conflict.Path, Config.IncludeWorkspaceLayouts)) continue;
            try
            {
                var (l, c) = Files(conflict.Path); var ls = Scanner.Read(l); var cs = Scanner.Read(c);
                if (Scanner.Same(ls, cs)) CompleteConvergedConflict(conflict, ls);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Log("PENDENTE", "Conferência do conflito adiada: " + ex.Message, conflict.Path); }
        }
    }
    private void CleanupConflict(Conflict conflict)
    {
        if (string.IsNullOrEmpty(conflict.Folder)) return;
        string folder = Paths.Normalize(conflict.Folder), parent = Path.Combine(Store.Root, "conflicts");
        if (Directory.GetParent(folder)?.FullName.Equals(parent, StringComparison.OrdinalIgnoreCase) == true && Guid.TryParseExact(Path.GetFileName(folder), "N", out _))
        { Paths.CheckAncestors(folder); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
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
        var ls = Scanner.Read(l); var cs = Scanner.Read(c);
        if (!Scanner.Same(ls, item.Local) || !Scanner.Same(cs, item.Cloud) ||
            (Config.PreferNewest && item.Local != null && item.Cloud != null &&
             (ls?.ModifiedUtcTicks != item.Local.ModifiedUtcTicks || cs?.ModifiedUtcTicks != item.Cloud.ModifiedUtcTicks)))
            throw new IOException("Arquivo mudou desde a comparação; será conferido novamente: " + item.Path);
    }
    private void ApplyOne(PlanItem item)
    {
        BeforeApply?.Invoke(item); Revalidate(item); var (l, c) = Files(item.Path);
        switch (item.Action)
        {
            case SyncAction.Equal: Store.Acknowledge(item.Path, item.Local); return;
            case SyncAction.Conflict: SaveConflict(item.Path, Config.PreferNewest ? "Datas iguais com conteúdos diferentes, ou exclusão contra edição" : "Edições concorrentes ou divergência inicial"); return;
            case SyncAction.Upload: ReplaceOne(item, l, c, item.Local!, item.Cloud); break;
            case SyncAction.Download: ReplaceOne(item, c, l, item.Cloud!, item.Local); break;
            case SyncAction.DeleteLocal: DeleteOne(item, l, item.Local!); break;
            case SyncAction.DeleteCloud: DeleteOne(item, c, item.Cloud!); break;
        }
        var ls = Scanner.Read(l); var cs = Scanner.Read(c);
        if (!Scanner.Same(ls, cs)) { SaveConflict(item.Path, "Alteração concorrente durante a operação"); return; }
        Store.Acknowledge(item.Path, ls);
        Log("OK", item.Action switch { SyncAction.Upload => "Copiado Local → iCloud", SyncAction.Download => "Copiado iCloud → Local", SyncAction.DeleteLocal => "Exclusão aplicada na pasta local", SyncAction.DeleteCloud => "Exclusão aplicada na pasta iCloud", _ => "Conteúdo verificado" }, item.Path);
    }
    private void ReplaceOne(PlanItem item, string source, string target, FileStamp desired, FileStamp? expectedTarget)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!); Paths.CheckAncestors(target);
        string stage = Path.Combine(Path.GetDirectoryName(target)!, ".megabrainsync-stage-" + Guid.NewGuid().ToString("N"));
        string previous = stage + ".previous";
        var journal = new OperationJournal(item.Path, stage, target, previous, expectedTarget, "replace");
        string record = WriteJournal(journal);
        bool mutationAttempted = false;
        try
        {
            CopyVerified(source, stage, desired); BeforePublish?.Invoke(item); Revalidate(item);
            mutationAttempted = true;
            if (expectedTarget == null) File.Move(stage, target, false);
            else
            {
                File.Replace(stage, target, previous, true);
                // File.Replace atomically captures the bytes actually replaced, including an atomic-save race.
                var replaced = Scanner.Read(previous);
                if (!Scanner.Same(replaced, expectedTarget) || (Config.PreferNewest && replaced?.ModifiedUtcTicks != expectedTarget.ModifiedUtcTicks))
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
            // A busy source or stale preview before publication is a retry, not a conflict.
            // When a replacement failed atomically the stage still exists and no previous bytes exist.
            if (!mutationAttempted || (File.Exists(stage) && !File.Exists(previous)))
            {
                if (File.Exists(stage)) File.Delete(stage);
                File.Delete(record);
            }
            throw;
        }
    }
    private void DeleteOne(PlanItem item, string target, FileStamp expected)
    {
        Revalidate(item);
        string previous = Path.Combine(Path.GetDirectoryName(target)!, ".megabrainsync-delete-" + Guid.NewGuid().ToString("N"));
        var journal = new OperationJournal(item.Path, "", target, previous, expected, "delete");
        string record = WriteJournal(journal);
        try { File.Move(target, previous, false); }
        catch { if (!File.Exists(previous)) File.Delete(record); throw; }
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
            if (entry.Kind == "replace" && File.Exists(entry.Stage) && !File.Exists(entry.Previous))
            {
                // Interrupted preparation: source was never atomically published.
                File.Delete(entry.Stage); File.Delete(path); Store.Queue(entry.Relative, true);
                Log("INFO", "Preparação interrompida retomada sem criar conflito.", entry.Relative); continue;
            }
            SaveConflict(entry.Relative, "Operação interrompida; revise as versões preservadas antes de continuar", File.Exists(entry.Previous) ? entry.Previous : null, requireExplicit: true);
            // A complete staged source is retained with the conflict as an additional recovery artifact.
            var conflict = Store.Conflicts().First(x => x.Path.Equals(entry.Relative, StringComparison.OrdinalIgnoreCase));
            if (File.Exists(entry.Stage)) File.Copy(entry.Stage, Path.Combine(conflict.Folder, "staged.bin"), true);
            if (File.Exists(entry.Stage)) File.Delete(entry.Stage);
            if (File.Exists(entry.Previous)) File.Delete(entry.Previous);
            File.Delete(path);
        }
    }
    private void SaveConflict(string relative, string reason, string? additional = null, bool requireExplicit = false)
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
        Store.SetConflict(new(relative, ls, cs, folder, reason, requireExplicit || additional != null || existing?.RequiresExplicitResolution == true));
        Log("CONFLITO", reason, relative);
    }
    public ComparisonSnapshot? PrepareComparison(Conflict requested)
    {
        CheckRoots();
        var (l, c) = Files(requested.Path);
        var ls = Scanner.Read(l); var cs = Scanner.Read(c);
        var existing = Store.Conflicts().FirstOrDefault(x => x.Path.Equals(requested.Path, StringComparison.OrdinalIgnoreCase));
        if (Scanner.Same(ls, cs))
        {
            if (CompleteConvergedConflict(existing ?? requested, ls)) return null;
            throw new IOException("O arquivo mudou durante a conferência. Tente abrir novamente.");
        }
        if (existing == null || existing.Local != ls || existing.Cloud != cs)
        {
            SaveConflict(requested.Path, existing?.Reason ?? requested.Reason);
            existing = Store.Conflicts().First(x => x.Path.Equals(requested.Path, StringComparison.OrdinalIgnoreCase));
        }
        bool markdown = requested.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
        var left = ComparisonText(l, existing.Local, markdown); var right = ComparisonText(c, existing.Cloud, markdown);
        return new(existing, left.Text, right.Text, markdown && left.Editable && right.Editable);
    }
    private static (string Text, bool Editable) ComparisonText(string path, FileStamp? stamp, bool markdown)
    {
        if (stamp == null) return ("[Arquivo excluído neste lado]", true);
        string description = $"{stamp.Length:N0} bytes\nSHA-256: {stamp.Hash}";
        if (!markdown || stamp.Length > 2 * 1024 * 1024)
            return (description + "\nPrévia textual indisponível. Inspecione as versões preservadas no aplicativo original antes de escolher.", false);
        string text;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false, true), true);
            text = reader.ReadToEnd();
        }
        catch (System.Text.DecoderFallbackException) { return (description + "\nCodificação não suportada na prévia. Inspecione as versões preservadas antes de escolher.", false); }
        if (!Scanner.Same(Scanner.Read(path), stamp)) throw new IOException("O arquivo mudou enquanto a comparação era preparada. Tente abrir novamente.");
        if (text.Length > 250000 || text.Count(ch => ch == '\n') > 5000)
            return (description + "\nNota extensa para a prévia. Inspecione as versões preservadas no aplicativo original antes de escolher.", false);
        return (text, true);
    }
    public void Resolve(Conflict conflict, string choice, string? merged = null)
    {
        CheckRoots(); var (l, c) = Files(conflict.Path);
        var observedL = Scanner.Read(l); var observedC = Scanner.Read(c);
        if (!Scanner.Same(observedL, conflict.Local) || !Scanner.Same(observedC, conflict.Cloud) ||
            (Config.PreferNewest && (observedL?.ModifiedUtcTicks != conflict.Local?.ModifiedUtcTicks || observedC?.ModifiedUtcTicks != conflict.Cloud?.ModifiedUtcTicks)))
        { SaveConflict(conflict.Path, "O arquivo mudou desde que o conflito foi aberto"); throw new IOException("As versões mudaram. Reabra o conflito para comparar novamente."); }
        if (choice is not ("local" or "cloud" or "merge")) throw new ArgumentException("Escolha inválida.");
        SaveConflict(conflict.Path, "Resolução em andamento"); // preserve both until fully acknowledged
        var preserved = Store.Conflicts().First(c => c.Path.Equals(conflict.Path, StringComparison.OrdinalIgnoreCase));
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
        Log("OK", "Conflito resolvido e verificado nas duas pastas.", conflict.Path);
        // Only resolved conflict artifacts are removed. Migration backup is managed separately.
        CleanupConflict(preserved);
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
