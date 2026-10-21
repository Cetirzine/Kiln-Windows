using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Kiln.Core;

public static class Net
{
    public static readonly string UserAgent = $"{AppInfo.ShortName}/{AppInfo.Version} (+https://github.com/{Updater.Repository})";

    /// 对应原版 URLSession：默认 20 秒超时（按请求计算）、UA、每主机 16 连接
    public static readonly HttpClient Session = CreateSession(16);

    internal static HttpClient CreateSession(int maxConnectionsPerHost) =>
        new(new SocketsHttpHandler
        {
            MaxConnectionsPerServer = maxConnectionsPerHost,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            AutomaticDecompression = DecompressionMethods.All,
        });

    // MARK: 镜像源（BMCLAPI）

    public enum Kind { VersionList, File }

    private static readonly (string From, string To)[] MirrorRules =
    {
        ("https://piston-meta.mojang.com/", "https://bmclapi2.bangbang93.com/"),
        ("https://launchermeta.mojang.com/", "https://bmclapi2.bangbang93.com/"),
        ("https://piston-data.mojang.com/", "https://bmclapi2.bangbang93.com/"),
        ("https://launcher.mojang.com/", "https://bmclapi2.bangbang93.com/"),
        ("https://libraries.minecraft.net/", "https://bmclapi2.bangbang93.com/maven/"),
        ("https://resources.download.minecraft.net/", "https://bmclapi2.bangbang93.com/assets/"),
        ("https://maven.fabricmc.net/", "https://bmclapi2.bangbang93.com/maven/"),
        ("https://meta.fabricmc.net/", "https://bmclapi2.bangbang93.com/fabric-meta/"),
        ("https://maven.quiltmc.org/repository/release/", "https://bmclapi2.bangbang93.com/maven/"),
        ("https://meta.quiltmc.org/", "https://bmclapi2.bangbang93.com/quilt-meta/"),
        ("https://maven.minecraftforge.net/", "https://bmclapi2.bangbang93.com/maven/"),
        ("https://maven.neoforged.net/releases/", "https://bmclapi2.bangbang93.com/maven/"),
    };

    public static string? Mirror(string url)
    {
        foreach (var (from, to) in MirrorRules)
            if (url.StartsWith(from, StringComparison.Ordinal))
                return to + url[from.Length..];
        return null;
    }

    /// 根据下载源设置给出候选地址（按尝试顺序）
    public static IReadOnlyList<string> Candidates(string url, Kind kind)
    {
        var m = Mirror(url);
        if (m == null) return new[] { url };
        var source = kind == Kind.VersionList
            ? LauncherSettings.Shared.VersionListSource
            : LauncherSettings.Shared.FileSource;
        return source == DownloadSource.MirrorFirst ? new[] { m, url } : new[] { url, m };
    }

    // MARK: 请求

    private static readonly IReadOnlyDictionary<string, string> NoHeaders = new Dictionary<string, string>();

    /// fresh：跳过本地 HTTP 缓存（用于检查更新等需要最新结果的请求；.NET 的 HttpClient 本身无缓存，参数保留以对应原版）
    public static async Task<byte[]> data(IReadOnlyList<string> urls, double? timeout = null,
        IReadOnlyDictionary<string, string>? headers = null, bool fresh = false, CancellationToken ct = default)
    {
        Exception lastError = new LauncherException("No URL");
        double? firstTimeout = timeout;
        if (firstTimeout == null && urls.Count > 1
            && LauncherSettings.Shared.VersionListSource == DownloadSource.OfficialSlowFallback)
        {
            // “仅在官方源加载缓慢时改用镜像源”：第一个地址只给较短的超时
            firstTimeout = 8;
        }
        for (var retry = 0; retry < 3; retry++)
        {
        for (var i = 0; i < urls.Count; i++)
        {
            var u = urls[i];
            if (!Uri.TryCreate(u, UriKind.Absolute, out var url)) continue;
            try
            {
                var total = i == 0 && firstTimeout is double ft ? ft : 20;
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(total));
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.UserAgent.ParseAdd(UserAgent);
                req.Headers.TryAddWithoutValidation("Accept", "application/json, */*");
                foreach (var (k, v) in headers ?? NoHeaders)
                    req.Headers.TryAddWithoutValidation(k, v);
                using var resp = await Session.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token);
                if (!resp.IsSuccessStatusCode)
                    throw new HttpRequestException($"HTTP {(int)resp.StatusCode}: {u}", null, resp.StatusCode);
                return await resp.Content.ReadAsByteArrayAsync(cts.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                Log.log($"[Net] 请求失败 {u}：{e.Friendly()}");
                lastError = e;
            }
        }

            if (!IsTransient(lastError) || retry == 2) break;
            await Task.Delay(TimeSpan.FromMilliseconds(250 * (retry + 1)), ct);
        }
        throw lastError;
    }

    public static Task<Json> json(string url, Kind kind = Kind.VersionList, CancellationToken ct = default) =>
        json(Candidates(url, kind), ct);

    public static async Task<Json> json(IReadOnlyList<string> urls, CancellationToken ct = default) =>
        Json.Parse(await data(urls, ct: ct));

    /// 发送 JSON / 表单请求（用于正版登录）
    public static async Task<(int Code, Json Body)> request(string url, string method = "POST", byte[]? body = null,
        string? contentType = null, IReadOnlyDictionary<string, string>? headers = null, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u))
            throw new LauncherException($"Bad URL {url}");
        HttpRequestMessage NewRequest() // 响应处理后 HttpRequestMessage 不可复用，每次尝试重建
        {
            var req = new HttpRequestMessage(new HttpMethod(method), u);
            req.Headers.UserAgent.ParseAdd(UserAgent);
            if (body != null) req.Content = new ByteArrayContent(body);
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            if (contentType != null && req.Content != null)
                req.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            foreach (var (k, v) in headers ?? NoHeaders)
                req.Headers.TryAddWithoutValidation(k, v);
            return req;
        }

        // 本地代理等环境下，复用的空闲连接可能已被关闭；.NET 不会自动重试已发送 body 的请求，这里重试临时性网络错误
        var attempt = 0;
        while (true)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(30));
                using var req = NewRequest();
                using var resp = await Session.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token);
                var code = (int)resp.StatusCode;
                if (code >= 400) Log.log($"[Net] {method} {u.Host} → HTTP {code}");
                var bytes = await resp.Content.ReadAsByteArrayAsync(cts.Token);
                return (code, Json.TryParse(bytes) ?? Json.Null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                attempt++;
                Log.log($"[Net] {method} {u.Host} 失败（第 {attempt} 次）：{e.Friendly()}");
                if (attempt >= 4 || !IsTransient(e)) throw;
                await Task.Delay(TimeSpan.FromMilliseconds(attempt * 500), ct);
            }
        }
    }

    /// 系统是否设置了 HTTP / HTTPS 代理
    public static bool SystemProxyEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
                return key?.GetValue("ProxyEnable") is int enabled && enabled == 1;
            }
            catch { return false; }
        }
    }

    /// 连接中断、超时等可以重试的网络错误
    public static bool IsTransient(Exception error) => error switch
    {
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout } => true,
        HttpRequestException { InnerException: SocketException } => true,
        HttpRequestException { InnerException: TimeoutException } => true,
        // HttpClient 超时表现为未带外部令牌的 TaskCanceledException；用户主动取消不算
        TaskCanceledException => true,
        _ => false,
    };

    public static byte[] formBody(IReadOnlyDictionary<string, string> items)
    {
        // EscapeDataString 恰好只保留 RFC 3986 非保留字符（字母数字与 -._~），与原版一致
        var s = string.Join("&", items.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        return Encoding.UTF8.GetBytes(s);
    }
}

public static class Hash
{
    public static string? Sha1File(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            return Convert.ToHexString(SHA1.HashData(fs)).ToLowerInvariant();
        }
        catch { return null; }
    }

    public static byte[] Md5(byte[] data) => MD5.HashData(data);

    public static string? Sha256File(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        }
        catch { return null; }
    }
}

// MARK: - 文件下载

/// 单个需要下载的文件
public sealed class DownloadItem
{
    public IReadOnlyList<string> Urls;
    public string Destination;
    public string? Sha1;
    public long? Size;

    public DownloadItem(string url, string destination, string? sha1 = null, long? size = null, Net.Kind kind = Net.Kind.File)
    {
        Urls = Net.Candidates(url, kind);
        Destination = destination;
        Sha1 = sha1?.ToLowerInvariant();
        Size = size;
    }

    /// 本地文件是否已存在且完整
    public bool IsValid(bool checkHash = true)
    {
        FileInfo info;
        try { info = new FileInfo(Destination); } catch { return false; }
        if (!info.Exists) return false;
        var fsize = info.Length;
        if (Size is long size && size > 0 && fsize != size) return false;
        if (fsize == 0 && (Size ?? 1) != 0) return false;
        if (checkHash &&!string.IsNullOrEmpty(Sha1))
        {
            // 快速校验：文件的大小与修改时间未变时，沿用上次的校验结果，避免重复计算 SHA1
            var mtime = (long)(info.LastWriteTimeUtc - DateTime.UnixEpoch).TotalSeconds;
            var stamp = $"{fsize}:{mtime}:{Sha1}";
            if (LauncherSettings.Shared.FastVerify && VerifyCache.Shared.Get(Destination) == stamp) return true;
            if (Hash.Sha1File(Destination) != Sha1) return false;
            VerifyCache.Shared.Set(Destination, stamp);
            return true;
        }
        return true;
    }
}

/// 已校验文件的记录（路径 → 大小:修改时间:SHA1）
public sealed class VerifyCache
{
    public static readonly VerifyCache Shared = new();
    private readonly object _gate = new();
    private Dictionary<string, string> _table = new(StringComparer.OrdinalIgnoreCase);
    private bool _dirty;
    private static readonly string CacheFile = Path.Combine(Paths.Cache, "verified.json");

    private VerifyCache()
    {
        try
        {
            if (File.Exists(CacheFile))
                _table = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(CacheFile))
                    ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch { }
    }

    public string? Get(string path) { lock (_gate) return _table.TryGetValue(path, out var v) ? v : null; }

    public void Set(string path, string stamp) { lock (_gate) { _table[path] = stamp; _dirty = true; } }

    public void Save()
    {
        lock (_gate)
        {
            if (!_dirty) return;
            try
            {
                var tmp = CacheFile + ".tmp";
                File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(_table));
                File.Move(tmp, CacheFile, overwrite: true);
                _dirty = false;
            }
            catch (Exception e)
            {
                Log.log($"[VerifyCache] 保存失败：{e.Friendly()}");
            }
        }
    }
}

/// 下载进度计数器（线程安全）
public sealed class DownloadCounter
{
    private long _bytes;
    private long _done;
    public int Total;
    public long TotalBytes;

    public void AddBytes(long n) => Interlocked.Add(ref _bytes, n);
    public void FinishOne() => Interlocked.Increment(ref _done);
    public long Bytes => Interlocked.Read(ref _bytes);
    public int Done => (int)Interlocked.Read(ref _done);
}

/// 基于 HttpClient 流式复制的下载引擎，支持字节级进度（对应原版 URLSessionDownloadDelegate）
public sealed class DownloadEngine
{
    public static readonly DownloadEngine Shared = new();

    private readonly HttpClient _client;

    private DownloadEngine()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            MaxConnectionsPerServer = 32,
            ConnectTimeout = TimeSpan.FromSeconds(30),
            AutomaticDecompression = DecompressionMethods.All,
        };
        _client = new HttpClient(handler);
        _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", Net.UserAgent);
    }

    /// 下载到临时文件并返回其位置
    public async Task<string> Download(Uri url, DownloadCounter? counter, CancellationToken ct, TimeSpan? idleTimeout = null)
    {
        long written = 0;
        var tmp = Path.Combine(Paths.Cache, "dl-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var resp = await OpenDownload(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                if (resp.RequestMessage?.RequestUri?.Host is "edge.forgecdn.net" or "mediafilez.forgecdn.net" or "mediafiles.forgecdn.net")
                    throw CurseForge.ServiceError((int)resp.StatusCode, true);
                throw new HttpRequestException($"HTTP {(int)resp.StatusCode}: {url}", null, resp.StatusCode);
            }
            await using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            var buf = new byte[1 << 16];
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
            while (true)
            {
                idle.CancelAfter(idleTimeout ?? TimeSpan.FromSeconds(30));
                int n;
                try { n = await src.ReadAsync(buf.AsMemory(0, buf.Length), idle.Token); }
                catch (OperationCanceledException error) when (!ct.IsCancellationRequested) { throw new TimeoutException(T.Invoke("下载连接未继续传输数据", "The download connection stopped sending data"), error); }
                idle.CancelAfter(Timeout.InfiniteTimeSpan);
                if (n <= 0) break;
                await fs.WriteAsync(buf.AsMemory(0, n), ct);
                written += n;
                counter?.AddBytes(n);
            }
            await fs.FlushAsync(ct);
        }
        catch (OperationCanceledException error) when (!ct.IsCancellationRequested)
        {
            try { File.Delete(tmp); } catch { }
            counter?.AddBytes(-written);
            throw new TimeoutException(T.Invoke("下载连接超时", "The download connection timed out"), error);
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            counter?.AddBytes(-written);
            throw;
        }
        return tmp;
    }

    private async Task<HttpResponseMessage> OpenDownload(Uri url, CancellationToken ct)
    {
        for (var redirects = 0; redirects <= 8; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            CurseForge.AuthenticateDownload(request, LauncherSettings.Shared.CurseforgeKey);
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)) return response;
            var next = response.Headers.Location;
            response.Dispose();
            if (next == null) throw new LauncherException("Download redirect has no destination");
            url = next.IsAbsoluteUri ? next : new Uri(url, next);
            if (url.Scheme is not ("https" or "http")) throw new LauncherException("Unsupported download redirect");
        }
        throw new LauncherException("Too many download redirects");
    }
}

public static class Downloader
{
    /// 并发下载一组文件；已存在且校验通过的文件会被跳过
    public static async Task Run(IReadOnlyList<DownloadItem> items, Job? job = null, int? threads = null,
        bool checkHash = true, CancellationToken ct = default)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, job?.Token ?? CancellationToken.None);
        ct = cancellation.Token;
        ct.ThrowIfCancellationRequested();
        if (threads == null) threads = LauncherSettings.Shared.DownloadThreads;

        // 去重
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = items.Where(i => seen.Add(i.Destination)).ToList();

        // 先过滤掉已完成的文件（8 路并发校验）
        var todo = new ConcurrentBag<DownloadItem>();
        using (var sem = new SemaphoreSlim(8))
        {
            await Task.WhenAll(unique.Select(async item =>
            {
                await sem.WaitAsync(ct);
                try { if (!item.IsValid(checkHash)) todo.Add(item); }
                finally { sem.Release(); }
            }));
        }
        if (todo.IsEmpty) { VerifyCache.Shared.Save(); return; }
        var todoList = todo.ToList();
        Log.log($"[Download] 需要下载 {todoList.Count} 个文件");

        var counter = new DownloadCounter
        {
            Total = todoList.Count,
            TotalBytes = todoList.Aggregate(0L, (sum, i) => sum + (i.Size ?? 0)),
        };

        using var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var monitor = Task.Run(async () =>
        {
            long lastBytes = 0;
            var lastTime = DateTime.UtcNow;
            while (!monitorCts.IsCancellationRequested)
            {
                var b = counter.Bytes;
                var now = DateTime.UtcNow;
                var dt = (now - lastTime).TotalSeconds;
                if (dt > 0.4 && job != null)
                {
                    job.Speed = (b - lastBytes) / dt;
                    lastBytes = b;
                    lastTime = now;
                }
                if (job != null)
                {
                    if (counter.TotalBytes > 0)
                        job.FileFraction = Math.Min(1, (double)b / counter.TotalBytes);
                    else
                        job.FileFraction = (double)counter.Done / Math.Max(1, counter.Total);
                    job.FileText = $"{counter.Done} / {counter.Total}";
                }
                try { await Task.Delay(250, monitorCts.Token); }
                catch (OperationCanceledException) { break; }
            }
        }, CancellationToken.None);
        try
        {
            // 第一轮：按设置的并发数下载；个别文件失败时先记下，不立即让整个任务失败
            var failed = await Pass(todoList, threads.Value, null, counter, ct);
            // 第二轮：网络拥塞（例如经过本地代理）时，降低并发、增加重试次数再试一次
            var permanent = failed.Where(f => f.Item2 is CurseForgeException).ToList();
            failed = failed.Where(f => f.Item2 is not CurseForgeException).ToList();
            if (failed.Count > 0)
            {
                Log.log($"[Download] {failed.Count} 个文件下载失败，降低并发后重试");
                failed = await Pass(failed.Select(f => f.Item1).ToList(), 4, 6, counter, ct);
            }
            failed.AddRange(permanent);
            if (failed.Count > 0)
            {
                var (item, error) = failed[0];
                throw new LauncherException(T.Invoke(
                    $"下载 {Path.GetFileName(item.Destination)} 失败：{error.Friendly()}（共 {failed.Count} 个文件未能下载，请检查网络或在设置中切换下载源）",
                    $"Failed to download {Path.GetFileName(item.Destination)}: {error.Friendly()} ({failed.Count} file(s) couldn't be downloaded; check your connection or switch the download source in Settings)"));
            }
            if (job != null) { job.FileFraction = 1; job.Speed = 0; }
        }
        finally
        {
            monitorCts.Cancel();
            try { await monitor; } catch { }
            VerifyCache.Shared.Save();
        }
    }

    /// 并发下载一组文件，返回失败的文件与错误；取消时抛出
    private static async Task<List<(DownloadItem, Exception)>> Pass(IReadOnlyList<DownloadItem> items, int threads,
        int? attempts, DownloadCounter counter, CancellationToken ct)
    {
        var failed = new List<(DownloadItem, Exception)>();
        var failedGate = new object();
        var next = 0;
        var workers = Math.Max(1, Math.Min(threads, items.Count));

        await Task.WhenAll(Enumerable.Range(0, workers).Select(async _ =>
        {
            while (true)
            {
                var i = Interlocked.Increment(ref next) - 1;
                if (i >= items.Count) return;
                var item = items[i];
                try
                {
                    await Fetch(item, counter, attempts, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    lock (failedGate) failed.Add((item, e));
                }
            }
        }));
        return failed;
    }

    public static async Task Fetch(DownloadItem item, DownloadCounter? counter = null, int? attempts = null,
        CancellationToken ct = default)
    {
        var lastError = (Exception)new LauncherException($"No URL for {Path.GetFileName(item.Destination)}");
        var total = attempts ?? Math.Max(4, item.Urls.Count * 2);
        for (var attempt = 0; attempt < total; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            // 重试前稍作等待，逐次加长
            if (attempt > 0) await Task.Delay(Math.Min(attempt, 4) * 700, ct);
            var u = item.Urls[attempt % item.Urls.Count];
            if (!Uri.TryCreate(u, UriKind.Absolute, out var url)) continue;
            if (LauncherSettings.Shared.DebugLog) Log.log($"[Download] 开始 {u}");
            try
            {
                var tmp = await DownloadEngine.Shared.Download(url, counter, ct);
                try
                {
                    if (!string.IsNullOrEmpty(item.Sha1))
                    {
                        var actual = Hash.Sha1File(tmp);
                        if (actual == null || actual != item.Sha1)
                            throw new LauncherException($"SHA1 mismatch: {Path.GetFileName(item.Destination)}");
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(item.Destination)!);
                    if (File.Exists(item.Destination)) File.Delete(item.Destination);
                    File.Move(tmp, item.Destination);
                    if (!string.IsNullOrEmpty(item.Sha1))
                    {
                        var info = new FileInfo(item.Destination);
                        var mtime = (long)(info.LastWriteTimeUtc - DateTime.UnixEpoch).TotalSeconds;
                        VerifyCache.Shared.Set(item.Destination, $"{info.Length}:{mtime}:{item.Sha1}");
                    }
                    counter?.FinishOne();
                    return;
                }
                finally
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (CurseForgeException) { throw; }
            catch (Exception e)
            {
                lastError = e;
                Log.log($"[Download] 下载失败（第 {attempt + 1} 次）{u}：{e.Friendly()}");
            }
        }
        throw new LauncherException(T.Invoke(
            $"下载 {Path.GetFileName(item.Destination)} 失败：{lastError.Friendly()}",
            $"Failed to download {Path.GetFileName(item.Destination)}: {lastError.Friendly()}"));
    }
}

