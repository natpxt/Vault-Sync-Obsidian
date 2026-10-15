using System.Diagnostics;
using System.Text.Json;
using VaultSync.Core;

if (args.FirstOrDefault() == "--benchmark") { Performance.Run(args.ElementAtOrDefault(1)!); return; }
string root = Path.Combine(Path.GetTempPath(), "VaultSync-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
var results = new List<object>(); int passed = 0, failed = 0;
void Test(string name, Action action)
{
    var watch = Stopwatch.StartNew();
    try { action(); passed++; results.Add(new { name, status = "passed", ms = watch.ElapsedMilliseconds }); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; results.Add(new { name, status = "failed", error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
void Throws(Action action) { bool thrown = false; try { action(); } catch (IOException) { thrown = true; } Assert(thrown, "Expected IOException"); }
Fixture New() => new(Path.Combine(root, Guid.NewGuid().ToString("N")));

Test("initial local file uploads", () => { using var f = New(); f.WriteL("a.md", "a"); f.Sync(); Assert(f.ReadC("a.md") == "a"); });
Test("initial cloud file downloads", () => { using var f = New(); f.WriteC("a.md", "a"); f.Sync(); Assert(f.ReadL("a.md") == "a"); });
Test("initial divergent files conflict without overwrite", () => { using var f = New(); f.WriteL("a.md", "local"); f.WriteC("a.md", "cloud"); f.Sync(); Assert(f.Engine.Store.Conflicts().Count == 1); Assert(f.ReadL("a.md") == "local" && f.ReadC("a.md") == "cloud"); });
Test("identical bytes ignore different timestamps", () => { using var f = New(); f.WriteL("a.md", "a"); f.WriteC("a.md", "a"); File.SetLastWriteTimeUtc(f.C("a.md"), DateTime.UtcNow.AddYears(-1)); f.Sync(); Assert(f.Engine.Store.Conflicts().Count == 0); });
Test("local edit propagates", () => { using var f = New(); f.Seed(); f.WriteL("a.md", "changed"); f.Sync(); Assert(f.ReadC("a.md") == "changed"); });
Test("remote edit propagates", () => { using var f = New(); f.Seed(); f.WriteC("a.md", "changed"); f.Sync(); Assert(f.ReadL("a.md") == "changed"); });
Test("concurrent edits conflict", () => { using var f = New(); f.Seed(); f.WriteL("a.md", "left"); f.WriteC("a.md", "right"); f.Sync(); Assert(f.Engine.Store.Conflicts().Count == 1 && f.ReadL("a.md") == "left"); });
Test("concurrent equal edits converge", () => { using var f = New(); f.Seed(); f.WriteL("a.md", "new"); f.WriteC("a.md", "new"); f.Sync(); Assert(f.Engine.Store.Conflicts().Count == 0); });
Test("local deletion propagates", () => { using var f = New(); f.Seed(); File.Delete(f.L("a.md")); f.Sync(); Assert(!File.Exists(f.C("a.md"))); });
Test("remote deletion propagates", () => { using var f = New(); f.Seed(); File.Delete(f.C("a.md")); f.Sync(); Assert(!File.Exists(f.L("a.md"))); });
Test("edit versus delete conflicts", () => { using var f = New(); f.Seed(); File.Delete(f.L("a.md")); f.WriteC("a.md", "new"); f.Sync(); Assert(f.Engine.Store.Conflicts().Count == 1 && f.ReadC("a.md") == "new"); });
Test("both deleted acknowledge tombstone", () => { using var f = New(); f.Seed(); File.Delete(f.L("a.md")); File.Delete(f.C("a.md")); f.Sync(); Assert(f.Engine.Store.Baseline()["a.md"] == null); });
Test("rename converges as verified copy and delete", () => { using var f = New(); f.Seed(); File.Move(f.L("a.md"), f.L("b.md")); f.Sync(); Assert(!File.Exists(f.C("a.md")) && f.ReadC("b.md") == "seed"); });
Test("nested file preserves relative path", () => { using var f = New(); f.WriteL("x/y/ação.md", "Olá"); f.Sync(); Assert(f.ReadC("x/y/ação.md") == "Olá"); });
Test("missing root never deletes", () => { using var f = New(); f.Seed(); Directory.Move(f.Cloud, f.Cloud + "-offline"); Assert(!f.Engine.Preview().Complete); Throws(() => f.Engine.Apply(f.Engine.Preview())); Assert(f.ReadL("a.md") == "seed"); });
Test("missing local root never deletes cloud", () => { using var f = New(); f.Seed(); Directory.Move(f.Local, f.Local + "-offline"); Throws(() => f.Engine.ProcessPaths(["a.md"])); Assert(f.ReadC("a.md") == "seed"); });
Test("blocked file does not become deletion", () => { using var f = New(); f.Seed(); using var locked = new FileStream(f.L("a.md"), FileMode.Open, FileAccess.ReadWrite, FileShare.None); var plan = f.Engine.Preview(); Assert(!plan.Complete); Throws(() => f.Engine.Apply(plan)); Assert(f.ReadC("a.md") == "seed"); });
Test("stale preview does not overwrite newer edit", () => { using var f = New(); f.Seed(); f.WriteL("a.md", "left"); var plan = f.Engine.Preview(); f.WriteC("a.md", "right"); f.Engine.Apply(plan); Assert(f.ReadC("a.md") == "right"); f.Sync(); Assert(f.Engine.Store.Conflicts().Count == 1); });
Test("race before apply is detected", () => { using var f = New(); f.Seed(); f.WriteL("a.md", "left"); f.Engine.BeforeApply = _ => f.WriteC("a.md", "race"); f.Sync(); Assert(f.ReadC("a.md") == "race"); });
Test("pending survives restart and retries", () => { using var f = New(); f.Seed(); f.WriteL("a.md", "new"); using (var locked = new FileStream(f.C("a.md"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) f.Engine.ProcessPaths(["a.md"]); Assert(f.Engine.Store.PendingErrors().Count > 0); f.Reopen(); f.Engine.Store.Queue("a.md", true); f.Engine.Retry(); Assert(f.ReadC("a.md") == "new"); });
Test("conflicts survive restart", () => { using var f = New(); f.Seed(); f.WriteL("a.md", "left"); f.WriteC("a.md", "right"); f.Sync(); f.Reopen(); Assert(f.Engine.Store.Conflicts().Count == 1); });
Test("unrelated files continue during conflict", () => { using var f = New(); f.Seed(); f.WriteL("a.md", "left"); f.WriteC("a.md", "right"); f.WriteL("ok.md", "ok"); f.Sync(); Assert(f.ReadC("ok.md") == "ok"); });
Test("resolve local verifies both copies", () => { using var f = New(); f.Seed(); f.WriteL("a.md", "left"); f.WriteC("a.md", "right"); f.Sync(); f.Engine.Resolve(f.Engine.Store.Conflicts()[0], "local"); Assert(f.ReadC("a.md") == "left" && f.Engine.Store.Conflicts().Count == 0); });
Test("resolve remote verifies both copies", () => { using var f = New(); f.Seed(); f.WriteL("a.md", "left"); f.WriteC("a.md", "right"); f.Sync(); f.Engine.Resolve(f.Engine.Store.Conflicts()[0], "cloud"); Assert(f.ReadL("a.md") == "right"); });
Test("manual Markdown merge", () => { using var f = New(); f.Seed(); f.WriteL("a.md", "left"); f.WriteC("a.md", "right"); f.Sync(); f.Engine.Resolve(f.Engine.Store.Conflicts()[0], "merge", "combined"); Assert(f.ReadL("a.md") == "combined" && f.ReadC("a.md") == "combined"); });
Test("changed conflict rejects stale resolution", () => { using var f = New(); f.Seed(); f.WriteL("a.md", "left"); f.WriteC("a.md", "right"); f.Sync(); var conflict = f.Engine.Store.Conflicts()[0]; f.WriteC("a.md", "newer"); Throws(() => f.Engine.Resolve(conflict, "local")); Assert(f.ReadC("a.md") == "newer"); });
Test("resolve delete versus edit by choosing deletion", () => { using var f = New(); f.Seed(); File.Delete(f.L("a.md")); f.WriteC("a.md", "new"); f.Sync(); f.Engine.Resolve(f.Engine.Store.Conflicts()[0], "local"); Assert(!File.Exists(f.C("a.md"))); });
Test("binary merge is rejected", () => { using var f = New(); f.WriteL("a.bin", "left"); f.WriteC("a.bin", "right"); f.Sync(); Throws(() => f.Engine.Resolve(f.Engine.Store.Conflicts()[0], "merge", "x")); });
Test("same roots rejected", () => { using var f = New(); Throws(() => Paths.ValidatePair(new(f.Local, f.Local), f.State)); });
Test("nested roots rejected", () => { using var f = New(); Throws(() => Paths.ValidatePair(new(f.Local, Path.Combine(f.Local, "nested")), f.State)); });
Test("state inside vault rejected", () => { using var f = New(); Throws(() => Paths.ValidatePair(f.Config, Path.Combine(f.Local, "state"))); });
Test("path traversal rejected", () => { using var f = New(); Throws(() => Paths.Resolve(f.Local, "../escape")); Throws(() => Paths.Resolve(f.Local, "C:\\escape")); Throws(() => Paths.Resolve(f.Local, "a:stream")); });
Test("internal files and FreeFileSync database ignored", () => { using var f = New(); f.WriteL("sync.ffs_db", "old"); f.WriteL(".megabrainsync-stage-1", "temp"); f.Sync(); Assert(!File.Exists(f.C("sync.ffs_db"))); });
Test("migration backup verified and manifest present", () => { using var f = New(); f.WriteL("a.md", "left"); f.WriteC("a.md", "right"); string backup = f.Engine.CreateMigrationBackup(f.Engine.Preview()); Assert(File.ReadAllText(Path.Combine(backup, "local/a.md")) == "left" && File.Exists(Path.Combine(backup, "manifest.json"))); });
Test("missed watcher event repaired by full scan", () => { using var f = New(); f.Seed(); f.WriteC("a.md", "missed"); f.Sync(); Assert(f.ReadL("a.md") == "missed"); });
Test("same-size edit with restored mtime still detected", () => { using var f = New(); f.Seed(); var time = File.GetLastWriteTimeUtc(f.L("a.md")); f.WriteL("a.md", "SEED"); File.SetLastWriteTimeUtc(f.L("a.md"), time); f.Sync(); Assert(f.ReadC("a.md") == "SEED"); });
Test("cloud placeholder flags recognized", () => { Assert(Paths.NeedsDownload((FileAttributes)0x400000)); Assert(Paths.NeedsDownload(FileAttributes.Offline)); Assert(!Paths.NeedsDownload((FileAttributes)0x80020)); });
Test("idempotent sync produces no loops", () => { using var f = New(); f.Seed(); var t = File.GetLastWriteTimeUtc(f.C("a.md")); f.Sync(); f.Sync(); Assert(File.GetLastWriteTimeUtc(f.C("a.md")) == t && f.Engine.Store.PendingErrors().Count == 0); });
Test("offline two-PC edits to different notes converge", () =>
{
    using var a = New(); a.Seed(); string second = Path.Combine(root, "pc2-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(second);
    using var b = new SyncEngine(new(second, a.Cloud), Path.Combine(root, "pc2state-" + Guid.NewGuid().ToString("N")));
    b.Apply(b.Preview()); a.WriteL("one.md", "one"); File.WriteAllText(Path.Combine(second, "two.md"), "two"); a.Sync(); b.Apply(b.Preview()); a.Sync(); Assert(a.ReadL("two.md") == "two" && File.ReadAllText(Path.Combine(second, "one.md")) == "one");
});
Test("offline two-PC same-note edits preserve conflict", () =>
{
    using var a = New(); a.Seed(); string second = Path.Combine(root, "pc2-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(second);
    using var b = new SyncEngine(new(second, a.Cloud), Path.Combine(root, "pc2state-" + Guid.NewGuid().ToString("N")));
    b.Apply(b.Preview()); a.WriteL("a.md", "work"); File.WriteAllText(Path.Combine(second, "a.md"), "home"); a.Sync(); b.Apply(b.Preview()); Assert(b.Store.Conflicts().Count == 1 && File.ReadAllText(Path.Combine(second, "a.md")) == "home" && a.ReadC("a.md") == "work");
});
Test("interrupted replace preserves rollback bytes after restart", () =>
{
    using var f = New(); f.Seed(); string prev = f.C(".megabrainsync-stage-simulated.previous"); File.WriteAllText(prev, "old interrupted bytes");
    string dir = Path.Combine(f.Engine.Store.Root, "operations"); Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "interrupted.json"), JsonSerializer.Serialize(new { Relative = "a.md", Stage = f.C(".megabrainsync-stage-simulated"), Target = f.C("a.md"), Previous = prev, Expected = Scanner.Read(prev), Kind = "replace" }));
    f.Reopen(); f.Sync(); var c = f.Engine.Store.Conflicts().Single(); Assert(Directory.GetFiles(c.Folder, "interrupted-version.bin", SearchOption.AllDirectories).Any(p => File.ReadAllText(p) == "old interrupted bytes"));
});
Test("junction is blocked without traversing external target", () =>
{
    using var f = New(); string outside = Path.Combine(root, "external-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "secret.md"), "outside");
    var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "-NoProfile", "-Command", "New-Item -ItemType Junction -Path '" + f.L("linked").Replace("'", "''") + "' -Target '" + outside.Replace("'", "''") + "' | Out-Null" } };
    using var proc = Process.Start(psi)!; proc.WaitForExit(); Assert(proc.ExitCode == 0); Assert(!f.Engine.Preview().Complete); Assert(!File.Exists(f.C("linked/secret.md")));
});
Test("worker observes edits and pause-resume", () =>
{
    using var f = New(); f.Seed();
    var workerEngine = new SyncEngine(f.Config, Path.Combine(root, "worker-" + Guid.NewGuid().ToString("N")));
    workerEngine.Apply(workerEngine.Preview()); using var worker = new SyncWorker(workerEngine); worker.Start();
    Thread.Sleep(1500); worker.Pause(true); Thread.Sleep(1200);
    f.WriteL("a.md", "paused"); Thread.Sleep(2300); Assert(f.ReadC("a.md") == "seed");
    worker.Pause(false); Assert(SpinWait.SpinUntil(() => { try { return f.ReadC("a.md") == "paused"; } catch (IOException) { return false; } }, 10000));
    _ = worker.GetStatusAsync().GetAwaiter().GetResult(); // wait for post-copy hash verification and baseline commit
    f.WriteC("a.md", "remote-event"); Assert(SpinWait.SpinUntil(() => { try { return f.ReadL("a.md") == "remote-event"; } catch (IOException) { return false; } }, 10000));
});
Test("worker recovers when cloud root is unavailable at startup", () =>
{
    using var f = New(); f.Seed();
    string state = Path.Combine(root, "worker-recovery-" + Guid.NewGuid().ToString("N"));
    var e = new SyncEngine(f.Config, state); e.Apply(e.Preview());
    string offline = f.Cloud + "-offline"; Directory.Move(f.Cloud, offline);
    using var worker = new SyncWorker(e); worker.Start(); Thread.Sleep(1800);
    f.WriteL("a.md", "while-offline"); Directory.Move(offline, f.Cloud);
    Assert(SpinWait.SpinUntil(() => { try { return f.ReadC("a.md") == "while-offline"; } catch (IOException) { return false; } }, 10000), "Root did not reattach");
});

string reportPath = args.FirstOrDefault() ?? Path.Combine(root, "results.json");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
File.WriteAllText(reportPath, JsonSerializer.Serialize(new { passed, failed, fixtures = root, results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{passed} passed, {failed} failed. Report: {reportPath}"); Environment.ExitCode = failed == 0 ? 0 : 1;

sealed class Fixture : IDisposable
{
    public string Local { get; } public string Cloud { get; } public string State { get; }
    public SyncConfig Config => new(Local, Cloud);
    public SyncEngine Engine { get; private set; }
    public Fixture(string root) { Local = Path.Combine(root, "local"); Cloud = Path.Combine(root, "cloud"); State = Path.Combine(root, "state"); Directory.CreateDirectory(Local); Directory.CreateDirectory(Cloud); Engine = new(Config, State); }
    public string L(string p) => Path.Combine(Local, p); public string C(string p) => Path.Combine(Cloud, p);
    public void WriteL(string p, string text) { Directory.CreateDirectory(Path.GetDirectoryName(L(p))!); File.WriteAllText(L(p), text); }
    public void WriteC(string p, string text) { Directory.CreateDirectory(Path.GetDirectoryName(C(p))!); File.WriteAllText(C(p), text); }
    public string ReadL(string p) => File.ReadAllText(L(p)); public string ReadC(string p) => File.ReadAllText(C(p));
    public void Sync() => Engine.Apply(Engine.Preview());
    public void Seed() { WriteL("a.md", "seed"); Sync(); }
    public void Reopen() { Engine.Dispose(); Engine = new(Config, State); }
    public void Dispose() => Engine.Dispose();
}
