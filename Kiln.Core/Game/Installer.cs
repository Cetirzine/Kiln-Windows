namespace Kiln.Core;

public static class Installer
{
    // MARK: 安装新版本

    /// 安装原版，可选附带 Fabric / Quilt
    public static async Task Install(RemoteVersion version, string name, (ModLoaderKind Kind, string LoaderVersion)? loader,
        string folder, Job job)
    {
        var total = loader == null ? 4 : 5;
        job.Enter(T.Invoke("下载版本 JSON", "Download version JSON"), 0, total);

        // 原版 JSON
        var vanillaDir = Path.Combine(folder, "versions", version.Id);
        var vanillaJsonUrl = Path.Combine(vanillaDir, $"{version.Id}.json");
        if (loader != null || name != version.Id)
        {
            // 安装加载器或自定义名称时，原版 JSON 依然保存在原版名称下，供 inheritsFrom 使用
            if (!File.Exists(vanillaJsonUrl))
            {
                await DownloadVanillaJson(version, vanillaJsonUrl);
            }
        }

        var targetDir = Path.Combine(folder, "versions", name);
        if (File.Exists(Path.Combine(targetDir, $"{name}.json")))
            throw new LauncherException(T.Invoke($"已存在名为 {name} 的版本", $"An instance named {name} already exists"));
        Directory.CreateDirectory(targetDir);
        var targetJson = Path.Combine(targetDir, $"{name}.json");

        if (loader is { } l)
        {
            job.Enter(T.Invoke($"获取 {l.Kind.Id()}", $"Fetch {l.Kind.Id()}"), 1, total);
            var data = await Net.data(Net.Candidates(l.Kind.ProfileUrl(version.Id, l.LoaderVersion), Net.Kind.VersionList));
            var profile = new Dictionary<string, Json>(Json.Parse(data).Object)
            {
                ["id"] = Json.Of(name),
                ["inheritsFrom"] = Json.Of(version.Id),
            };
            File.WriteAllBytes(targetJson, Json.From(profile).Data());
        }
        else if (name == version.Id)
        {
            await DownloadVanillaJson(version, targetJson);
        }
        else
        {
            var j = new Dictionary<string, Json>(Json.Parse(File.ReadAllBytes(vanillaJsonUrl)).Object)
            {
                ["id"] = Json.Of(name),
                ["clientVersion"] = Json.Of(version.Id),
            };
            File.WriteAllBytes(targetJson, Json.From(j).Data());
        }

        // 新版本的隔离设置
        var instance = new McInstance(folder, name);
        var setup = instance.Setup;
        setup.Isolation = instance.Isolated;
        LauncherSettings.Shared.SaveInstanceSetup(setup, targetDir);

        await CompleteFiles(instance, job, stageOffset: total - 3, stageCount: total);

        // 自动调整游戏语言
        if (LauncherSettings.Shared.GameLanguageAuto)
        {
            WriteGameLanguage(instance);
        }
    }

    private static async Task DownloadVanillaJson(RemoteVersion version, string url)
    {
        var item = new DownloadItem(version.Url, url, version.Sha1, kind: Net.Kind.VersionList);
        await Downloader.Fetch(item);
    }

    /// 将游戏语言设置为启动器语言
    public static void WriteGameLanguage(McInstance instance)
    {
        var options = Path.Combine(instance.GameDir, "options.txt");
        if (File.Exists(options)) return;
        var code = LauncherSettings.Shared.Language.GameLanguage();
        // 1.10 及以前使用 xx_XX 格式
        var old = instance.ReleaseTime < new DateTime(2016, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var value = old ? code[..3] + code[^2..].ToUpperInvariant() : code;
        try
        {
            Directory.CreateDirectory(instance.GameDir);
            File.WriteAllText(options, $"lang:{value}\n");
        }
        catch { }
    }

    // MARK: 补全文件

    /// 补全某个版本运行所需的所有文件（原版 JSON、客户端、支持库、资源文件）
    public static async Task CompleteFiles(McInstance instance, Job? job, int stageOffset = 0, int stageCount = 3,
        string? arch = null, bool quick = false)
    {
        // 缺失的前置版本
        if (instance.MissingParent is { } parent)
        {
            var remote = await VersionManifest.Find(parent);
            var url = Path.Combine(instance.Folder, "versions", parent, $"{parent}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(url)!);
            await DownloadVanillaJson(remote, url);
            instance.Reload();
        }
        var json = instance.Json;
        var ctx = new RuleContext(arch ?? instance.PreferredArch);

        // 客户端与支持库
        job?.Enter(T.Invoke("下载游戏文件", "Downloading game files"), stageOffset, stageCount);
        var items = new List<DownloadItem>();
        var client = json["downloads"]["client"];
        if (client["url"].String is { } clientUrl)
        {
            items.Add(new DownloadItem(clientUrl, instance.ClientJar, client["sha1"].String,
                client["size"].Double is { } sz ? (long)sz : null));
        }
        foreach (var lib in instance.Libraries(ctx))
        {
            if (lib.Download is { } d) items.Add(d);
            else if (!File.Exists(lib.Path))
            {
                Log.log($"[Install] 支持库 {lib.Name} 缺失，且没有下载地址");
            }
        }
        if (json["logging"]["client"]["file"]["url"].String is { } logging
            && json["logging"]["client"]["file"]["id"].String is { } logId)
        {
            items.Add(new DownloadItem(logging,
                Path.Combine(instance.Folder, "assets", "log_configs", logId),
                json["logging"]["client"]["file"]["sha1"].String));
        }
        await Downloader.Run(items, job, checkHash: !quick);

        // 与官方启动器一致：继承版本使用自己目录下的 <名称>.jar（Forge 1.17+ 的 ignoreList 依赖这个文件名）。
        var ownJar = Path.Combine(instance.Dir, $"{instance.Name}.jar");
        if (instance.Parents.Count > 0 && !File.Exists(ownJar) && File.Exists(instance.ClientJar))
        {
            try { File.Copy(instance.ClientJar, ownJar); } catch { }
        }

        // 资源文件
        job?.Enter(T.Invoke("下载资源文件", "Download assets"), stageOffset + 1, stageCount);
        if (!(quick && LauncherSettings.Shared.SkipAssetCheck))
        {
            await CompleteAssets(instance, job, checkHash: false);
        }
        job?.Enter(T.Invoke("完成", "Done"), stageOffset + 2, stageCount);
    }

    public static async Task CompleteAssets(McInstance instance, Job? job, bool checkHash)
    {
        var json = instance.Json;
        var assets = Path.Combine(instance.Folder, "assets");
        var index = json["assetIndex"];
        var indexUrl = index["url"].String;
        var id = index["id"].String;
        if (indexUrl == null || id == null) return;
        var indexFile = Path.Combine(assets, "indexes", $"{id}.json");
        var indexItem = new DownloadItem(indexUrl, indexFile, index["sha1"].String);
        if (!indexItem.IsValid()) await Downloader.Fetch(indexItem);
        var indexJson = Json.Parse(File.ReadAllBytes(indexFile));
        var items = new List<DownloadItem>();
        var copies = new List<(string Src, string Dst)>();
        var isVirtual = indexJson["virtual"].Bool == true;
        var mapToResources = indexJson["map_to_resources"].Bool == true;
        foreach (var (path, obj) in indexJson["objects"].Object)
        {
            var hash = obj["hash"].String;
            if (hash == null || hash.Length <= 2) continue;
            var prefix = hash[..2];
            var dest = Path.Combine(assets, "objects", prefix, hash);
            items.Add(new DownloadItem($"https://resources.download.minecraft.net/{prefix}/{hash}", dest,
                hash, obj["size"].Double is { } sz ? (long)sz : null));
            if (isVirtual) copies.Add((dest, Path.Combine(assets, "virtual", id, path)));
            if (mapToResources) copies.Add((dest, Path.Combine(instance.GameDir, "resources", path)));
        }
        await Downloader.Run(items, job, checkHash: checkHash);
        foreach (var (src, dst) in copies)
        {
            if (File.Exists(dst)) continue;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst);
            }
            catch { }
        }
    }
}
