using System.Collections.Concurrent;

namespace Kiln.Core;

/// 本地 Mod 的识别、更新与前置解析。
/// 先用 SHA-1 在 Modrinth 上一次性查询所有文件，剩下的再用 CurseForge 指纹一次性查询，不逐个请求。
public static class ModIndex
{
    /// 识别出的本地文件
    public sealed record Entry(string File, string Sha1, Modrinth.Project? Project, Modrinth.Version? Version)
    {
        public Modrinth.Source? SourceKind => Version?.SourceKind;
    }

    /// 一个可用的更新
    public sealed record Update(string File, Modrinth.Project? Project, Modrinth.Version? Current, Modrinth.Version Latest)
    {
        public string Id => File;
    }

    /// 识别文件（.jar 与 .jar.disabled 均可），返回 文件 → 识别结果
    public static async Task<Dictionary<string, Entry>> Identify(IReadOnlyList<string> files)
    {
        // 计算哈希（并行）
        var hashed = new ConcurrentBag<(string File, string Sha1)>();
        await Task.WhenAll(files.Select(f => Task.Run(() =>
        {
            if (Hash.Sha1File(f) is { } sha1) hashed.Add((f, sha1));
        })));
        var hashList = hashed.ToList();
        var result = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        var modrinthVersions = new Dictionary<string, Modrinth.Version>(StringComparer.OrdinalIgnoreCase);

        // Modrinth：一次查询全部
        try
        {
            var map = await Modrinth.VersionsByHash(hashList.Select(h => h.Sha1).ToList());
            foreach (var (file, sha1) in hashList)
            {
                if (map.TryGetValue(sha1, out var j) && Modrinth.ParseVersion(j) is { } v)
                {
                    v.SourceKind = Modrinth.Source.Modrinth;
                    modrinthVersions[file] = v;
                }
            }
        }
        catch { }
        var mrProjects = await TryAsync(() => Modrinth.ProjectsByIds(modrinthVersions.Values.Select(v => v.ProjectId).Distinct().ToList()),
            Array.Empty<Modrinth.Project>());
        var mrById = mrProjects.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var (file, sha1) in hashList)
        {
            if (modrinthVersions.TryGetValue(file, out var v))
                result[file] = new Entry(file, sha1, mrById.GetValueOrDefault(v.ProjectId), v);
        }

        // 其余文件：CurseForge 指纹
        var rest = hashList.Where(h => !result.ContainsKey(h.File)).ToList();
        if (rest.Count > 0)
        {
            var prints = new Dictionary<string, uint>();
            foreach (var (file, _) in rest)
            {
                try { prints[file] = CurseForge.Fingerprint(File.ReadAllBytes(file)); }
                catch { }
            }
            try
            {
                var matched = await CurseForge.Match(prints.Values.Distinct().ToList());
                var modIds = matched.Values.Select(v => int.TryParse(v.ProjectId.AsSpan(3), out var id) ? id : 0)
                    .Where(id => id > 0).ToHashSet();
                var projects = await CurseForge.ProjectsByIds(modIds.ToList());
                foreach (var (file, sha1) in rest)
                {
                    if (!prints.TryGetValue(file, out var fp) || !matched.TryGetValue(fp, out var v)) continue;
                    var p = int.TryParse(v.ProjectId.AsSpan(3), out var id)
                        ? projects.TryGetValue(id, out var tuple) ? tuple.Project : null
                        : null;
                    result[file] = new Entry(file, sha1, p, v);
                }
            }
            catch { }
        }
        // 都没找到的：仍然记录哈希
        foreach (var (file, sha1) in hashList.Where(h => !result.ContainsKey(h.File)))
            result[file] = new Entry(file, sha1, null, null);
        return result;
    }

    private static async Task<T> TryAsync<T>(Func<Task<T>> action, T fallback)
    {
        try { return await action(); }
        catch { return fallback; }
    }

    /// 检查更新：Modrinth 一次请求；CurseForge 按项目并发查询
    public static async Task<IReadOnlyList<Update>> Updates(IReadOnlyList<Entry> entries, string gameVersion, string? loader)
    {
        var updates = new List<Update>();
        var mr = entries.Where(e => e.SourceKind == Modrinth.Source.Modrinth).ToList();
        if (mr.Count > 0)
        {
            try
            {
                var latest = await Modrinth.LatestByHash(mr.Select(e => e.Sha1).ToList(), gameVersion, loader);
                foreach (var e in mr)
                {
                    if (!latest.TryGetValue(e.Sha1, out var v) || v.Id == e.Version?.Id) continue;
                    // 只升级到更新的版本
                    if (e.Version is { } cur && v.Published <= cur.Published) continue;
                    v.SourceKind = Modrinth.Source.Modrinth;
                    updates.Add(new Update(e.File, e.Project, e.Version, v));
                }
            }
            catch { }
        }
        var cf = entries.Where(e => e.SourceKind == Modrinth.Source.CurseForge).ToList();
        var cfUpdates = await Task.WhenAll(cf.Where(e => e.Project != null).Select(async e =>
        {
            try
            {
                var files = await CurseForge.Files(e.Project!, gameVersion, loader);
                var newest = files.FirstOrDefault(f => !f.Blocked);
                if (newest == null || newest.Id == e.Version?.Id
                    || newest.Published <= (e.Version?.Published ?? DateTime.MinValue)) return null;
                return new Update(e.File, e.Project, e.Version, newest);
            }
            catch { return null; }
        }));
        updates.AddRange(cfUpdates.Where(u => u != null).Select(u => u!));
        return updates
            .OrderBy(u => u.Project?.TitleText ?? "", StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// 应用一个更新：下载新文件，旧文件移到回收站；已禁用的 Mod 更新后仍保持禁用
    public static async Task Apply(Update u, Job? job = null)
    {
        var disabled = Path.GetFileName(u.File).EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
        var dir = Path.GetDirectoryName(u.File)!;
        var dest = Path.Combine(dir, u.Latest.FileName + (disabled ? ".disabled" : ""));
        var item = new DownloadItem(u.Latest.FileUrl, dest, u.Latest.Sha1, u.Latest.Size);
        if (u.Latest.SourceKind == Modrinth.Source.CurseForge) item.Urls = CurseForge.DownloadUrls(u.Latest);
        await Downloader.Run(new[] { item }, job);
        if (!string.Equals(Path.GetFullPath(dest), Path.GetFullPath(u.File), StringComparison.OrdinalIgnoreCase))
        {
            try { Recycle.Delete(u.File); } catch { }
        }
    }

    // MARK: 前置

    /// 解析某个版本的必需前置（递归），跳过已安装的项目。返回需要额外安装的版本
    public static async Task<IReadOnlyList<(Modrinth.Project? Project, Modrinth.Version Version)>> RequiredDependencies(
        Modrinth.Version version, string? gameVersion, string? loader, ISet<string> installedProjects)
    {
        var seen = new HashSet<string>(installedProjects) { version.ProjectId };
        var queue = new List<Modrinth.Dependency>(version.Dependencies.Where(d => d.Required));
        var result = new List<(Modrinth.Project?, Modrinth.Version)>();
        var depth = 0;
        while (queue.Count > 0 && depth < 6)
        {
            depth++;
            var current = queue;
            queue = new List<Modrinth.Dependency>();
            foreach (var dep in current)
            {
                if (seen.Contains(dep.ProjectId)) continue;
                seen.Add(dep.ProjectId);
                if (await TryAsync(() => Resolve(dep, gameVersion, loader), null) is not { } v) continue;
                result.Add((null, v));
                queue.AddRange(v.Dependencies.Where(d => d.Required));
            }
        }
        // 补充项目名称
        var mrIds = result.Select(r => r.Item2.ProjectId).Where(id => !id.StartsWith("cf-")).ToList();
        var cfIds = result.Select(r => r.Item2.ProjectId).Where(id => id.StartsWith("cf-"))
            .Select(id => int.TryParse(id.AsSpan(3), out var n) ? n : 0).Where(n => n > 0).ToList();
        var mr = (await TryAsync(() => Modrinth.ProjectsByIds(mrIds), Array.Empty<Modrinth.Project>()))
            .GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        var cf = await TryAsync(() => CurseForge.ProjectsByIds(cfIds),
            new Dictionary<int, (Modrinth.Project, int)>());
        return result.Select(r =>
        {
            Modrinth.Project? p = r.Item2.ProjectId.StartsWith("cf-")
                && int.TryParse(r.Item2.ProjectId.AsSpan(3), out var id)
                && cf.TryGetValue(id, out var tuple) ? tuple.Item1
                : mr.GetValueOrDefault(r.Item2.ProjectId);
            return (p, r.Item2);
        }).ToList();
    }

    private static async Task<Modrinth.Version?> Resolve(Modrinth.Dependency dep, string? gameVersion, string? loader)
    {
        if (dep.ProjectId.StartsWith("cf-"))
        {
            var stub = new Modrinth.Project { Id = dep.ProjectId };
            return (await CurseForge.Files(stub, gameVersion, loader)).FirstOrDefault(v => !v.Blocked);
        }
        // 指定了版本时，只有在它适用于当前游戏版本时才使用
        if (dep.VersionId is { } id && await Modrinth.VersionById(id) is { } specified
            && (gameVersion == null || specified.GameVersions.Contains(gameVersion)))
        {
            specified.SourceKind = Modrinth.Source.Modrinth;
            return specified;
        }
        var v = (await Modrinth.Versions(dep.ProjectId, gameVersion, loader)).FirstOrDefault();
        if (v != null) v.SourceKind = Modrinth.Source.Modrinth;
        return v;
    }

    /// 已安装 Mod 对应的项目 ID（用于跳过已安装的前置）
    public static HashSet<string> InstalledProjects(IReadOnlyDictionary<string, Entry> entries) =>
        entries.Values.Where(e => e.Version != null).Select(e => e.Version!.ProjectId).ToHashSet();
}
