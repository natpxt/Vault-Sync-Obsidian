using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace VaultSync.Core;

public sealed class StateStore : IDisposable
{
    private readonly SqliteConnection db;
    public string Root { get; }
    public StateStore(string root)
    {
        Root = root; Directory.CreateDirectory(root);
        db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "state.sqlite"), Pooling = false }.ToString());
        db.Open();
        Execute("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; CREATE TABLE IF NOT EXISTS baseline(path TEXT PRIMARY KEY COLLATE NOCASE, stamp TEXT); CREATE TABLE IF NOT EXISTS pending(path TEXT PRIMARY KEY COLLATE NOCASE, attempts INTEGER NOT NULL DEFAULT 0, due INTEGER NOT NULL DEFAULT 0, error TEXT); CREATE TABLE IF NOT EXISTS conflicts(path TEXT PRIMARY KEY COLLATE NOCASE, json TEXT NOT NULL); CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT);");
    }
    private void Execute(string sql, params (string, object?)[] parameters)
    {
        using var command = db.CreateCommand(); command.CommandText = sql;
        foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }
    public Dictionary<string, FileStamp?> Baseline()
    {
        using var command = db.CreateCommand(); command.CommandText = "SELECT path, stamp FROM baseline";
        using var reader = command.ExecuteReader(); var result = new Dictionary<string, FileStamp?>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read()) result.Add(reader.GetString(0), reader.IsDBNull(1) ? null : JsonSerializer.Deserialize<FileStamp>(reader.GetString(1)));
        return result;
    }
    public void Acknowledge(string path, FileStamp? stamp)
    {
        using var tx = db.BeginTransaction();
        using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO baseline(path,stamp) VALUES($p,$s) ON CONFLICT(path) DO UPDATE SET stamp=$s; DELETE FROM pending WHERE path=$p; DELETE FROM conflicts WHERE path=$p;";
        cmd.Parameters.AddWithValue("$p", path); cmd.Parameters.AddWithValue("$s", stamp == null ? DBNull.Value : JsonSerializer.Serialize(stamp)); cmd.ExecuteNonQuery(); tx.Commit();
    }
    public void Queue(string path, bool immediate = false) => Execute(immediate
        ? "INSERT INTO pending(path,due) VALUES($p,0) ON CONFLICT(path) DO UPDATE SET due=0"
        : "INSERT OR IGNORE INTO pending(path) VALUES($p)", ("$p", path));
    public void CompleteUnchanged(string path) => Execute("DELETE FROM pending WHERE path=$p", ("$p", path));
    public List<string> Due()
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT path FROM pending WHERE due <= $t";
        cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        using var reader = cmd.ExecuteReader(); var result = new List<string>(); while (reader.Read()) result.Add(reader.GetString(0)); return result;
    }
    public void Failed(string path, string error) => Execute("UPDATE pending SET attempts=attempts+1,due=$t + min(300, 2 << min(attempts,7)),error=$e WHERE path=$p", ("$p", path), ("$t", DateTimeOffset.UtcNow.ToUnixTimeSeconds()), ("$e", error));
    public void SetConflict(Conflict conflict)
    {
        Execute("INSERT INTO conflicts(path,json) VALUES($p,$j) ON CONFLICT(path) DO UPDATE SET json=$j; DELETE FROM pending WHERE path=$p;", ("$p", conflict.Path), ("$j", JsonSerializer.Serialize(conflict)));
    }
    public List<Conflict> Conflicts()
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT json FROM conflicts ORDER BY path";
        using var reader = cmd.ExecuteReader(); var result = new List<Conflict>();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<Conflict>(reader.GetString(0))!); return result;
    }
    public List<string> PendingErrors()
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT path || ': ' || coalesce(error,'aguardando') FROM pending ORDER BY due LIMIT 200";
        using var r = cmd.ExecuteReader(); var list = new List<string>(); while (r.Read()) list.Add(r.GetString(0)); return list;
    }
    public bool HasSetting(string key)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT count(*) FROM settings WHERE key=$k"; cmd.Parameters.AddWithValue("$k", key); return (long)cmd.ExecuteScalar()! > 0;
    }
    public void SetSetting(string key, string value) => Execute("INSERT INTO settings VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v", ("$k", key), ("$v", value));
    public void Dispose() => db.Dispose();
}
