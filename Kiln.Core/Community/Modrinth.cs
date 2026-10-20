using System.Globalization;

namespace Kiln.Core;

/// Modrinth 社区资源（Mod / 资源包 / 光影）
public static class Modrinth
{
    public const string Api = "https://api.modrinth.com/v2";

    public enum ProjectType { Mod, ResourcePack, Shader, Modpack }

    public enum Source { Modrinth, CurseForge }

    public static string Id(this ProjectType t) => t switch
    {
        ProjectType.Mod => "mod",
        ProjectType.ResourcePack => "resourcepack",
        ProjectType.Shader => "shader",
        _ => "modpack",
    };

    public static string Title(this ProjectType t) => t switch
    {
        ProjectType.Mod => "Mod",
        ProjectType.ResourcePack => T.Invoke("资源包", "Resource packs"),
        ProjectType.Shader => T.Invoke("光影", "Shaders"),
        _ => T.Invoke("整合包", "Modpacks"),
    };

    public static string Folder(this ProjectType t) => t switch
    {
        ProjectType.Mod => "mods",
        ProjectType.ResourcePack => "resourcepacks",
        ProjectType.Shader => "shaderpacks",
        _ => "",
    };

    public sealed class Project
    {
        public string Id = "";
        public string Slug = "";
        public string TitleText = "";
        public string Description = "";
        public string? IconUrl;
        public long Downloads;
        public List<string> Categories = new();
        public Source SourceKind = Source.Modrinth;
        /// 项目网页
        public string? PageUrl;
    }

    public sealed class Version
    {
        public string Id = "";
        public string Name = "";
        public string Number = "";
        public List<string> GameVersions = new();
        public List<string> Loaders = new();
        public string FileUrl = "";
        public string FileName = "";
        public string? Sha1;
        public long? Size;
        public DateTime Published;
        /// 作者禁止第三方下载（CurseForge），需要到网站手动下载
        public bool Blocked;
        /// 所属项目（Modrinth 项目 ID，CurseForge 为 "cf-<modId>"）
        public string ProjectId = "";
        public List<Dependency> Dependencies = new();
        public Source SourceKind = Source.Modrinth;
    }

    /// 依赖：必需的前置，或可选的联动
    public sealed record Dependency(string ProjectId, string? VersionId, bool Required);

    internal static string Encode(string v) => Uri.EscapeDataString(v);

    /// 某个加载器能使用的 Mod 类别：Quilt 可以加载大部分 Fabric Mod
    public static IReadOnlyList<string> CompatibleLoaders(string loader)
    {
        var l = loader.ToLowerInvariant();
        return l == "quilt" ? new[] { "quilt", "fabric" } : new[] { l };
    }

    public static async Task<IReadOnlyList<Project>> Search(string query, ProjectType type, string? gameVersion,
        string? loader, int offset = 0, CancellationToken ct = default)
    {
        var facets = new List<string> { $"[\"project_type:{type.Id()}\"]" };
        if (!string.IsNullOrEmpty(gameVersion)) facets.Add($"[\"versions:{gameVersion}\"]");
        // 同一层中的多个条件为“或”：Quilt 同时包含 Fabric Mod
        if ((type == ProjectType.Mod || type == ProjectType.Modpack) && !string.IsNullOrEmpty(loader))
            facets.Add("[" + string.Join(",", CompatibleLoaders(loader).Select(l => $"\"categories:{l}\"")) + "]");
        var url = $"{Api}/search?query={Encode(query)}&limit=30&offset={offset}&index=relevance&facets={Encode("[" + string.Join(",", facets) + "]")}";
        var j = await Net.json(new[] { url }, ct);
        var result = new List<Project>();
        foreach (var h in j["hits"].Array)
        {
            if (h["project_id"].String is not { } id) continue;
            result.Add(new Project
            {
                Id = id, Slug = h["slug"].String ?? id, TitleText = h["title"].String ?? id,
                Description = h["description"].String ?? "", IconUrl = h["icon_url"].String,
                Downloads = h["downloads"].Int ?? 0,
                Categories = h["categories"].Array.Select(c => c.String ?? "").Where(s => s.Length > 0).ToList(),
            });
        }
        return result;
    }

    /// 按 slug 批量获取项目（用于中文名搜索），按给定顺序返回，并按游戏版本与加载器筛选
    public static async Task<IReadOnlyList<Project>> Projects(IReadOnlyList<string> slugs, string? gameVersion, string? loader, CancellationToken ct = default)
    {
        if (slugs.Count == 0) return Array.Empty<Project>();
        var ids = "[" + string.Join(",", slugs.Take(60).Select(s => $"\"{s}\"")) + "]";
        var j = await Net.json(new[] { $"{Api}/projects?ids={Encode(ids)}" }, ct);
        var allowed = loader != null ? new HashSet<string>(CompatibleLoaders(loader)) : null;
        var bySlug = new Dictionary<string, Project>();
        foreach (var p in j.Array)
        {
            if (p["id"].String is not { } id || p["slug"].String is not { } slug) continue;
            var loaders = p["loaders"].Array.Select(l => l.String ?? "").Where(s => s.Length > 0).ToList();
            if (!string.IsNullOrEmpty(gameVersion)
                && !p["game_versions"].Array.Select(g => g.String).Contains(gameVersion)) continue;
            if (allowed != null && loaders.All(l => !allowed.Contains(l))) continue;
            bySlug[slug] = new Project
            {
                Id = id, Slug = slug, TitleText = p["title"].String ?? slug, Description = p["description"].String ?? "",
                IconUrl = p["icon_url"].String, Downloads = p["downloads"].Int ?? 0,
                Categories = p["categories"].Array.Select(c => c.String ?? "").Where(s => s.Length > 0).Concat(loaders).ToList(),
            };
        }
        return slugs.Where(bySlug.ContainsKey).Select(s => bySlug[s]).ToList();
    }

    public static async Task<IReadOnlyList<Version>> Versions(string projectId, string? gameVersion, string? loader)
    {
        var url = $"{Api}/project/{projectId}/version";
        var q = new List<string>();
        if (!string.IsNullOrEmpty(gameVersion)) q.Add("game_versions=" + Encode($"[\"{gameVersion}\"]"));
        if (!string.IsNullOrEmpty(loader))
            q.Add("loaders=" + Encode("[" + string.Join(",", CompatibleLoaders(loader).Select(l => $"\"{l}\"")) + "]"));
        if (q.Count > 0) url += "?" + string.Join("&", q);
        var j = await Net.json(new[] { url });
        return j.Array.Select(ParseVersion).Where(v => v != null).Select(v => v!).ToList();
    }

    /// 单个版本
    public static async Task<Version?> VersionById(string id) =>
        ParseVersion(await Net.json(new[] { $"{Api}/version/{id}" }));

    /// 解析 Modrinth 的版本对象
    public static Version? ParseVersion(Json v)
    {
        var files = v["files"].Array;
        var id = v["id"].String;
        if (id == null) return null;
        var file = files.FirstOrDefault(f => f["primary"].Bool == true);
        if (!file.Exists) file = files.FirstOrDefault();
        var fileUrl = file["url"].String;
        if (fileUrl == null) return null;
        var r = new Version
        {
            Id = id,
            Name = v["name"].String ?? "",
            Number = v["version_number"].String ?? "",
            GameVersions = v["game_versions"].Array.Select(g => g.String ?? "").Where(s => s.Length > 0).ToList(),
            Loaders = v["loaders"].Array.Select(l => l.String ?? "").Where(s => s.Length > 0).ToList(),
            FileUrl = fileUrl,
            FileName = file["filename"].String
                ?? (Uri.TryCreate(fileUrl, UriKind.Absolute, out var u) ? u.Segments[^1] : id),
            Sha1 = file["hashes"]["sha1"].String,
            Size = file["size"].Double is { } sz ? (long)sz : null,
            Published = ParseDate(v["date_published"].String) ?? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ProjectId = v["project_id"].String ?? "",
        };
        foreach (var d in v["dependencies"].Array)
        {
            var type = d["dependency_type"].String;
            if (type is not ("required" or "optional")) continue;
            if (d["project_id"].String is not { } pid) continue;
            r.Dependencies.Add(new Dependency(pid, d["version_id"].String, type == "required"));
        }
        return r;
    }

    private static DateTime? ParseDate(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.UtcDateTime;
        return null;
    }

    /// 按 SHA-1 查找文件对应的版本（一次请求查询多个文件）
    public static async Task<Dictionary<string, Json>> VersionsByHash(IReadOnlyList<string> sha1s)
    {
        if (sha1s.Count == 0) return new();
        var body = Json.From(new Dictionary<string, Json>
        {
            ["hashes"] = Json.From(sha1s.Select(Json.Of).ToList()),
            ["algorithm"] = Json.Of("sha1"),
        }).Data(false);
        var (code, j) = await Net.request($"{Api}/version_files", body: body, contentType: "application/json");
        if (code != 200) throw new LauncherException($"Modrinth HTTP {code}");
        return j.Object.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// 按 SHA-1 查询每个文件在指定游戏版本与加载器下的最新版本
    public static async Task<Dictionary<string, Version>> LatestByHash(IReadOnlyList<string> sha1s, string gameVersion, string? loader)
    {
        if (sha1s.Count == 0) return new();
        var body = new Dictionary<string, Json>
        {
            ["hashes"] = Json.From(sha1s.Select(Json.Of).ToList()),
            ["algorithm"] = Json.Of("sha1"),
            ["game_versions"] = Json.From(new[] { Json.Of(gameVersion) }),
        };
        if (loader != null) body["loaders"] = Json.From(CompatibleLoaders(loader).Select(Json.Of).ToList());
        var (code, j) = await Net.request($"{Api}/version_files/update", body: Json.From(body).Data(false),
            contentType: "application/json");
        if (code != 200) throw new LauncherException($"Modrinth HTTP {code}");
        var result = new Dictionary<string, Version>();
        foreach (var (hash, vj) in j.Object)
            if (ParseVersion(vj) is { } pv) result[hash] = pv;
        return result;
    }

    /// 按 ID 批量获取项目信息
    public static async Task<IReadOnlyList<Project>> ProjectsByIds(IReadOnlyList<string> ids)
    {
        var result = new List<Project>();
        if (ids.Count == 0) return result;
        // URL 长度有限，分批查询
        for (var start = 0; start < ids.Count; start += 80)
        {
            var chunk = ids.Skip(start).Take(80).ToList();
            var list = "[" + string.Join(",", chunk.Select(i => $"\"{i}\"")) + "]";
            var j = await Net.json(new[] { $"{Api}/projects?ids={Encode(list)}" });
            foreach (var p in j.Array)
            {
                if (p["id"].String is not { } id || p["slug"].String is not { } slug) continue;
                result.Add(new Project
                {
                    Id = id, Slug = slug, TitleText = p["title"].String ?? slug, Description = p["description"].String ?? "",
                    IconUrl = p["icon_url"].String, Downloads = p["downloads"].Int ?? 0,
                    Categories = p["categories"].Array.Select(c => c.String ?? "").Where(s => s.Length > 0).ToList(),
                });
            }
        }
        return result;
    }
}
