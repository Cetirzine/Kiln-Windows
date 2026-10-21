using System.IO.Compression;

namespace Kiln.Core;

/// 整合包：安装 Modrinth（.mrpack）与 CurseForge（manifest.json）格式，导出为 .mrpack。
/// CurseForge 的文件信息一次批量查询；作者禁止第三方下载的文件会列出来，由用户手动下载后自动放入。
public static class Modpack
{
    public enum Format { Modrinth, CurseForge }

    public sealed record Info(Format FormatKind, string Name, string Version, string Summary, string GameVersion,
        (string Kind, string Version)? Loader, int FileCount, Json Manifest);

    /// 需要手动下载的文件（CurseForge 作者禁止第三方下载）
    public sealed record ManualFile(string Title, string FileName, string PageUrl, string Destination, string? Sha1)
    {
        public string Id => Destination;
    }

    // MARK: 读取

    public static Info Inspect(string archive)
    {
        if (Zip.Read(archive, "modrinth.index.json") is { } mrData)
        {
            Json j;
            try { j = Json.Parse(mrData); }
            catch { j = Json.Null; }
            if (j.Exists)
            {
                var deps = j["dependencies"];
                var game = deps["minecraft"].String
                    ?? throw new LauncherException(T.Invoke("整合包缺少 Minecraft 版本", "The modpack doesn't specify a Minecraft version"));
                (string, string)? loader = null;
                foreach (var (key, kind) in new[] { ("fabric-loader", "Fabric"), ("quilt-loader", "Quilt"), ("neoforge", "NeoForge"), ("forge", "Forge") })
                    if (deps[key].String is { } v) { loader = (kind, v); break; }
                var files = j["files"].Array.Where(f => f["env"]["client"].String != "unsupported").ToList();
                return new Info(Format.Modrinth,
                    j["name"].String ?? Path.GetFileNameWithoutExtension(archive),
                    j["versionId"].String ?? "", j["summary"].String ?? "",
                    game, loader, files.Count, j);
            }
        }
        if (Zip.Read(archive, "manifest.json") is { } cfData)
        {
            Json j;
            try { j = Json.Parse(cfData); }
            catch { j = Json.Null; }
            if (j.Exists && j["manifestType"].String == "minecraftModpack")
            {
                var mc = j["minecraft"];
                var game = mc["version"].String
                    ?? throw new LauncherException(T.Invoke("整合包缺少 Minecraft 版本", "The modpack doesn't specify a Minecraft version"));
                var loaders = mc["modLoaders"].Array;
                var primary = loaders.FirstOrDefault(l => l["primary"].Bool == true);
                if (!primary.Exists) primary = loaders.FirstOrDefault();
                (string, string)? loader = null;
                if (primary["id"].String is { } id)
                {
                    var dash = id.IndexOf('-');
                    if (dash > 0)
                    {
                        var kind = id[..dash].ToLowerInvariant();
                        var ver = id[(dash + 1)..];
                        loader = kind switch
                        {
                            "forge" => ("Forge", ver),
                            "neoforge" => ("NeoForge", ver),
                            "fabric" => ("Fabric", ver),
                            "quilt" => ("Quilt", ver),
                            _ => null,
                        };
                    }
                }
                return new Info(Format.CurseForge,
                    j["name"].String ?? Path.GetFileNameWithoutExtension(archive),
                    j["version"].String ?? "",
                    j["author"].String is { } author ? T.Invoke($"作者：{author}", $"By {author}") : "",
                    game, loader, j["files"].Array.Count, j);
            }
        }
        throw new LauncherException(T.Invoke("不是支持的整合包（需要 Modrinth 的 .mrpack 或 CurseForge 整合包）",
            "Not a supported modpack (expected a Modrinth .mrpack or a CurseForge modpack)"));
    }

    // MARK: 安装

    /// 安装整合包为新版本；返回需要手动下载的文件
    public static async Task<IReadOnlyList<ManualFile>> Install(string archive, string name, string folder, Job job)
    {
        var info = Inspect(archive);
        var dir = Path.Combine(folder, "versions", name);
        if (Directory.Exists(dir))
            throw new LauncherException(T.Invoke($"已存在名为 {name} 的版本", $"An instance named {name} already exists"));
        // 安装失败时删除未完成的版本，避免留下无法启动的版本
        var finished = false;
        try
        {
            // 1. 游戏与加载器
            await InstallGame(info.GameVersion, info.Loader, name, folder, job);
            var instance = new McInstance(folder, name);
            // 整合包总是使用独立的游戏目录
            var setup = instance.Setup;
            setup.Isolation = true;
            LauncherSettings.Shared.SaveInstanceSetup(setup, instance.Dir);
            var gameDir = instance.Dir;

            // 2. 解压覆盖文件
            job.Enter(T.Invoke("解压整合包", "Extracting the modpack"), 0, 2);
            var tmp = Path.Combine(Paths.Cache, "modpack-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                Zip.ExtractAll(archive, tmp);
                var overrideDirs = info.FormatKind == Format.Modrinth
                    ? new[] { "overrides", "client-overrides" }
                    : new[] { info.Manifest["overrides"].String ?? "overrides" };
                foreach (var sub in overrideDirs) Merge(Path.Combine(tmp, sub), gameDir);

                // 3. 下载文件
                job.Enter(T.Invoke("下载整合包文件", "Downloading modpack files"), 1, 2);
                var manual = new List<ManualFile>();
                if (info.FormatKind == Format.Modrinth)
                {
                    var items = new List<DownloadItem>();
                    foreach (var f in info.Manifest["files"].Array)
                    {
                        if (f["env"]["client"].String == "unsupported") continue;
                        if (f["path"].String is not { } path || SafePath(path, gameDir) is not { } dest) continue;
                        var urls = f["downloads"].Array.Select(u => u.String).Where(u => u != null).Select(u => u!).ToList();
                        if (urls.Count == 0) continue;
                        var item = new DownloadItem(urls[0], dest, f["hashes"]["sha1"].String,
                            f["fileSize"].Double is { } sz ? (long)sz : null) { Urls = urls };
                        items.Add(item);
                    }
                    await Downloader.Run(items, job);
                }
                else
                {
                    var entries = info.Manifest["files"].Array.Where(e => e["required"].Bool != false).ToList();
                    var fileIds = entries.Select(e => e["fileID"].Int).Where(i => i != null).Select(i => (int)i!).ToList();
                    var projectIds = entries.Select(e => e["projectID"].Int).Where(i => i != null).Select(i => (int)i!).Distinct().ToList();
                    var filesTask = CurseForge.FilesByIds(fileIds);
                    var projectsTask = CurseForge.ProjectsByIds(projectIds);
                    var files = await filesTask;
                    var projects = await Task.Run(async () =>
                    {
                        try { return await projectsTask; }
                        catch { return new Dictionary<int, (Modrinth.Project, int)>(); }
                    });
                    var items = new List<DownloadItem>();
                    var missing = new List<string>();
                    foreach (var e in entries)
                    {
                        if (e["fileID"].Int is not { } fidL || e["projectID"].Int is not { } pidL) continue;
                        var fid = (int)fidL;
                        var pid = (int)pidL;
                        if (!files.TryGetValue(fid, out var v)) { missing.Add($"{pid}/{fid}"); continue; }
                        var classId = projects.TryGetValue(pid, out var tuple) ? tuple.Item2 : 6;
                        var sub = classId switch
                        {
                            12 => "resourcepacks",
                            6552 => "shaderpacks",
                            _ => "mods",
                        };
                        if (SafePath($"{sub}/{v.FileName}", gameDir) is not { } dest) continue;
                        if (v.Blocked)
                        {
                            var page = projects.TryGetValue(pid, out var t)
                                ? CurseForge.PageUrl(classId, t.Item1.Slug) + $"/files/{fid}"
                                : $"https://www.curseforge.com/projects/{pid}";
                            manual.Add(new ManualFile(projects.TryGetValue(pid, out var t2) ? t2.Item1.TitleText : v.FileName,
                                v.FileName, page, dest, v.Sha1));
                            continue;
                        }
                        var item = new DownloadItem(v.FileUrl, dest, v.Sha1, v.Size) { Urls = CurseForge.DownloadUrls(v) };
                        items.Add(item);
                    }
                    if (missing.Count > 0)
                        Log.log($"[Modpack] CurseForge 上找不到 {missing.Count} 个文件：{string.Join(", ", missing)}");
                    // 作者禁止第三方下载的文件：如果作者也在 Modrinth 上发布了同一个文件（SHA-1 相同），从 Modrinth 下载
                    var hashes = manual.Select(m => m.Sha1).Where(h => h != null).Select(h => h!).ToList();
                    if (hashes.Count > 0)
                    {
                        try
                        {
                            var found = await Modrinth.VersionsByHash(hashes);
                            manual = manual.Where(m =>
                            {
                                if (m.Sha1 is not { } sha1 || !found.TryGetValue(sha1, out var v)) return true;
                                var file = v["files"].Array.FirstOrDefault(f => f["hashes"]["sha1"].String == sha1);
                                var url = file["url"].String;
                                if (url == null) return true;
                                items.Add(new DownloadItem(url, m.Destination, sha1,
                                    file["size"].Double is { } sz ? (long)sz : null));
                                Log.log($"[Modpack] {m.FileName} 改从 Modrinth 下载（SHA-1 一致）");
                                return false;
                            }).ToList();
                        }
                        catch { }
                    }
                    await Downloader.Run(items, job);
                }

                // 记录来源，便于以后识别
                var meta = Json.From(new Dictionary<string, Json>
                {
                    ["format"] = Json.Of(info.FormatKind == Format.Modrinth ? "Modrinth" : "CurseForge"),
                    ["name"] = Json.Of(info.Name),
                    ["version"] = Json.Of(info.Version),
                });
                try { File.WriteAllBytes(Path.Combine(instance.Dir, ".kiln", "modpack.json"), meta.Data()); }
                catch { }
                finished = true;
                return manual;
            }
            finally
            {
                try { Directory.Delete(tmp, recursive: true); } catch { }
            }
        }
        finally
        {
            if (!finished)
                try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// 在“下载”文件夹中查找用户手动下载的文件（校验 SHA-1 后移到目标位置），返回仍未找到的文件
    public static IReadOnlyList<ManualFile> CollectManualDownloads(IReadOnlyList<ManualFile> files, string? downloadsDirectory = null)
    {
        var downloads = downloadsDirectory ?? Paths.Downloads;
        return files.Where(m =>
        {
            bool Valid(string path) => File.Exists(path) && (m.Sha1 == null || string.Equals(Hash.Sha1File(path), m.Sha1, StringComparison.OrdinalIgnoreCase));
            try { if (Valid(m.Destination)) return false; } catch { }
            if (m.FileName.Contains('/') || m.FileName.Contains('\\') || SafePath(m.FileName, downloads) == null) return true;
            // 浏览器可能把空格等字符替换掉，同时比较原名与常见变体
            var candidates = new[] { m.FileName, m.FileName.Replace(" ", "+"), m.FileName.Replace(" ", "_") };
            foreach (var file in candidates)
            {
                var src = Path.Combine(downloads, file);
                try
                {
                    if (!Valid(src)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(m.Destination)!);
                    File.Move(src, m.Destination, overwrite: true);
                    return false;
                }
                catch { }
            }
            return true;
        }).ToList();
    }

    /// 安装游戏与加载器（Fabric / Quilt / Forge / NeoForge）为新版本
    public static async Task InstallGame(string gameVersion, (string Kind, string Version)? loader, string name, string folder, Job job)
    {
        var game = await VersionManifest.Find(gameVersion);
        if (loader is { } l)
        {
            switch (l.Kind)
            {
                case "Fabric" or "Quilt":
                {
                    var kind = l.Kind == "Fabric" ? ModLoaderKind.Fabric : ModLoaderKind.Quilt;
                    await Installer.Install(game, name, (kind, l.Version), folder, job);
                    return;
                }
                case "Forge" or "NeoForge":
                {
                    var kind = l.Kind == "Forge" ? ForgeKind.Forge : ForgeKind.NeoForge;
                    var maven = await MavenVersion(kind, l.Version, gameVersion);
                    await ForgeInstaller.Install(kind, maven, game, name, folder, job);
                    return;
                }
            }
        }
        await Installer.Install(game, name, null, folder, job);
    }

    /// 整合包中的加载器版本 → Forge / NeoForge 的 Maven 版本号
    public static async Task<string> MavenVersion(ForgeKind kind, string v, string game)
    {
        IReadOnlyList<ForgeInstaller.Version> list;
        try { list = await ForgeInstaller.Versions(kind, game); }
        catch { list = Array.Empty<ForgeInstaller.Version>(); }
        // 早期 NeoForge 带 -beta 后缀：21.0.18 → 21.0.18-beta
        var hit = list.FirstOrDefault(x => x.VersionString == v || x.Display == v || x.VersionString == $"{game}-{v}")
                  ?? list.FirstOrDefault(x => x.VersionString.StartsWith(v + "-")
                      || x.VersionString.StartsWith($"{game}-{v}-"));
        if (hit != null) return hit.VersionString;
        if (kind == ForgeKind.Forge && !v.StartsWith(game)) return $"{game}-{v}";
        return v;
    }

    /// 相对路径解析到游戏目录内；拒绝绝对路径与 ..
    public static string? SafePath(string path, string root)
    {
        if (path.StartsWith('/') || path.Contains('\\') || path.Contains(':') || path.Contains('\0')) return null;
        var parts = path.Split('/').Where(p => p.Length > 0).ToList();
        if (parts.Count == 0 || parts.Any(p => p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) return null;
        return parts.Aggregate(root, (acc, p) => Path.Combine(acc, p));
    }

    /// 把目录内容合并复制到目标（覆盖同名文件）
    private static void Merge(string src, string dst)
    {
        try
        {
            if (!Directory.Exists(src)) return;
            foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(src, file);
                if (SafePath(rel.Replace('\\', '/'), dst) is not { } target) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target))
                    try { File.Delete(target); } catch { }
                File.Copy(file, target);
            }
        }
        catch (Exception e)
        {
            Log.log($"[Modpack] 合并覆盖文件失败：{e.Friendly()}");
        }
    }

    // MARK: 导出

    /// 可以选择导出的内容（游戏目录顶层）
    public sealed record ExportItem(string Path, long Size, bool IsDirectory, bool Suggested)
    {
        public string Id => Path;
    }

    /// 不应导出的内容：缓存、日志、账号信息与启动器文件
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "logs", "crash-reports", "versions", "assets", "libraries", "natives", ".kiln", ".fabric",
        ".mixin.out", "debug", "downloads", "usercache.json", "usernamecache.json",
        "launcher_profiles.json", "launcher_accounts.json", "launcher_settings.json", "command_history.txt", ".DS_Store", "backups", ".cache", "local",
    };

    /// 默认不勾选：个人数据
    private static readonly HashSet<string> Personal = new(StringComparer.OrdinalIgnoreCase)
    {
        "saves", "screenshots", "servers.dat", "servers.dat_old", "options.txt", "optionsof.txt",
        "optionsshaders.txt", "schematics", "replay_recordings", "journeymap", "xaero", "XaeroWorldMap", "XaeroWaypoints",
    };

    public static IReadOnlyList<ExportItem> ExportItems(McInstance instance)
    {
        var dir = instance.GameDir;
        string[] names;
        try { names = Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName).Where(n => n != null).Select(n => n!).ToArray(); }
        catch { return Array.Empty<ExportItem>(); }
        return names.SelectMany(n =>
        {
            if (Excluded.Contains(n) || n.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || n.StartsWith('.')) return Array.Empty<ExportItem>();
            // 独立游戏目录中的版本文件
            if (instance.Isolated && (n == $"{instance.Name}.json" || n == $"{instance.Name}.jar" || n == $"{instance.Name}-natives"))
                return Array.Empty<ExportItem>();
            var url = Path.Combine(dir, n);
            var isDir = Directory.Exists(url) && !File.Exists(url);
            return new[] { new ExportItem(n, SizeOf(url), isDir, !Personal.Contains(n)) };
        })
        .OrderBy(i => i.IsDirectory ? 0 : 1).ThenBy(i => i.Path.ToLowerInvariant())
        .ToList();
    }

    private static long SizeOf(string url)
    {
        try
        {
            if (File.Exists(url)) return new FileInfo(url).Length;
            return Directory.EnumerateFiles(url, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true }).Sum(f => new FileInfo(f).Length);
        }
        catch { return 0; }
    }

    public sealed record ExportResult(int Linked, int Embedded);

    /// 导出为 .mrpack：能在 Modrinth 上找到的 Mod / 资源包 / 光影以下载地址引用，其余文件放入 overrides
    public static async Task<ExportResult> Export(McInstance instance, string name, string version, string summary,
        ISet<string> include, string output, Job job)
    {
        var gameDir = instance.GameDir;
        job.Token.ThrowIfCancellationRequested();
        job.Enter(T.Invoke("整理文件", "Collecting files"), 0, 3);
        // 收集文件
        var files = new List<(string Rel, string Url)>();
        foreach (var top in include.OrderBy(s => s, StringComparer.Ordinal))
        {
            job.Token.ThrowIfCancellationRequested();
            if (top.Contains('/') || SafePath(top, gameDir) is not { } url || top.StartsWith('.') || Excluded.Contains(top) || top.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                throw new LauncherException(T.Invoke($"无法导出此路径：{top}", $"This path cannot be exported: {top}"));
            if (File.Exists(url))
            {
                files.Add((top, url));
                continue;
            }
            if (!Directory.Exists(url)) continue;
            var baseFull = Path.GetFullPath(url);
            foreach (var f in Directory.EnumerateFiles(url, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                job.Token.ThrowIfCancellationRequested();
                if (Path.GetFileName(f) == ".DS_Store") continue;
                var full = Path.GetFullPath(f);
                if (!full.StartsWith(baseFull + Path.DirectorySeparatorChar)) continue;
                files.Add((top + "/" + Path.GetRelativePath(baseFull, full).Replace('\\', '/'), f));
            }
        }

        // 在 Modrinth 上查找：mods / resourcepacks / shaderpacks 中的文件
        job.Enter(T.Invoke("在 Modrinth 上查找文件", "Looking up files on Modrinth"), 1, 3);
        var linkable = files.Where(f =>
        {
            var parts = f.Rel.Split('/');
            return parts.Length == 2 && new[] { "mods", "resourcepacks", "shaderpacks" }.Contains(parts[0])
                && !f.Rel.EndsWith(".disabled");
        }).ToList();
        var hashes = new Dictionary<string, string>();
        foreach (var f in linkable)
        {
            job.Token.ThrowIfCancellationRequested();
            if (Hash.Sha1File(f.Url) is { } h) hashes[f.Rel] = h;
        }
        var found = new Dictionary<string, Json>();
        try { found = await Modrinth.VersionsByHash(hashes.Values.ToList(), job.Token); }
        catch (OperationCanceledException) when (job.Token.IsCancellationRequested) { throw; }
        catch { }
        var indexFiles = new List<Json>();
        var linkedPaths = new HashSet<string>();
        foreach (var f in linkable)
        {
            if (!hashes.TryGetValue(f.Rel, out var sha1) || !found.TryGetValue(sha1, out var v)) continue;
            var file = v["files"].Array.FirstOrDefault(x => x["hashes"]["sha1"].String == sha1);
            var url = file["url"].String;
            var sha512 = file["hashes"]["sha512"].String;
            if (url == null || sha512 == null) continue;
            indexFiles.Add(Json.From(new Dictionary<string, Json>
            {
                ["path"] = Json.Of(f.Rel),
                ["hashes"] = Json.From(new Dictionary<string, Json> { ["sha1"] = Json.Of(sha1), ["sha512"] = Json.Of(sha512) }),
                ["env"] = Json.From(new Dictionary<string, Json>
                {
                    ["client"] = Json.Of("required"),
                    ["server"] = Json.Of(f.Rel.StartsWith("mods/") ? "required" : "unsupported"),
                }),
                ["downloads"] = Json.From(new[] { Json.Of(url) }),
                ["fileSize"] = Json.Of(file["size"].Double ?? 0),
            }));
            linkedPaths.Add(f.Rel);
        }

        // 打包
        job.Enter(T.Invoke("打包", "Packing"), 2, 3);
        var staging = Path.Combine(Paths.Cache, "export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var embedded = 0;
            foreach (var f in files.Where(f => !linkedPaths.Contains(f.Rel)))
            {
                job.Token.ThrowIfCancellationRequested();
                if (SafePath("overrides/" + f.Rel, staging) is not { } dest) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(f.Url, dest);
                embedded++;
            }
            var deps = new Dictionary<string, Json> { ["minecraft"] = Json.Of(instance.VanillaVersion) };
            foreach (var l in instance.Loaders)
            {
                if (instance.LoaderVersion(l) is not { } v) continue;
                switch (l)
                {
                    case "Fabric": deps["fabric-loader"] = Json.Of(v); break;
                    case "Quilt": deps["quilt-loader"] = Json.Of(v); break;
                    case "Forge": deps["forge"] = Json.Of(v); break;
                    case "NeoForge": deps["neoforge"] = Json.Of(v); break;
                }
            }
            var index = new Dictionary<string, Json>
            {
                ["formatVersion"] = Json.Of(1),
                ["game"] = Json.Of("minecraft"),
                ["versionId"] = Json.Of(version),
                ["name"] = Json.Of(name),
                ["files"] = Json.From(indexFiles),
                ["dependencies"] = Json.From(deps),
            };
            if (summary.Length > 0) index["summary"] = Json.Of(summary);
            File.WriteAllBytes(Path.Combine(staging, "modrinth.index.json"), Json.From(index).Data());
            var packed = Path.Combine(staging, "export.mrpack");
            var payload = Directory.GetFiles(staging, "*", SearchOption.AllDirectories);
            using (var zip = ZipFile.Open(packed, ZipArchiveMode.Create))
                foreach (var f in payload)
                {
                    job.Token.ThrowIfCancellationRequested();
                    zip.CreateEntryFromFile(f, Path.GetRelativePath(staging, f).Replace('\\', '/'));
                }
            job.Token.ThrowIfCancellationRequested();
            File.Move(packed, output, overwrite: true);
            return new ExportResult(linkedPaths.Count, embedded);
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch { }
        }
    }
}
