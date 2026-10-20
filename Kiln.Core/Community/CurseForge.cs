using System.Globalization;

namespace Kiln.Core;

/// CurseForge：作为 Modrinth 的补充来源。
/// 无密钥时使用 Curse.tools 公共代理及 MCIM 备用；填写自己的 API 密钥后使用官方接口。
public static class CurseForge
{
    private const string Official = "https://api.curseforge.com";
    private const string Mirror = "https://mod.mcimirror.top/curseforge";
    private const string PublicProxy = "https://api.curse.tools";

    /// 资源类型对应的 CurseForge 分类 ID
    public static int ClassId(Modrinth.ProjectType type) => type switch
    {
        Modrinth.ProjectType.Mod => 6,
        Modrinth.ProjectType.ResourcePack => 12,
        Modrinth.ProjectType.Shader => 6552,
        _ => 4471,
    };

    /// 加载器对应的 modLoaderType
    public static int? LoaderType(string loader) => loader.ToLowerInvariant() switch
    {
        "forge" => 1,
        "fabric" => 4,
        "quilt" => 5,
        "neoforge" => 6,
        _ => null,
    };

    private static async Task<Json> Get(string path, CancellationToken ct = default)
    {
        return await Send(path, LauncherSettings.Shared.CurseforgeKey, "GET", null, ct);
    }

    private static async Task<Json> Post(string path, Json body)
    {
        return await Send(path, LauncherSettings.Shared.CurseforgeKey, "POST", body.Data(false));
    }

    private static async Task<Json> Send(string path, string apiKey, string method, byte[]? body, CancellationToken ct = default)
    {
        var key = apiKey.Trim();
        var official = key.Length > 0;
        var headers = official ? new Dictionary<string, string> { ["x-api-key"] = key } : null;
        var urls = official ? new[] { Official + path } : new[] { PublicProxy + path, Mirror + path };
        for (var index = 0; index < urls.Length; index++)
        {
            try
            {
                var (code, json) = await Net.request(urls[index], method: method,
                    body: body, contentType: body == null ? null : "application/json", headers: headers, ct: ct);
                if (code != 200)
                {
                    if (!official && index + 1 < urls.Length && code is 401 or 403 or 429 or 502 or 503 or 504) continue;
                    throw ServiceError(code, official);
                }
                if (!json["data"].Exists) throw new CurseForgeException(T.Invoke("CurseForge 返回了无法识别的响应，请稍后重试。", "CurseForge returned an invalid response; retry later."));
                return json;
            }
            catch (HttpRequestException) when (index + 1 < urls.Length) { }
        }
        throw ServiceError(503, official);
    }

    public static Task CheckConnection(string apiKey, CancellationToken ct = default) =>
        Send("/v1/mods/238222", apiKey, "GET", null, ct);

    public static CurseForgeException ServiceError(int status, bool official) => new(status switch
    {
        401 or 403 when official => T.Invoke("CurseForge API 密钥无效或无权访问。请在设置 → 下载中检查密钥。", "The CurseForge API key is invalid or lacks access. Check Settings → Downloads."),
        401 or 403 => T.Invoke("CurseForge 镜像拒绝访问。请在设置 → 下载中配置自己的官方 API 密钥。", "The CurseForge mirror denied access. Configure your own official API key in Settings → Downloads."),
        429 => T.Invoke("CurseForge 请求过于频繁，请稍后重试。", "CurseForge rate limit reached. Retry later."),
        502 or 503 or 504 when !official => T.Invoke("CurseForge 公共代理暂时不可用。可稍后重试，或在设置 → 下载中配置官方 API 密钥。", "The public CurseForge services are unavailable. Retry later or configure an official API key in Settings → Downloads."),
        404 => T.Invoke("CurseForge 项目或文件已不存在。", "This CurseForge project or file no longer exists."),
        _ => T.Invoke($"CurseForge 服务暂时不可用（HTTP {status}），请稍后重试。", $"CurseForge is unavailable (HTTP {status}); retry later."),
    });

    /// Recomputed for every redirect; keys never travel to a mirror or unrelated host.
    public static void AuthenticateDownload(HttpRequestMessage request, string apiKey)
    {
        request.Headers.Remove("x-api-key");
        if (request.RequestUri is not { Scheme: "https" } uri || apiKey.Trim().Length == 0) return;
        if (uri.Host is "edge.forgecdn.net" or "mediafilez.forgecdn.net" or "mediafiles.forgecdn.net")
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey.Trim());
    }

    private static string Query(IReadOnlyList<(string Key, string Value)> items) =>
        string.Join("&", items.Select(i => $"{i.Key}={Uri.EscapeDataString(i.Value)}"));

    public static async Task<IReadOnlyList<Modrinth.Project>> Search(string text, Modrinth.ProjectType type,
        string? gameVersion, string? loader, int offset = 0, CancellationToken ct = default)
    {
        var q = new List<(string, string)>
        {
            ("gameId", "432"), ("classId", $"{ClassId(type)}"), ("searchFilter", text),
            ("sortField", "2"), ("sortOrder", "desc"), ("index", $"{offset}"), ("pageSize", "30"),
        };
        if (!string.IsNullOrEmpty(gameVersion)) q.Add(("gameVersion", gameVersion));
        if ((type == Modrinth.ProjectType.Mod || type == Modrinth.ProjectType.Modpack)
            && loader != null && LoaderType(loader) is { } t) q.Add(("modLoaderType", $"{t}"));
        var j = await Get("/v1/mods/search?" + Query(q), ct);
        var list = j["data"].Array.Select(ParseProject).Where(p => p != null).Select(p => p!).ToList();
        // CurseForge 的模糊匹配较宽：在热度顺序基础上，把名称更匹配的排到前面
        var key = text.ToLowerInvariant().Trim();
        if (key.Length == 0) return list;
        int Score(Modrinth.Project p)
        {
            var name = p.TitleText.ToLowerInvariant();
            if (name == key || p.Slug == key.Replace(' ', '-')) return 3;
            if (name.StartsWith(key, StringComparison.Ordinal)) return 2;
            return name.Contains(key, StringComparison.Ordinal) ? 1 : 0;
        }
        return list.Select((p, i) => (p, i))
            .OrderByDescending(x => Score(x.p)).ThenBy(x => x.i)
            .Select(x => x.p).ToList();
    }

    public static Modrinth.Project? ParseProject(Json m)
    {
        if (m["id"].Int is not { } id || m["slug"].String is not { } slug) return null;
        return new Modrinth.Project
        {
            Id = $"cf-{id}", Slug = slug, TitleText = m["name"].String ?? slug, Description = m["summary"].String ?? "",
            IconUrl = m["logo"]["thumbnailUrl"].String,
            Downloads = m["downloadCount"].Int ?? 0,
            Categories = m["categories"].Array.Select(c => c["name"].String ?? "").Where(s => s.Length > 0).ToList(),
            SourceKind = Modrinth.Source.CurseForge,
            PageUrl = m["links"]["websiteUrl"].String,
        };
    }

    /// 某个项目的文件（版本），按游戏版本与加载器筛选
    public static async Task<IReadOnlyList<Modrinth.Version>> Files(Modrinth.Project project, string? gameVersion, string? loader)
    {
        var modId = project.Id.Replace("cf-", "");
        var q = new List<(string, string)> { ("pageSize", "50") };
        if (!string.IsNullOrEmpty(gameVersion)) q.Add(("gameVersion", gameVersion));
        if (loader != null && LoaderType(loader) is { } t) q.Add(("modLoaderType", $"{t}"));
        var j = await Get("/v1/mods/" + modId + "/files?" + Query(q));
        return j["data"].Array.Select(ParseFile).Where(v => v != null).Select(v => v!)
            .OrderByDescending(v => v.Published).ToList();
    }

    /// 解析 CurseForge 的文件对象
    public static Modrinth.Version? ParseFile(Json f)
    {
        if (f["id"].Int is not { } id || f["fileName"].String is not { } name) return null;
        var loaderNames = new HashSet<string> { "forge", "neoforge", "fabric", "quilt" };
        var tags = f["gameVersions"].Array.Select(t => t.String ?? "").Where(s => s.Length > 0).ToList();
        var url = f["downloadUrl"].String ?? "";
        var v = new Modrinth.Version
        {
            Id = $"{id}",
            Name = f["displayName"].String ?? name,
            Number = name,
            GameVersions = tags.Where(t => t.Length > 0 && char.IsDigit(t[0])).ToList(),
            Loaders = tags.Select(t => t.ToLowerInvariant()).Where(loaderNames.Contains).ToList(),
            FileUrl = url,
            FileName = name,
            Sha1 = f["hashes"].Array.FirstOrDefault(h => h["algo"].Int == 1)["value"].String,
            Size = f["fileLength"].Double is { } sz ? (long)sz : null,
            Published = ParseDate(f["fileDate"].String) ?? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Blocked = url.Length == 0,
            SourceKind = Modrinth.Source.CurseForge,
            ProjectId = f["modId"].Int is { } modId ? $"cf-{modId}" : "",
        };
        // relationType：3 必需，2 可选
        foreach (var d in f["dependencies"].Array)
        {
            if (d["modId"].Int is not { } mod || d["relationType"].Int is not { } type
                || (type != 3 && type != 2)) continue;
            v.Dependencies.Add(new Modrinth.Dependency($"cf-{mod}", null, type == 3));
        }
        return v;
    }

    private static DateTime? ParseDate(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.UtcDateTime;
        return null;
    }

    /// 按文件 ID 批量查询（一次请求）
    public static async Task<Dictionary<int, Modrinth.Version>> FilesByIds(IReadOnlyList<int> ids)
    {
        var result = new Dictionary<int, Modrinth.Version>();
        if (ids.Count == 0) return result;
        for (var start = 0; start < ids.Count; start += 500)
        {
            var chunk = ids.Skip(start).Take(500).ToList();
            var j = await Post("/v1/mods/files",
                Json.From(new Dictionary<string, Json> { ["fileIds"] = Json.From(chunk.Select(i => Json.Of((double)i)).ToList()) }));
            foreach (var f in j["data"].Array)
                if (f["id"].Int is { } fid && ParseFile(f) is { } v) result[(int)fid] = v;
        }
        return result;
    }

    /// 按项目 ID 批量查询（一次请求）
    public static async Task<Dictionary<int, (Modrinth.Project Project, int ClassId)>> ProjectsByIds(IReadOnlyList<int> ids)
    {
        var result = new Dictionary<int, (Modrinth.Project, int)>();
        if (ids.Count == 0) return result;
        var j = await Post("/v1/mods",
            Json.From(new Dictionary<string, Json> { ["modIds"] = Json.From(ids.Select(i => Json.Of((double)i)).ToList()) }));
        foreach (var m in j["data"].Array)
            if (m["id"].Int is { } mid && ParseProject(m) is { } p)
                result[(int)mid] = (p, (int)(m["classId"].Int ?? 6));
        return result;
    }

    /// 按指纹查找文件（用于识别本地 Mod）：返回 指纹 → 文件
    public static async Task<Dictionary<uint, Modrinth.Version>> Match(IReadOnlyList<uint> fingerprints)
    {
        var result = new Dictionary<uint, Modrinth.Version>();
        if (fingerprints.Count == 0) return result;
        var j = await Post("/v1/fingerprints/432",
            Json.From(new Dictionary<string, Json>
            {
                ["fingerprints"] = Json.From(fingerprints.Select(fp => Json.Of((double)fp)).ToList()),
            }));
        foreach (var m in j["data"]["exactMatches"].Array)
        {
            var f = m["file"];
            if (f["fileFingerprint"].Double is { } fp && ParseFile(f) is { } v)
                result[(uint)fp] = v;
        }
        return result;
    }

    /// CurseForge 的文件指纹：去掉空白字节后的 MurmurHash2（种子 1）
    public static uint Fingerprint(byte[] data)
    {
        // 去掉 \t \n \r 与空格
        var bytes = data.Where(b => b != 9 && b != 10 && b != 13 && b != 32).ToArray();
        const uint m = 0x5bd1_e995;
        var len = bytes.Length;
        unchecked
        {
            var h = 1 ^ (uint)len;
            var i = 0;
            while (i + 4 <= len)
            {
                var k = bytes[i] | (uint)bytes[i + 1] << 8 | (uint)bytes[i + 2] << 16 | (uint)bytes[i + 3] << 24;
                k *= m;
                k ^= k >> 24;
                k *= m;
                h *= m;
                h ^= k;
                i += 4;
            }
            var rest = len - i;
            if (rest >= 3) h ^= (uint)bytes[i + 2] << 16;
            if (rest >= 2) h ^= (uint)bytes[i + 1] << 8;
            if (rest >= 1)
            {
                h ^= bytes[i];
                h *= m;
            }
            h ^= h >> 13;
            h *= m;
            h ^= h >> 15;
            return h;
        }
    }

    /// 项目网页
    public static string PageUrl(int classId, string slug)
    {
        var path = classId switch
        {
            12 => "texture-packs",
            6552 => "shaders",
            4471 => "modpacks",
            _ => "mc-mods",
        };
        return $"https://www.curseforge.com/minecraft/{path}/{slug}";
    }

    /// 下载地址候选：官方 CDN，以及 MCIM 的文件镜像
    public static IReadOnlyList<string> DownloadUrls(Modrinth.Version v)
    {
        if (v.FileUrl.Length == 0) return Array.Empty<string>();
        var urls = new List<string> { v.FileUrl };
        var marker = "https://edge.forgecdn.net/";
        var i = v.FileUrl.IndexOf(marker, StringComparison.Ordinal);
        if (i >= 0) urls.Add("https://mod.mcimirror.top/" + v.FileUrl[(i + marker.Length)..]);
        return urls;
    }
}

public sealed class CurseForgeException(string message) : Exception(message);
