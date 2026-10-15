using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VaultSync.Core;

public sealed record SyncConfig(string LocalRoot, string CloudRoot, bool StartWithWindows = true)
{
    public string PairId => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        Paths.Normalize(LocalRoot).ToUpperInvariant() + "|" + Paths.Normalize(CloudRoot).ToUpperInvariant())))[..24];
}
public sealed record FileStamp(string Hash, long Length, long ModifiedUtcTicks);
public sealed record TreeSnapshot(Dictionary<string, FileStamp> Files, List<string> Errors)
{
    public bool Complete => Errors.Count == 0;
}
public enum SyncAction { Equal, Upload, Download, DeleteLocal, DeleteCloud, Conflict }
public sealed record PlanItem(string Path, SyncAction Action, FileStamp? Local, FileStamp? Cloud);
public sealed record SyncPlan(List<PlanItem> Items, List<string> Errors)
{
    public bool Complete => Errors.Count == 0;
}
public sealed record Conflict(string Path, FileStamp? Local, FileStamp? Cloud, string Folder, string Reason);
public sealed record ValidationReport(List<string> Errors, List<string> Warnings)
{
    public bool CanContinue => Errors.Count == 0;
}
public static class AppFiles
{
    public static string DataRoot => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VaultSync");
    public static string ConfigPath => System.IO.Path.Combine(DataRoot, "config.json");
    public static SyncConfig? LoadConfig() => File.Exists(ConfigPath)
        ? JsonSerializer.Deserialize<SyncConfig>(File.ReadAllText(ConfigPath)) : null;
    public static void SaveConfig(SyncConfig config)
    {
        Directory.CreateDirectory(DataRoot);
        var temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, ConfigPath, true);
    }
}
