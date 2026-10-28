using System.Text.Json;

namespace VaultSync.Updates;

public enum ResumeState { Stopped, Running, Paused }
public sealed record UpdateHandoff(int Protocol, int OldPid, long OldProcessTicks, string Version, string PackageName,
    string Sha256, bool Startup, ResumeState Resume, bool Visible);
public sealed record UpdateResult(bool Success, string Message);

public static class HandoffFile
{
    public static void Write(string file, UpdateHandoff job)
    { UpdateStore.CheckPath(file); File.WriteAllText(file, JsonSerializer.Serialize(job)); }
    public static UpdateHandoff Read(string file)
    {
        UpdateStore.CheckPath(file);
        if (new FileInfo(file).Length > 16384) throw new IOException("Solicitação de atualização muito grande.");
        var job = JsonSerializer.Deserialize<UpdateHandoff>(File.ReadAllText(file)) ?? throw new IOException("Solicitação de atualização inválida.");
        var version = UpdateVersion.Parse(job.Version);
        if (job.Protocol != 1 || version.Preview || job.OldPid <= 0 || job.OldProcessTicks <= 0 || !Enum.IsDefined(job.Resume) ||
            job.PackageName != $"VaultSync-Obsidian-{version}-win-x64-Setup.exe" || job.Sha256.Length != 64 || !job.Sha256.All(Uri.IsHexDigit)) throw new IOException("Solicitação de atualização inválida.");
        return job;
    }
}
