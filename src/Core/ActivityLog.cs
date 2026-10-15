using System.Text.Json;

namespace VaultSync.Core;

public sealed class ActivityLog
{
    private readonly object gate = new();
    private readonly Queue<SyncEvent> entries = new();
    public string FilePath { get; }
    public string? LastWriteError { get; private set; }
    public event Action<SyncEvent>? Added;
    public ActivityLog(string dataRoot)
    {
        Directory.CreateDirectory(Path.Combine(dataRoot, "logs"));
        FilePath = Path.Combine(dataRoot, "logs", "execution.jsonl");
        try
        {
            if (File.Exists(FilePath)) foreach (var line in File.ReadLines(FilePath).TakeLast(500))
                    try { if (JsonSerializer.Deserialize<SyncEvent>(line) is { } entry) entries.Enqueue(entry); }
                    catch (JsonException) { }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LastWriteError = ex.Message; }
    }
    public List<SyncEvent> Snapshot() { lock (gate) return entries.ToList(); }
    public void Write(string level, string message, string? path = null) => Write(new(DateTimeOffset.Now, level, message, path));
    public void Write(SyncEvent entry)
    {
        lock (gate)
        {
            entries.Enqueue(entry); while (entries.Count > 500) entries.Dequeue();
            try
            {
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 2 * 1024 * 1024)
                {
                    for (int i = 2; i >= 1; i--) if (File.Exists(FilePath + "." + i)) File.Move(FilePath + "." + i, FilePath + "." + (i + 1), true);
                    File.Move(FilePath, FilePath + ".1", true);
                }
                File.AppendAllText(FilePath, JsonSerializer.Serialize(entry) + Environment.NewLine); LastWriteError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LastWriteError = ex.Message; }
        }
        Added?.Invoke(entry);
    }
}
