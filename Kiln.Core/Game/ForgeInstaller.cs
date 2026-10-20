using System.Diagnostics;

namespace Kiln.Core;

/// Forge / NeoForge 安装（实现思路参考 HMCL：解析安装器中的 install_profile.json，下载支持库并运行 processors）
public enum ForgeKind { Forge, NeoForge }

public static class ForgeKindExtensions
{
    public static string Id(this ForgeKind kind) => kind == ForgeKind.Forge ? "Forge" : "NeoForge";

    public static string InstallerUrl(this ForgeKind kind, string version) =>
        kind == ForgeKind.Forge
            ? $"https://maven.minecraftforge.net/net/minecraftforge/forge/{version}/forge-{version}-installer.jar"
            // 1.20.1 的 NeoForge 沿用 Forge 的坐标：net.neoforged:forge:1.20.1-47.1.x
            : ForgeInstaller.IsLegacyNeoForge(version)
                ? $"https://maven.neoforged.net/releases/net/neoforged/forge/{version}/forge-{version}-installer.jar"
                : $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{version}/neoforge-{version}-installer.jar";
}

public static class ForgeInstaller
{
    public sealed record Version(string VersionString)
    {
        public string Id => VersionString;

        /// 显示用的简短版本号
        public string Display
        {
            get
            {
                var i = VersionString.IndexOf('-');
                if (i >= 0 && VersionString.StartsWith("1.")) return VersionString[(i + 1)..];
                return VersionString;
            }
        }

        public bool IsBeta => VersionString.Contains("beta") || VersionString.Contains("alpha");
    }

    // MARK: 版本列表

    public static async Task<IReadOnlyList<Version>> Versions(ForgeKind kind, string game)
    {
        if (kind == ForgeKind.Forge)
        {
            var j = await Net.json(new[]
            {
                "https://files.minecraftforge.net/net/minecraftforge/forge/maven-metadata.json",
                "https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/maven-metadata.json",
            });
            return j[game].Array.Select(v => v.String).Where(s => s != null).Reverse()
                .Select(v => new Version(v!)).ToList();
        }
        if (game == "1.20.1")
        {
            var j = await Net.json(new[] { "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/forge" });
            // 少数早期版本没有 1.20.1- 前缀（如 47.1.82）
            return j["versions"].Array.Select(v => v.String).Where(s => s != null && s.StartsWith("1.20.1-")).Reverse()
                .Select(v => new Version(v!)).ToList();
        }
        var jj = await Net.json(new[]
        {
            "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge",
            "https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/neoforge",
        });
        return jj["versions"].Array.Select(v => v.String).Where(s => s != null && NeoForgeGameVersion(s!) == game).Reverse()
            .Select(v => new Version(v!)).ToList();
    }

    /// 1.20.1 的 NeoForge（47.1.x，Maven 版本号带 1.20.1- 前缀）
    public static bool IsLegacyNeoForge(string version) =>
        version.StartsWith("1.20.1-") || version.StartsWith("47.");

    /// NeoForge 版本号对应的 Minecraft 版本：21.1.77 → 1.21.1，20.4.x → 1.20.4，26.1.0.3 → 26.1
    public static string? NeoForgeGameVersion(string v)
    {
        var comps = v.Split('-')[0].Split('.').Select(p => int.TryParse(p, out var n) ? n : (int?)null)
            .Where(n => n != null).Select(n => n!.Value).ToList();
        if (comps.Count < 2) return null;
        if (comps[0] >= 26)
        {
            // 新的年份版本号：主.次.修订.构建
            var patch = comps.Count >= 4 ? comps[2] : 0;
            return patch == 0 ? $"{comps[0]}.{comps[1]}" : $"{comps[0]}.{comps[1]}.{patch}";
        }
        return comps[1] == 0 ? $"1.{comps[0]}" : $"1.{comps[0]}.{comps[1]}";
    }

    // MARK: 安装

    public static async Task Install(ForgeKind kind, string version, RemoteVersion game, string name, string folder, Job job)
    {
        const int total = 5;
        var libraries = Path.Combine(folder, "libraries");

        // 1. 原版
        job.Enter(T.Invoke($"准备原版 {game.Id}", $"Preparing vanilla {game.Id}"), 0, total);
        var vanillaJson = Path.Combine(folder, "versions", game.Id, $"{game.Id}.json");
        if (!File.Exists(vanillaJson))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(vanillaJson)!);
            await Downloader.Fetch(new DownloadItem(game.Url, vanillaJson, game.Sha1, kind: Net.Kind.VersionList));
        }
        var vanilla = new McInstance(folder, game.Id);
        await Installer.CompleteFiles(vanilla, null);

        // 2. 安装器
        job.Enter(T.Invoke($"下载 {kind.Id()} 安装器", $"Downloading the {kind.Id()} installer"), 1, total);
        var installer = Path.Combine(Paths.Cache, $"{kind.Id().ToLowerInvariant()}-{version}-installer.jar");
        await Downloader.Fetch(new DownloadItem(kind.InstallerUrl(version), installer));
        var profileData = Zip.Read(installer, "install_profile.json")
            ?? throw new LauncherException(T.Invoke("安装器中缺少 install_profile.json", "install_profile.json is missing from the installer"));
        // 旧地址替换为新的 HTTPS 地址
        var profileText = System.Text.Encoding.UTF8.GetString(profileData)
            .Replace("http://files.minecraftforge.net/maven/", "https://maven.minecraftforge.net/");
        var profile = Json.Parse(profileText);

        var targetDir = Path.Combine(folder, "versions", name);
        if (File.Exists(Path.Combine(targetDir, $"{name}.json")))
            throw new LauncherException(T.Invoke($"已存在名为 {name} 的版本", $"An instance named {name} already exists"));
        Directory.CreateDirectory(targetDir);

        if (profile["install"].Exists)
        {
            // 旧格式（1.12.2 及以前）
            await InstallLegacy(profile, installer, game, name, targetDir, libraries, job);
        }
        else
        {
            await InstallModern(profile, installer, vanilla, game, name, targetDir, folder, libraries, job);
        }

        job.Enter(T.Invoke("检查文件", "Verifying files"), 4, total);
        var instance = new McInstance(folder, name);
        var setup = instance.Setup;
        setup.Isolation = instance.Isolated;
        LauncherSettings.Shared.SaveInstanceSetup(setup, targetDir);
        await Installer.CompleteFiles(instance, null);
        if (LauncherSettings.Shared.GameLanguageAuto) Installer.WriteGameLanguage(instance);
    }

    private static void WriteVersionJson(Json json, string name, string game, string dir)
    {
        var j = new Dictionary<string, Json>(json.Object) { ["id"] = Json.Of(name) };
        if (!j.ContainsKey("inheritsFrom")) j["inheritsFrom"] = Json.Of(game);
        j.Remove("jar");
        File.WriteAllBytes(Path.Combine(dir, $"{name}.json"), Json.From(j).Data());
    }

    private static async Task InstallLegacy(Json profile, string installer, RemoteVersion game, string name,
        string targetDir, string libraries, Job job)
    {
        job.Enter(T.Invoke("安装支持库", "Installing libraries"), 2, 5);
        var install = profile["install"];
        if (install["path"].String is { } coords && install["filePath"].String is { } file && Maven.Path(coords) is { } rel)
        {
            Zip.Extract(installer, file, Path.Combine(libraries, rel));
        }
        var info = new Dictionary<string, Json>(profile["versionInfo"].Object);
        // 只保留客户端需要的支持库
        var libs = profile["versionInfo"]["libraries"].Array.Where(l => l["clientreq"].Bool != false).ToList();
        info["libraries"] = Json.From(libs);
        WriteVersionJson(Json.From(info), name, game.Id, targetDir);
        await Task.CompletedTask;
    }

    private static async Task InstallModern(Json profile, string installer, McInstance vanilla, RemoteVersion game,
        string name, string targetDir, string folder, string libraries, Job job)
    {
        // 版本 JSON
        var jsonPath = profile["json"].String ?? "/version.json";
        if (jsonPath.StartsWith('/')) jsonPath = jsonPath[1..];
        var versionData = Zip.Read(installer, jsonPath)
            ?? throw new LauncherException(T.Invoke("安装器中缺少 version.json", "version.json is missing from the installer"));
        var versionJson = Json.Parse(versionData);

        // 支持库：安装器内置的直接解压，其余下载
        job.Enter(T.Invoke("下载支持库", "Downloading libraries"), 2, 5);
        var items = new List<DownloadItem>();
        foreach (var lib in profile["libraries"].Array.Concat(versionJson["libraries"].Array))
        {
            var libName = lib["name"].String;
            if (libName == null) continue;
            var art = lib["downloads"]["artifact"];
            var rel = art["path"].String ?? Maven.Path(libName);
            if (rel == null) continue;
            var dest = Path.Combine(libraries, rel);
            if (art["url"].String is { Length: > 0 } url)
            {
                items.Add(new DownloadItem(url, dest, art["sha1"].String,
                    art["size"].Double is { } sz ? (long)sz : null));
            }
            else if (!File.Exists(dest))
            {
                try { Zip.Extract(installer, $"maven/{rel}", dest); } catch { }
            }
        }
        await Downloader.Run(items, job);

        // data 字段
        var work = Path.Combine(Paths.Cache, "forge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var data = new Dictionary<string, string>
        {
            ["SIDE"] = "client",
            ["MINECRAFT_JAR"] = vanilla.ClientJar,
            ["MINECRAFT_VERSION"] = game.Id,
            ["ROOT"] = folder,
            ["INSTALLER"] = installer,
            ["LIBRARY_DIR"] = libraries,
        };
        foreach (var (key, value) in profile["data"].Object)
        {
            if (value["client"].String is not { } v) continue;
            data[key] = ResolveData(v, installer, libraries, work);
        }
        string Substitute(string s)
        {
            if (s.StartsWith('[') && s.EndsWith(']') && Maven.Path(s[1..^1]) is { } rel)
                return Path.Combine(libraries, rel);
            var r = s;
            foreach (var (k, v) in data) r = r.Replace($"{{{k}}}", v);
            return r;
        }

        // processors
        var processors = profile["processors"].Array
            .Where(p => p["sides"].Array.Select(s => s.String).Where(s => s != null).ToList() is { } sides
                        && (sides.Count == 0 || sides.Contains("client")))
            .ToList();
        if (processors.Count > 0)
        {
            var java = await JavaManager.Select(vanilla, job);
            for (var i = 0; i < processors.Count; i++)
            {
                job.Token.ThrowIfCancellationRequested();
                job.Enter(T.Invoke($"运行安装处理器 {i + 1}/{processors.Count}", $"Running processor {i + 1}/{processors.Count}"), 3, 5);
                job.FileFraction = (double)i / processors.Count;
                var p = processors[i];
                var jarCoords = p["jar"].String;
                var jarRel = jarCoords == null ? null : Maven.Path(jarCoords);
                if (jarRel == null) continue;
                var jar = Path.Combine(libraries, jarRel);
                // 输出均已存在且校验正确时跳过
                var outputs = p["outputs"].Object
                    .Select(kv => (Substitute(kv.Key), Substitute(kv.Value.String ?? "")))
                    .ToList();
                if (outputs.Count > 0 && outputs.All(o =>
                        File.Exists(o.Item1) && Hash.Sha1File(o.Item1) == o.Item2.Trim('\'')))
                    continue;
                var mainClass = Zip.MainClass(jar)
                    ?? throw new LauncherException(T.Invoke($"无法读取处理器 {jarCoords}", $"Couldn't read processor {jarCoords}"));
                var cpList = new List<string> { jar };
                foreach (var c in p["classpath"].Array)
                    if (c.String is { } cn && Maven.Path(cn) is { } crel)
                        cpList.Add(Path.Combine(libraries, crel));
                // Windows 的 classpath 分隔符是分号
                var cp = string.Join(";", cpList);
                var args = p["args"].Array.Select(a => a.String).Where(s => s != null).Select(s => Substitute(s!)).ToList();
                RunProcessor(java.Path, cp, mainClass, args, work);
            }
        }
        WriteVersionJson(versionJson, name, game.Id, targetDir);
        try { Directory.Delete(work, recursive: true); } catch { }
    }

    private static string ResolveData(string v, string installer, string libraries, string work)
    {
        if (v.StartsWith('[') && v.EndsWith(']') && Maven.Path(v[1..^1]) is { } rel)
            return Path.Combine(libraries, rel);
        if (v.StartsWith('\'') && v.EndsWith('\'')) return v[1..^1];
        if (v.StartsWith('/'))
        {
            var dest = Path.Combine(work, v[1..]);
            Zip.Extract(installer, v[1..], dest);
            return dest;
        }
        return v;
    }

    private static void RunProcessor(string java, string classpath, string mainClass, IReadOnlyList<string> args, string cwd)
    {
        var info = new ProcessStartInfo
        {
            FileName = java,
            Arguments = "-cp \"" + classpath + "\" " + mainClass + " " + string.Join(" ", args.Select(a => "\"" + a + "\"")),
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        Log.log($"[Forge] 运行处理器 {mainClass} {string.Join(" ", args)}");
        using var p = Process.Start(info)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
        {
            var text = output.Length > 4000 ? output[^4000..] : output;
            Log.log($"[Forge] 处理器失败：{text}");
            throw new LauncherException(T.Invoke($"安装处理器运行失败（{mainClass}）：", $"Processor failed ({mainClass}): ")
                + (text.Length > 600 ? text[^600..] : text));
        }
    }
}
