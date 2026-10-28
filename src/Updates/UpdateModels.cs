using System.Text.Json;
using System.Text.RegularExpressions;

namespace VaultSync.Updates;

public readonly record struct UpdateVersion(int Major, int Minor, int Patch, bool Preview) : IComparable<UpdateVersion>
{
    public static bool TryParse(string? value, out UpdateVersion version)
    {
        version = default;
        var match = Regex.Match(value ?? "", @"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out int major) || !int.TryParse(match.Groups[2].Value, out int minor) || !int.TryParse(match.Groups[3].Value, out int patch)) return false;
        version = new(major, minor, patch, match.Groups[4].Success); return true;
    }
    public static UpdateVersion Parse(string value) => TryParse(value, out var result) ? result : throw new InvalidDataException("Versão de atualização inválida.");
    public int CompareTo(UpdateVersion other)
    {
        int c = Major.CompareTo(other.Major); if (c != 0) return c;
        c = Minor.CompareTo(other.Minor); if (c != 0) return c;
        c = Patch.CompareTo(other.Patch); return c != 0 ? c : other.Preview.CompareTo(Preview);
    }
    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
public enum UpdatePhase { Checking, Current, Available, Downloading, Preparing, Completed, Failed }
public sealed record ReleaseAsset(string Name, string Url, long Size, string? Digest);
public sealed record UpdateRelease(string Version, string Notes, string PageUrl, ReleaseAsset Package, ReleaseAsset Manifest);
public sealed record UpdateCheck(UpdatePhase Phase, UpdateRelease? Release = null);
public sealed record DownloadProgress(long Bytes, long Total);
public sealed record VerifiedUpdate(string Path, string Sha256, UpdateRelease Release);
public sealed record UpdatePreferences(bool Automatic = true, string? ETag = null, UpdateRelease? CachedRelease = null,
    DateTimeOffset? LastCheckedUtc = null, DateTimeOffset? RetryAfterUtc = null);

public sealed class UpdateStore
{
    private readonly object gate = new();
    public string Root { get; }
    public UpdatePreferences Preferences { get; private set; }
    public UpdateStore(string root)
    {
        Root = Path.GetFullPath(root); Preferences = new();
        try { CheckPath(Root); CheckPath(SettingsPath); if (File.Exists(SettingsPath)) Preferences = JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(SettingsPath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }
    private string SettingsPath => Path.Combine(Root, "update-settings.json");
    public void Save(UpdatePreferences preferences)
    {
        lock (gate)
        {
        CheckPath(Root); CheckPath(SettingsPath);
        Directory.CreateDirectory(Root); string temp = SettingsPath + ".tmp";
        CheckPath(temp);
        File.WriteAllText(temp, JsonSerializer.Serialize(preferences)); File.Move(temp, SettingsPath, true); Preferences = preferences;
        }
    }
    public void Modify(Func<UpdatePreferences, UpdatePreferences> update) { lock (gate) Save(update(Preferences)); }
    public static void CheckPath(string path)
    {
        for (var info = new DirectoryInfo(Path.GetFullPath(path)); info != null; info = info.Parent)
            if (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("O diretório de atualização não pode ser um link ou junção.");
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("O arquivo de atualização não pode ser um link.");
    }
}
