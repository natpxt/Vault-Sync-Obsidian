using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VaultSync.Updates;

public sealed class GitHubUpdates : IDisposable
{
    public const string Repository = "natpxt/Vault-Sync-Obsidian";
    public const string ReleasePage = "https://github.com/" + Repository + "/releases";
    public const string LatestUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
    public const long MaximumPackageSize = 512L * 1024 * 1024;
    private readonly HttpClient client;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly UpdateStore store;
    private readonly Func<DateTimeOffset> now;
    private readonly Func<string, string?> readVersion;
    public GitHubUpdates(UpdateStore store, HttpClient? client = null, Func<DateTimeOffset>? now = null, Func<string, string?>? readVersion = null)
    {
        this.store = store;
        this.client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        this.readVersion = readVersion ?? (file => FileVersionInfo.GetVersionInfo(file).ProductVersion);
    }
    public async Task<UpdateCheck> CheckAsync(string currentVersion, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (store.Preferences.RetryAfterUtc > now()) throw new IOException("GitHub solicitou aguardar antes de verificar novamente.");
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await GetAsync(new Uri(LatestUrl), store.Preferences.ETag, limit.Token).ConfigureAwait(false);
            UpdateRelease? release;
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                release = store.Preferences.CachedRelease;
                if (release == null && store.Preferences.LastCheckedUtc == null) throw new IOException("Cache de atualização indisponível; tente novamente.");
                if (release != null) ValidateRelease(release);
            }
            else
            {
                response.EnsureSuccessStatusCode();
                string json = Encoding.UTF8.GetString(await ReadBoundedAsync(response, 256 * 1024, limit.Token).ConfigureAwait(false));
                release = ParseRelease(json);
            }
            store.Modify(p => p with { ETag = response.Headers.ETag?.ToString() ?? p.ETag,
                CachedRelease = release, LastCheckedUtc = now(), RetryAfterUtc = null });
            if (release == null || UpdateVersion.Parse(release.Version).CompareTo(UpdateVersion.Parse(currentVersion)) <= 0) return new(UpdatePhase.Current);
            return new(UpdatePhase.Available, release);
        }
        finally { gate.Release(); }
    }
    public static UpdateRelease? ParseRelease(string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!UpdateVersion.TryParse(tag, out var version) || version.Preview) throw new IOException("A release não possui uma versão estável válida.");
        string name = $"VaultSync-Obsidian-{version}-win-x64-Setup.exe";
        ReleaseAsset Asset(string expected)
        {
            var matches = root.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == expected).ToList();
            if (matches.Count != 1) throw new IOException("A release está incompleta: instalador ou checksum indisponível.");
            var a = matches[0];
            if (a.GetProperty("state").GetString() != "uploaded") throw new IOException("O arquivo da release ainda não está disponível.");
            return new(expected, a.GetProperty("browser_download_url").GetString() ?? "", a.GetProperty("size").GetInt64(),
                a.TryGetProperty("digest", out var digest) ? digest.GetString() : null);
        }
        var release = new UpdateRelease(version.ToString(), root.TryGetProperty("body", out var notes) ? notes.GetString() ?? "" : "",
            root.GetProperty("html_url").GetString() ?? "", Asset(name), Asset("SHA256SUMS.txt"));
        // Accept the established vX.Y.Z or X.Y.Z tags, but no asset URLs outside this exact release.
        string prefix = $"https://github.com/{Repository}/releases/download/{tag}/";
        if (release.Package.Url != prefix + name || release.Manifest.Url != prefix + "SHA256SUMS.txt" || release.PageUrl != $"{ReleasePage}/tag/{tag}")
            throw new IOException("A origem dos arquivos de atualização não é a release oficial.");
        ValidateRelease(release); return release;
    }
    public static void ValidateRelease(UpdateRelease release)
    {
        var version = UpdateVersion.Parse(release.Version);
        if (version.Preview || release.Package.Name != $"VaultSync-Obsidian-{version}-win-x64-Setup.exe" || release.Manifest.Name != "SHA256SUMS.txt") throw new IOException("Pacote de atualização inválido.");
        if (release.Package.Size <= 0 || release.Package.Size > MaximumPackageSize || release.Manifest.Size <= 0 || release.Manifest.Size > 65536) throw new IOException("Tamanho do pacote de atualização inválido.");
        string tag = new Uri(release.PageUrl).Segments.Last();
        if (tag != version.ToString() && tag != "v" + version) throw new IOException("Tag de atualização inválida.");
        if (release.PageUrl != $"{ReleasePage}/tag/{tag}" || release.Package.Url != $"https://github.com/{Repository}/releases/download/{tag}/{release.Package.Name}" || release.Manifest.Url != $"https://github.com/{Repository}/releases/download/{tag}/SHA256SUMS.txt") throw new IOException("Origem de atualização inválida.");
        foreach (var asset in new[] { release.Package, release.Manifest })
            if (asset.Digest != null && !Regex.IsMatch(asset.Digest, "^sha256:[0-9a-fA-F]{64}$")) throw new IOException("Digest da release inválido.");
    }
    public async Task<VerifiedUpdate> DownloadAsync(UpdateRelease release, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false); string? job = null;
        try
        {
            ValidateRelease(release); UpdateStore.CheckPath(store.Root);
            if (new DriveInfo(Path.GetPathRoot(store.Root)!).AvailableFreeSpace < release.Package.Size * 4 + 10485760) throw new IOException("Espaço insuficiente para baixar e preservar a versão anterior.");
            job = Path.Combine(store.Root, "jobs", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(job); UpdateStore.CheckPath(job);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(TimeSpan.FromMinutes(30));
            using var manifestResponse = await GetAsync(new Uri(release.Manifest.Url), null, limit.Token).ConfigureAwait(false); manifestResponse.EnsureSuccessStatusCode();
            byte[] manifest = await ReadBoundedAsync(manifestResponse, 65536, limit.Token).ConfigureAwait(false);
            if (manifest.Length != release.Manifest.Size) throw new IOException("Checksum recebido com tamanho divergente.");
            ValidateDigest(manifest, release.Manifest.Digest);
            string expectedHash = ParseChecksum(Encoding.UTF8.GetString(manifest).TrimStart('\uFEFF'), release.Package.Name);
            if (release.Package.Digest != null && !release.Package.Digest[7..].Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new IOException("O manifesto e o digest do GitHub divergem.");
            string partial = Path.Combine(job, "package.part"), final = Path.Combine(job, release.Package.Name);
            using var response = await GetAsync(new Uri(release.Package.Url), null, limit.Token).ConfigureAwait(false); response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } size && size != release.Package.Size) throw new IOException("O tamanho anunciado pelo servidor diverge da release.");
            await using (var input = await response.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous))
            {
                byte[] buffer = new byte[131072]; long total = 0;
                while (true)
                {
                    using var stall = CancellationTokenSource.CreateLinkedTokenSource(limit.Token); stall.CancelAfter(TimeSpan.FromSeconds(60));
                    int read = await input.ReadAsync(buffer, stall.Token).ConfigureAwait(false); if (read == 0) break;
                    total += read; if (total > release.Package.Size) throw new IOException("Pacote maior que o anunciado.");
                    await output.WriteAsync(buffer.AsMemory(0, read), limit.Token).ConfigureAwait(false); progress?.Report(new(total, release.Package.Size));
                }
                if (total != release.Package.Size) throw new IOException("O download terminou incompleto.");
                await output.FlushAsync(limit.Token).ConfigureAwait(false);
            }
            await VerifyFileAsync(partial, expectedHash, release.Version, limit.Token).ConfigureAwait(false);
            File.Move(partial, final); return new(final, expectedHash, release);
        }
        catch { if (job != null && Directory.Exists(job)) { UpdateStore.CheckPath(job); Directory.Delete(job, true); } throw; }
        finally { gate.Release(); }
    }
    public static string ParseChecksum(string text, string filename)
    {
        var matches = text.Split('\n').Select(l => Regex.Match(l.TrimEnd('\r'), @"^([0-9a-fA-F]{64})\s+\*?(.+)$")).Where(m => m.Success && m.Groups[2].Value == filename).ToList();
        if (matches.Count != 1) throw new IOException("O manifesto não contém um checksum único para o instalador.");
        return matches[0].Groups[1].Value.ToUpperInvariant();
    }
    public async Task VerifyFileAsync(string file, string hash, string version, CancellationToken ct = default)
    {
        UpdateStore.CheckPath(file);
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous);
        string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
        if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("SHA-256 do instalador divergente; atualização cancelada.");
        var product = UpdateVersion.Parse(readVersion(file) ?? "");
        if (product.Preview || product.CompareTo(UpdateVersion.Parse(version)) != 0) throw new IOException("A versão do executável não corresponde à release.");
    }
    private static void ValidateDigest(byte[] bytes, string? digest)
    { if (digest != null && !Convert.ToHexString(SHA256.HashData(bytes)).Equals(digest[7..], StringComparison.OrdinalIgnoreCase)) throw new IOException("Digest do checksum divergente."); }
    private async Task<HttpResponseMessage> GetAsync(Uri uri, string? etag, CancellationToken ct)
    {
        for (int redirects = 0; redirects < 6; redirects++)
        {
            ValidateTransport(uri);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("VaultSync", "1.2.0")); request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            if (etag != null && uri.AbsoluteUri == LatestUrl) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                var retry = response.Headers.RetryAfter?.Date ?? now() + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(15));
                if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values) && long.TryParse(values.FirstOrDefault(), out long reset))
                    retry = retry > DateTimeOffset.FromUnixTimeSeconds(reset) ? retry : DateTimeOffset.FromUnixTimeSeconds(reset);
                store.Modify(p => p with { RetryAfterUtc = retry }); response.Dispose(); throw new IOException("GitHub solicitou aguardar antes de verificar novamente.");
            }
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location; response.Dispose();
                if (location == null) throw new IOException("Redirecionamento de atualização inválido."); uri = location.IsAbsoluteUri ? location : new Uri(uri, location); continue;
            }
            return response;
        }
        throw new IOException("Redirecionamentos demais no download de atualização.");
    }
    private static void ValidateTransport(Uri uri)
    {
        string[] allowed = ["api.github.com", "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com", "objects-origin.githubusercontent.com", "github-releases.githubusercontent.com"];
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || !allowed.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)) throw new IOException("Conexão de atualização fora do GitHub oficial.");
    }
    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int maximum, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > maximum) throw new IOException("Resposta de atualização muito grande.");
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false); using var output = new MemoryStream(); byte[] buffer = new byte[8192];
        while (true) { int read = await input.ReadAsync(buffer, ct).ConfigureAwait(false); if (read == 0) break; if (output.Length + read > maximum) throw new IOException("Resposta de atualização muito grande."); output.Write(buffer, 0, read); }
        return output.ToArray();
    }
    public void Dispose() { client.Dispose(); /* In-flight cancellation still releases gate; no wait handle is allocated. */ }
}
