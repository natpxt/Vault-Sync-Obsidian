using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VaultSync.Updates;

internal static class UpdateTests
{
    private static void Assert(bool condition) { if (!condition) throw new Exception("Update assertion failed"); }
    private static void Throws(Action action) { try { action(); } catch (Exception ex) when (ex is IOException or HttpRequestException or JsonException or OperationCanceledException) { return; } throw new Exception("Expected update failure"); }
    public static void Run(Action<string, Action> test)
    {
        test("updates compare versions numerically", () => Assert(UpdateVersion.Parse("v1.10.0").CompareTo(UpdateVersion.Parse("1.2.0")) > 0));
        test("stable version outranks preview of same version", () => Assert(UpdateVersion.Parse("1.2.0").CompareTo(UpdateVersion.Parse("1.2.0-preview.1+abc")) > 0));
        test("invalid and overflowing versions are rejected", () => { foreach (var value in new[] { "1.2", "../1.2.0", "01.2.0", "999999999999.2.0" }) Assert(!UpdateVersion.TryParse(value, out _)); });
        test("draft releases are never offered", () => Assert(GitHubUpdates.ParseRelease(Json(draft: true)) == null));
        test("preview releases are never offered", () => Assert(GitHubUpdates.ParseRelease(Json(preview: true)) == null));
        test("unflagged preview tag is rejected", () => Throws(() => GitHubUpdates.ParseRelease(Json().Replace("v1.3.0", "v1.3.0-preview.1"))));
        test("release with missing installer is incomplete", () => Throws(() => GitHubUpdates.ParseRelease(Json().Replace(FileName, "missing.exe"))));
        test("release asset outside official repository is rejected", () => Throws(() => GitHubUpdates.ParseRelease(Json().Replace("https://github.com/natpxt/", "https://evil.example/natpxt/"))));
        test("release with invalid digest is rejected", () => Throws(() => GitHubUpdates.ParseRelease(Json().Replace("sha256:", "md5:"))));
        test("package larger than supported bound is rejected", () => { var r = Release(); Throws(() => GitHubUpdates.ValidateRelease(r with { Package = r.Package with { Size = GitHubUpdates.MaximumPackageSize + 1 } })); });
        test("checks use ETag and persisted cached release on 304", () =>
        {
            var store = Store(); bool conditional = false;
            using var client = Client(request => { conditional = request.Headers.IfNoneMatch.Any(); return conditional ? new(HttpStatusCode.NotModified) : Response(Json(), etag: true); });
            using var updates = new GitHubUpdates(store, client);
            Assert(updates.CheckAsync("1.2.0").GetAwaiter().GetResult().Phase == UpdatePhase.Available);
            Assert(updates.CheckAsync("1.2.0").GetAwaiter().GetResult().Release?.Version == "1.3.0" && conditional);
            Assert(new UpdateStore(store.Root).Preferences.CachedRelease?.Version == "1.3.0");
        });
        test("same or older release never downgrades installed version", () =>
        {
            using var updates = new GitHubUpdates(Store(), Client(_ => Response(Json())));
            Assert(updates.CheckAsync("1.3.0").GetAwaiter().GetResult().Phase == UpdatePhase.Current);
            Assert(updates.CheckAsync("1.10.0").GetAwaiter().GetResult().Phase == UpdatePhase.Current);
        });
        test("rate limit honors retry without additional request", () =>
        {
            int calls = 0; var clock = DateTimeOffset.UtcNow; var store = Store();
            using var updates = new GitHubUpdates(store, Client(_ => { calls++; var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests); r.Headers.RetryAfter = new(TimeSpan.FromMinutes(5)); return r; }), () => clock);
            Throws(() => updates.CheckAsync("1.2.0").GetAwaiter().GetResult()); Throws(() => updates.CheckAsync("1.2.0").GetAwaiter().GetResult()); Assert(calls == 1 && store.Preferences.RetryAfterUtc > clock);
        });
        test("offline or malformed response does not change automatic preference", () =>
        {
            var store = Store(); store.Modify(p => p with { Automatic = false });
            using var updates = new GitHubUpdates(store, Client(_ => throw new HttpRequestException("offline")));
            Throws(() => updates.CheckAsync("1.2.0").GetAwaiter().GetResult()); Assert(!store.Preferences.Automatic && store.Preferences.LastCheckedUtc == null);
        });
        test("unsupported redirect host is refused", () =>
        {
            using var updates = new GitHubUpdates(Store(), Client(_ => { var r = new HttpResponseMessage(HttpStatusCode.Redirect); r.Headers.Location = new Uri("https://evil.example/setup.exe"); return r; }));
            Throws(() => updates.CheckAsync("1.2.0").GetAwaiter().GetResult());
        });
        test("HTTPS downgrade redirect is refused", () =>
        {
            using var updates = new GitHubUpdates(Store(), Client(_ => { var r = new HttpResponseMessage(HttpStatusCode.Redirect); r.Headers.Location = new Uri("http://github.com/test"); return r; }));
            Throws(() => updates.CheckAsync("1.2.0").GetAwaiter().GetResult());
        });
        test("valid download verifies manifest size hash and executable version", () =>
        {
            var store = Store(); using var updates = Downloader(store);
            var result = updates.DownloadAsync(Release()).GetAwaiter().GetResult(); Assert(File.ReadAllBytes(result.Path).SequenceEqual(Payload) && result.Sha256 == Hash(Payload));
            Assert(!Directory.GetFiles(store.Root, "*.part", SearchOption.AllDirectories).Any());
        });
        test("tampered installer is never retained", () =>
        {
            var store = Store(); using var updates = Downloader(store, payload: Enumerable.Repeat((byte)99, Payload.Length).ToArray());
            Throws(() => updates.DownloadAsync(Release()).GetAwaiter().GetResult()); Assert(Directory.GetDirectories(Path.Combine(store.Root, "jobs")).Length == 0);
        });
        test("wrong executable product version is refused", () => { using var updates = Downloader(Store(), version: "1.4.0"); Throws(() => updates.DownloadAsync(Release()).GetAwaiter().GetResult()); });
        test("preview executable cannot masquerade as stable", () => { using var updates = Downloader(Store(), version: "1.3.0-preview.1"); Throws(() => updates.DownloadAsync(Release()).GetAwaiter().GetResult()); });
        test("incomplete download is refused", () => { using var updates = Downloader(Store(), payload: Payload[..100]); Throws(() => updates.DownloadAsync(Release()).GetAwaiter().GetResult()); });
        test("duplicate or absent checksum entry is refused", () => { Throws(() => GitHubUpdates.ParseChecksum(Manifest + Manifest, FileName)); Throws(() => GitHubUpdates.ParseChecksum(Manifest, "missing.exe")); });
        test("manifest and GitHub digest must agree", () =>
        {
            using var updates = Downloader(Store()); var release = Release(); release = release with { Package = release.Package with { Digest = "sha256:" + new string('a', 64) } };
            Throws(() => updates.DownloadAsync(release).GetAwaiter().GetResult());
        });
        test("cancelled download removes partial package", () =>
        {
            var store = Store(); using var updates = Downloader(store); using var ct = new CancellationTokenSource();
            Throws(() => updates.DownloadAsync(Release(), new InlineProgress(_ => ct.Cancel()), ct.Token).GetAwaiter().GetResult());
            Assert(Directory.GetDirectories(Path.Combine(store.Root, "jobs")).Length == 0);
        });
        test("handoff rejects traversal and unknown protocol", () =>
        {
            string root = Store().Root; Directory.CreateDirectory(root); string file = Path.Combine(root, "request.json");
            var job = new UpdateHandoff(1, 123, 1, "1.3.0", FileName, Hash(Payload), false, ResumeState.Paused, true);
            HandoffFile.Write(file, job); Assert(HandoffFile.Read(file).Resume == ResumeState.Paused);
            HandoffFile.Write(file, job with { PackageName = "../bad.exe" }); Throws(() => HandoffFile.Read(file));
            HandoffFile.Write(file, job with { Protocol = 2 }); Throws(() => HandoffFile.Read(file));
        });
        test("automatic check preference survives restart", () => { var store = Store(); store.Modify(p => p with { Automatic = false }); Assert(!new UpdateStore(store.Root).Preferences.Automatic); });
    }
    private const string FileName = "VaultSync-Obsidian-1.3.0-win-x64-Setup.exe";
    private static readonly byte[] Payload = Enumerable.Repeat((byte)42, 300000).ToArray();
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Manifest => Hash(Payload) + "  " + FileName + "\n";
    private static UpdateStore Store() => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VaultSyncBuild", "update-unit-tests", Guid.NewGuid().ToString("N")));
    private static string Json(bool draft = false, bool preview = false) => JsonSerializer.Serialize(new { draft, prerelease = preview, tag_name = "v1.3.0", html_url = GitHubUpdates.ReleasePage + "/tag/v1.3.0", body = "Novidades", assets = new[] {
        new { name = FileName, state = "uploaded", size = Payload.Length, digest = "sha256:" + Hash(Payload).ToLowerInvariant(), browser_download_url = $"https://github.com/{GitHubUpdates.Repository}/releases/download/v1.3.0/{FileName}" },
        new { name = "SHA256SUMS.txt", state = "uploaded", size = Encoding.UTF8.GetByteCount(Manifest), digest = "sha256:" + Hash(Encoding.UTF8.GetBytes(Manifest)).ToLowerInvariant(), browser_download_url = $"https://github.com/{GitHubUpdates.Repository}/releases/download/v1.3.0/SHA256SUMS.txt" } } });
    internal static UpdateRelease Release() => GitHubUpdates.ParseRelease(Json())!;
    private static HttpResponseMessage Response(string body, bool etag = false) { var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }; if (etag) response.Headers.ETag = new("\"test\""); return response; }
    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> handler) => new(new Handler(handler));
    private static GitHubUpdates Downloader(UpdateStore store, byte[]? payload = null, string version = "1.3.0") => new(store,
        Client(r => new(HttpStatusCode.OK) { Content = new ByteArrayContent(r.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal) ? Encoding.UTF8.GetBytes(Manifest) : payload ?? Payload) }), readVersion: _ => version);
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(handler(request)); } }
    private sealed class InlineProgress(Action<DownloadProgress> action) : IProgress<DownloadProgress> { public void Report(DownloadProgress value) => action(value); }
}
