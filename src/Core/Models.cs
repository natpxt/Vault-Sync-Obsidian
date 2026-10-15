using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VaultSync.Core;

public sealed record SyncConfig(string LocalRoot, string CloudRoot, bool StartWithWindows = true, bool MonitorOnLaunch = false, bool IncludeWorkspaceLayouts = false, bool PreferNewest = true, int PolicyRevision = 0)
{
    public bool CanAutoStart => MonitorOnLaunch && PolicyRevision >= 1;
    public string PairId => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        Paths.Normalize(LocalRoot).ToUpperInvariant() + "|" + Paths.Normalize(CloudRoot).ToUpperInvariant())))[..24];
}
public sealed record FileStamp(string Hash, long Length, long ModifiedUtcTicks);
public sealed record TreeSnapshot(Dictionary<string, FileStamp> Files, List<string> Errors, List<string>? BlockedPaths = null)
{
    public bool Complete => Errors.Count == 0;
}
public enum SyncAction { Equal, Upload, Download, DeleteLocal, DeleteCloud, Conflict }
public sealed record PlanItem(string Path, SyncAction Action, FileStamp? Local, FileStamp? Cloud);
public sealed record SyncPlan(List<PlanItem> Items, List<string> Errors, bool RootsAvailable = true, List<string>? BlockedPaths = null)
{
    public bool Complete => Errors.Count == 0;
}
public sealed record Conflict(string Path, FileStamp? Local, FileStamp? Cloud, string Folder, string Reason, bool RequiresExplicitResolution = false);
public sealed record ComparisonSnapshot(Conflict Conflict, string LocalText, string CloudText, bool CanMerge);
public sealed record ValidationReport(List<string> Errors, List<string> Warnings)
{
    public bool CanContinue => Errors.Count == 0;
}
public sealed record SyncEvent(DateTimeOffset Time, string Level, string Message, string? Path = null);
public static class AppFiles
{
    public static string DataRoot => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VaultSync");
    public static string ConfigPath => System.IO.Path.Combine(DataRoot, "config.json");
    public static SyncConfig? LoadConfig(string? dataRoot = null)
    {
        string file = System.IO.Path.Combine(dataRoot ?? DataRoot, "config.json");
        return File.Exists(file) ? JsonSerializer.Deserialize<SyncConfig>(File.ReadAllText(file)) : null;
    }
    public static void SaveConfig(SyncConfig config, string? dataRoot = null)
    {
        var root = dataRoot ?? DataRoot; Directory.CreateDirectory(root);
        var path = System.IO.Path.Combine(root, "config.json"); var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
}
