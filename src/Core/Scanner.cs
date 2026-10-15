using System.Security.Cryptography;

namespace VaultSync.Core;

public sealed class Scanner
{
    public static bool Same(FileStamp? a, FileStamp? b) => a?.Hash == b?.Hash;
    public static bool IsInternal(string relative, bool includeWorkspaceLayouts = false)
    {
        var parts = relative.Split('/', '\\');
        return parts.Any(p => p.StartsWith(".megabrainsync-", StringComparison.OrdinalIgnoreCase) || p.Equals("sync.ffs_db", StringComparison.OrdinalIgnoreCase))
            || (!includeWorkspaceLayouts && parts.Length == 2 && parts[0].Equals(".obsidian", StringComparison.OrdinalIgnoreCase)
                && parts[1].StartsWith("workspace", StringComparison.OrdinalIgnoreCase) && parts[1].Contains(".json", StringComparison.OrdinalIgnoreCase));
    }

    public static FileStamp? Read(string fullPath)
    {
        Paths.CheckAncestors(fullPath);
        var attrs = Paths.TryAttributes(fullPath);
        if (attrs == null) return null;
        if (attrs.Value.HasFlag(FileAttributes.Directory)) throw new IOException("Uma pasta ocupa o lugar de um arquivo: " + fullPath);
        if (Paths.NeedsDownload(attrs.Value)) throw new IOException("Baixe e mantenha neste dispositivo: " + fullPath);
        var before = new FileInfo(fullPath);
        long length = before.Length, ticks = before.LastWriteTimeUtc.Ticks;
        // Deny concurrent writers while hashing. Never treat a sharing violation as absence.
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.SequentialScan);
        string hash = Convert.ToHexString(SHA256.HashData(stream));
        var after = new FileInfo(fullPath);
        if (length != after.Length || ticks != after.LastWriteTimeUtc.Ticks) throw new IOException("Arquivo mudou durante a leitura: " + fullPath);
        return new(hash, length, ticks);
    }
    public TreeSnapshot Scan(string root, CancellationToken cancellationToken = default, bool includeWorkspaceLayouts = false)
    {
        var files = new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase); var errors = new List<string>(); var blocked = new List<string>();
        try
        {
            Paths.CheckAncestors(root);
            if (!Directory.Exists(root)) throw new IOException("Raiz indisponível: " + root);
            Walk(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add(ex.Message); blocked.Add(""); }
        return new(files, errors, blocked);

        void Walk(string dir)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var full in Directory.EnumerateFileSystemEntries(dir))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relative = Path.GetRelativePath(root, full);
                    if (IsInternal(relative, includeWorkspaceLayouts)) continue;
                    try
                    {
                        Paths.CheckAncestors(full);
                        var attr = File.GetAttributes(full);
                        if (Paths.NeedsDownload(attr)) throw new IOException("Baixe e mantenha neste dispositivo: " + full);
                        if (attr.HasFlag(FileAttributes.Directory)) Walk(full);
                        else if (Read(full) is { } stamp) files.Add(relative, stamp);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { errors.Add(relative + ": " + ex.Message); blocked.Add(relative); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add(ex.Message); blocked.Add(dir.Equals(root, StringComparison.OrdinalIgnoreCase) ? "" : Path.GetRelativePath(root, dir)); }
        }
    }
    public static ValidationReport Validate(SyncConfig config, string stateRoot, bool probeWrite)
    {
        var errors = new List<string>(); var warnings = new List<string>();
        try
        {
            Paths.ValidatePair(config, stateRoot);
            foreach (var root in new[] { config.LocalRoot, config.CloudRoot })
            {
                if (!Directory.Exists(root)) { errors.Add("Crie/selecione uma pasta existente: " + root); continue; }
                _ = Directory.EnumerateFileSystemEntries(root).FirstOrDefault();
                if (probeWrite)
                {
                    string probe = Path.Combine(root, ".megabrainsync-probe-" + Guid.NewGuid().ToString("N"));
                    try { using var test = new FileStream(probe, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose); test.WriteByte(1); test.Flush(true); }
                    finally { if (File.Exists(probe)) File.Delete(probe); }
                }
                var drive = new DriveInfo(Path.GetPathRoot(root)!);
                if (drive.AvailableFreeSpace < 1024L * 1024 * 1024) warnings.Add("Menos de 1 GB livre: " + drive.Name);
                if (File.Exists(Path.Combine(root, "sync.ffs_db"))) warnings.Add("Há vestígio do FreeFileSync em " + root + ". Desative tarefas anteriores antes de iniciar. O banco antigo será preservado e ignorado.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { errors.Add(ex.Message); }
        warnings.Add("O utilitário confirma as pastas deste PC; a entrega à nuvem e aos outros dispositivos depende do iCloud.");
        foreach (var name in new[] { "FreeFileSync", "RealTimeSync" })
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(name);
            if (processes.Length > 0) warnings.Add(name + " está em execução. Confira seus pares de pastas antes de ativar.");
            foreach (var process in processes) process.Dispose();
        }
        return new(errors, warnings);
    }
}
