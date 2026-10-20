using System.Text.Json.Serialization;

namespace Kiln.Core;

/// 外置登录（authlib-injector / Yggdrasil 协议），如 LittleSkin。
/// 只保存服务器下发的令牌（本机的 Tokens.json），不保存密码；令牌失效时再请用户输入密码。
public static class Yggdrasil
{
    public sealed record Profile(string Id, string Name);

    public sealed record Session(string AccessToken, string ClientToken, IReadOnlyList<Profile> Profiles, Profile? Selected);

    /// 常用的皮肤站
    public static readonly (string Name, string Url)[] Presets = { ("LittleSkin", "https://littleskin.cn/api/yggdrasil") };

    /// 解析用户输入的地址：补全 https，按 authlib-injector 的 API 地址指示（ALI）找到真正的 API 根地址，并读取元数据
    public static async Task<(string Root, Json Meta)> Resolve(string input)
    {
        var s = input.Trim();
        if (!s.ToLowerInvariant().StartsWith("http")) s = "https://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var url))
            throw new LauncherException(T.Invoke("地址无效", "Invalid address"));
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var resp = await Net.Session.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (resp.Headers.TryGetValues("X-Authlib-Injector-API-Location", out var values)
                && values.FirstOrDefault() is { } ali
                && Uri.TryCreate(ali, UriKind.Absolute, out var resolved))
            {
                // 不接受从 https 降级到 http
                if (!(url.Scheme == "https" && resolved.Scheme == "http")) url = resolved;
            }
        }
        catch { }
        var root = url.AbsoluteUri;
        while (root.EndsWith('/')) root = root[..^1];
        var meta = await Net.json(new[] { root });
        if (!meta["meta"].Exists && !meta["skinDomains"].Exists)
            throw new LauncherException(T.Invoke("这个地址不是 authlib-injector 认证服务器",
                "This address isn't an authlib-injector authentication server"));
        return (root, meta);
    }

    public static string ServerName(Json meta, string root) =>
        meta["meta"]["serverName"].String ?? (Uri.TryCreate(root, UriKind.Absolute, out var u) ? u.Host : root);

    private static Task<(int Code, Json Body)> Post(string url, Json body) =>
        Net.request(url, body: body.Data(false), contentType: "application/json");

    private static void Check(int code, Json j)
    {
        if (code is >= 200 and < 300) return;
        var msg = j["errorMessage"].String ?? j["error"].String ?? $"HTTP {code}";
        if (j["error"].String == "ForbiddenOperationException")
            throw new LauncherException(T.Invoke($"账号或密码错误，或登录已失效：{msg}",
                $"Wrong email or password, or the session expired: {msg}"));
        throw new LauncherException(msg);
    }

    private static List<Profile> ParseProfiles(Json j)
    {
        var result = new List<Profile>();
        foreach (var p in j.Array)
            if (p["id"].String is { } id && p["name"].String is { } name)
                result.Add(new Profile(id, name));
        return result;
    }

    /// 用账号密码登录（密码只用于这一次请求）
    public static async Task<Session> Authenticate(string root, string username, string password)
    {
        var body = Json.From(new Dictionary<string, Json>
        {
            ["agent"] = Json.From(new Dictionary<string, Json> { ["name"] = Json.Of("Minecraft"), ["version"] = Json.Of(1) }),
            ["username"] = Json.Of(username),
            ["password"] = Json.Of(password),
            ["clientToken"] = Json.Of(Guid.NewGuid().ToString("N")),
            ["requestUser"] = Json.Of(false),
        });
        var (code, j) = await Post(root + "/authserver/authenticate", body);
        Check(code, j);
        if (j["accessToken"].String is not { } access) throw new LauncherException(T.Invoke("登录失败", "Sign-in failed"));
        var selected = j["selectedProfile"]["id"].String is { } id
            ? new Profile(id, j["selectedProfile"]["name"].String ?? "") : null;
        return new Session(access, j["clientToken"].String ?? "", ParseProfiles(j["availableProfiles"]), selected);
    }

    /// 刷新令牌；可同时选择角色
    public static async Task<Session> Refresh(string root, string accessToken, string clientToken, Profile? select = null)
    {
        var body = new Dictionary<string, Json>
        {
            ["accessToken"] = Json.Of(accessToken),
            ["clientToken"] = Json.Of(clientToken),
            ["requestUser"] = Json.Of(false),
        };
        if (select != null)
            body["selectedProfile"] = Json.From(new Dictionary<string, Json>
            {
                ["id"] = Json.Of(select.Id),
                ["name"] = Json.Of(select.Name),
            });
        var (code, j) = await Post(root + "/authserver/refresh", Json.From(body));
        Check(code, j);
        if (j["accessToken"].String is not { } access) throw new LauncherException(T.Invoke("登录失败", "Sign-in failed"));
        var selected = j["selectedProfile"]["id"].String is { } id
            ? new Profile(id, j["selectedProfile"]["name"].String ?? "") : select;
        return new Session(access, j["clientToken"].String ?? clientToken, Array.Empty<Profile>(), selected);
    }

    /// 令牌是否仍然有效
    public static async Task<bool> Validate(string root, string accessToken, string clientToken)
    {
        var body = Json.From(new Dictionary<string, Json>
        {
            ["accessToken"] = Json.Of(accessToken),
            ["clientToken"] = Json.Of(clientToken),
        });
        try
        {
            var (code, _) = await Post(root + "/authserver/validate", body);
            return code == 204 || code == 200;
        }
        catch { return false; }
    }
}

// MARK: - authlib-injector

public static class AuthlibInjector
{
    private static string Jar => Path.Combine(Paths.Support, "authlib-injector.jar");

    /// 获取 authlib-injector（每天最多检查一次新版本），返回 jar 位置
    public static async Task<string> Ensure()
    {
        var stamp = Path.Combine(Paths.Support, "authlib-injector.checked");
        if (File.Exists(Jar) && File.Exists(stamp)
            && (DateTime.UtcNow - File.GetLastWriteTimeUtc(stamp)).TotalSeconds < 24 * 3600)
            return Jar;
        try
        {
            var info = await Net.json(new[]
            {
                "https://authlib-injector.yushi.moe/artifact/latest.json",
                "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/latest.json",
            });
            if (info["download_url"].String is not { } url) throw new LauncherException("authlib-injector");
            var sha256 = info["checksums"]["sha256"].String?.ToLowerInvariant();
            if (!(File.Exists(Jar) && sha256 != null && Hash.Sha256File(Jar) == sha256))
            {
                var tmp = Jar + ".download";
                try { File.Delete(tmp); } catch { }
                var item = new DownloadItem(url, tmp)
                {
                    Urls = new[] { url, url.Replace("https://authlib-injector.yushi.moe/", "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/") },
                };
                await Downloader.Fetch(item);
                if (sha256 != null && Hash.Sha256File(tmp) != sha256)
                {
                    try { File.Delete(tmp); } catch { }
                    throw new LauncherException(T.Invoke("authlib-injector 校验失败", "authlib-injector failed verification"));
                }
                try { File.Delete(Jar); } catch { }
                File.Move(tmp, Jar);
                Log.log($"[Auth] 已下载 authlib-injector {info["version"].String ?? ""}");
            }
            try { File.WriteAllBytes(stamp, Array.Empty<byte>()); } catch { }
        }
        catch (Exception e)
        {
            // 网络不可用时继续使用已有的版本
            if (File.Exists(Jar)) return Jar;
            throw;
        }
        return Jar;
    }

    /// 启动参数：加载 authlib-injector 并指定认证服务器（附带预先取得的元数据，省去一次请求）
    public static async Task<List<string>> JvmArgs(string root, Json? meta)
    {
        var jar = await Ensure();
        var args = new List<string> { $"-javaagent:{jar}={root}" };
        if (meta is { } m)
            args.Add($"-Dauthlibinjector.yggdrasil.prefetched={Convert.ToBase64String(m.Data(false))}");
        return args;
    }
}
