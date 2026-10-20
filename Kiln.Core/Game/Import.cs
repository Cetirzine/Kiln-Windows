using System.Globalization;

namespace Kiln.Core;

/// 从其他启动器导入版本：Prism Launcher / MultiMC / PolyMC（instance.cfg + mmc-pack.json）、
/// CurseForge App（minecraftinstance.json）、官方启动器（launcher_profiles.json 中使用独立游戏目录的配置）。
/// 导入时在 Kiln 中重新安装同样的游戏与加载器，再把 Mod、存档、配置等复制到新版本的独立游戏目录。
public static class LauncherImport
{
    public enum Source { Prism, MultiMC, PolyMC, CurseForge, Official }

    public static string DisplayName(this Source s) => s switch
    {
        Source.Prism => "Prism Launcher",
        Source.MultiMC => "MultiMC",
        Source.PolyMC => "PolyMC",
        Source.CurseForge => "CurseForge",
        _ => "Minecraft Launcher",
    };

    public sealed record Loader(string Kind, string Version);

    public sealed class Candidate
    {
        public Source Source;
        public string Name = "";
        public string GameVersion = "";
        public Loader? LoaderInfo;
        /// 游戏目录（mods、saves、config 等所在的位置）
        public string ContentDir = "";
        public string? JvmArgs;
        public double? MemoryGB;
        public DateTime? LastPlayed;
        /// 不能导入的原因（使用了 Kiln 不支持的加载器或组件）
        public string? Unsupported;
        /// 可以导入，但需要注意的地方
        public string? Warning;
        /// 从压缩包解压出的临时目录，导入结束后删除
        public string? Temporary;

        public string Id => ContentDir + "|" + Name;

        public int ModCount
        {
            get
            {
                try
                {
                    return Directory.GetFiles(Path.Combine(ContentDir, "mods"))
                        .Count(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                                    || f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase));
                }
                catch { return 0; }
            }
        }

        public int WorldCount
        {
            get
            {
                try
                {
                    return Directory.GetDirectories(Path.Combine(ContentDir, "saves"))
                        .Count(d => !Path.GetFileName(d).StartsWith('.'));
                }
                catch { return 0; }
            }
        }

        public string Summary => string.Join(" · ",
            new[] { GameVersion }.Concat(LoaderInfo != null ? new[] { $"{LoaderInfo.Kind} {LoaderInfo.Version}" } : Array.Empty<string>()));
    }

    /// 复制时跳过的内容：游戏文件由 Kiln 重新安装，日志与缓存没有意义
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "versions", "libraries", "assets", "runtime", "natives", "logs", ".fabric", ".quilt", ".mixin.out",
        "instance.cfg", "mmc-pack.json", "minecraftinstance.json", ".curseclient", "profileImage",
        "launcher_profiles.json", "launcher_accounts.json", "launcher_settings.json", "usercache.json",
        "crash-reports", ".DS_Store", "patches",
    };

    // MARK: 查找

    public static IReadOnlyList<Candidate> Discover()
    {
        var result = new List<Candidate>();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // Prism / PolyMC / MultiMC：实例目录可以在配置中修改（Windows 数据在 %APPDATA%）
        var mmcLike = new[]
        {
            (Source.Prism, Path.Combine(appData, "PrismLauncher"), "prismlauncher.cfg"),
            (Source.PolyMC, Path.Combine(appData, "PolyMC"), "polymc.cfg"),
            (Source.MultiMC, Path.Combine(appData, "MultiMC"), "multimc.cfg"),
        };
        foreach (var (source, dataDir, cfgName) in mmcLike)
        {
            var instances = Path.Combine(dataDir, "instances");
            try
            {
                if (File.Exists(Path.Combine(dataDir, cfgName))
                    && Ini(File.ReadAllText(Path.Combine(dataDir, cfgName))).TryGetValue("InstanceDir", out var custom)
                    && custom.Length > 0)
                {
                    instances = custom.StartsWith('/') || custom.Contains(':')
                        ? ExpandTilde(custom)
                        : Path.Combine(dataDir, custom);
                }
            }
            catch { }
            foreach (var dir in Subdirectories(instances))
                if (MmcInstance(dir, source) is { } c) result.Add(c);
        }
        // CurseForge App
        foreach (var dir in Subdirectories(Path.Combine(home, "Documents", "CurseForge", "Minecraft", "Instances")))
            if (CurseForgeInstance(dir) is { } c) result.Add(c);
        // 官方启动器
        result.AddRange(OfficialProfiles(Paths.DefaultMinecraft));
        return result.OrderByDescending(c => c.LastPlayed ?? DateTime.MinValue).ToList();
    }

    private static string ExpandTilde(string path) =>
        path.StartsWith("~") ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[1..].TrimStart('\\', '/')) : path;

    /// 识别用户选择的文件夹（某个启动器的实例文件夹）
    public static Candidate? Inspect(string folder)
    {
        if (File.Exists(Path.Combine(folder, "minecraftinstance.json"))) return CurseForgeInstance(folder);
        if (File.Exists(Path.Combine(folder, "instance.cfg")))
        {
            // Prism 的 instance.cfg 带有 [General] 分节，MultiMC 没有
            var cfg = "";
            try { cfg = File.ReadAllText(Path.Combine(folder, "instance.cfg")); } catch { }
            var source = folder.Contains("PolyMC", StringComparison.OrdinalIgnoreCase) ? Source.PolyMC
                : folder.Contains("PrismLauncher", StringComparison.OrdinalIgnoreCase) || cfg.Contains("[General]")
                    ? Source.Prism : Source.MultiMC;
            return MmcInstance(folder, source);
        }
        return null;
    }

    // MARK: 导出的压缩包

    /// 压缩包是否是 Prism / MultiMC 导出的实例（含 instance.cfg）
    public static bool IsInstanceArchive(string archive) => InstanceEntry(archive) != null;

    /// instance.cfg 在压缩包中的路径（取层级最浅的一个）
    private static string? InstanceEntry(string archive) =>
        Zip.List(archive)
            .Where(e => (e == "instance.cfg" || e.EndsWith("/instance.cfg")) && !e.StartsWith("__MACOSX"))
            .OrderBy(e => e.Split('/').Length).FirstOrDefault();

    /// 解压 Prism / MultiMC 导出的实例压缩包并识别
    public static Candidate InspectArchive(string archive)
    {
        var entry = InstanceEntry(archive)
            ?? throw new LauncherException(T.Invoke("压缩包中没有 instance.cfg，不是 Prism 或 MultiMC 导出的实例",
                "There's no instance.cfg in the archive, so it isn't a Prism or MultiMC instance export"));
        var tmp = Path.Combine(Paths.Cache, "import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        Zip.ExtractAll(archive, tmp);
        try
        {
            var root = tmp;
            var parts = entry.Split('/');
            foreach (var p in parts[..^1]) root = Path.Combine(root, p);
            var c = Inspect(root)
                ?? throw new LauncherException(T.Invoke("无法识别压缩包中的实例（缺少 Minecraft 版本）",
                    "Couldn't read the instance in the archive (no Minecraft version)"));
            c.Temporary = tmp;
            return c;
        }
        catch
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
            throw;
        }
    }

    // MARK: Prism / MultiMC

    /// Kiln 能够还原的组件；unsup（整合包自动同步）不影响游戏本身，忽略
    private static readonly HashSet<string> KnownComponents = new()
    {
        "net.minecraft", "org.lwjgl", "org.lwjgl3", "net.fabricmc.intermediary", "net.fabricmc.fabric-loader",
        "org.quiltmc.quilt-loader", "org.quiltmc.hashed", "net.minecraftforge", "net.neoforged",
        "com.unascribed.unsup",
    };

    private static Candidate? MmcInstance(string dir, Source source)
    {
        string cfgText;
        try { cfgText = File.ReadAllText(Path.Combine(dir, "instance.cfg")); }
        catch { return null; }
        var cfg = Ini(cfgText);
        var game = cfg.GetValueOrDefault("IntendedVersion");
        Loader? loader = null;
        var unknown = new List<string>();
        try
        {
            var pack = Json.Parse(File.ReadAllBytes(Path.Combine(dir, "mmc-pack.json")));
            foreach (var comp in pack["components"].Array)
            {
                if (comp["uid"].String is not { } uid) continue;
                var display = comp["cachedName"].String ?? uid;
                var v = comp["version"].String ?? comp["cachedVersion"].String;
                if (uid == "net.minecraft" && v != null) game = v;
                // 不认识的组件（Babric、Cleanroom、jar mod 等）无法在 Kiln 中还原；被 patches/ 覆盖的组件同理
                if (!KnownComponents.Contains(uid) || display == "Cleanroom"
                    || File.Exists(Path.Combine(dir, "patches", $"{uid}.json")) && uid != "org.lwjgl" && uid != "org.lwjgl3")
                {
                    unknown.Add(uid == "net.minecraft" ? T.Invoke("修改过的 Minecraft", "a modified Minecraft") : display);
                    continue;
                }
                if (v == null) continue;
                switch (uid)
                {
                    case "net.minecraft": game = v; break;
                    case "net.fabricmc.fabric-loader": loader = new Loader("Fabric", v); break;
                    case "org.quiltmc.quilt-loader": loader = new Loader("Quilt", v); break;
                    case "net.minecraftforge": loader = new Loader("Forge", v); break;
                    case "net.neoforged": loader = new Loader("NeoForge", v); break;
                }
            }
        }
        catch
        {
            if (cfg.GetValueOrDefault("ForgeVersion") is { Length: > 0 } forge)
                loader = new Loader("Forge", forge);
        }
        if (string.IsNullOrEmpty(game)) return null;
        var content = new[] { ".minecraft", "minecraft" }
            .Select(sub => Path.Combine(dir, sub))
            .FirstOrDefault(Directory.Exists) ?? Path.Combine(dir, ".minecraft");
        var c = new Candidate
        {
            Source = source,
            Name = cfg.GetValueOrDefault("name") is { Length: > 0 } n ? n : Path.GetFileName(dir),
            GameVersion = game!,
            LoaderInfo = loader,
            ContentDir = content,
        };
        if (cfg.GetValueOrDefault("OverrideJavaArgs") == "true" && cfg.TryGetValue("JvmArgs", out var args))
            c.JvmArgs = CleanJvmArgs(args);
        if (cfg.GetValueOrDefault("OverrideMemory") == "true"
            && double.TryParse(cfg.GetValueOrDefault("MaxMemAlloc"), NumberStyles.Float, CultureInfo.InvariantCulture, out var mb)
            && mb > 0)
            c.MemoryGB = mb / 1024;
        if (double.TryParse(cfg.GetValueOrDefault("lastLaunchTime"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)
            && ms > 0)
            c.LastPlayed = DateTime.UnixEpoch.AddMilliseconds(ms);
        // 启动前命令（packwiz 等）通常用来下载或更新 Mod，Kiln 不会运行
        if (cfg.GetValueOrDefault("OverrideCommands") == "true"
            && cfg.GetValueOrDefault("PreLaunchCommand") is { Length: > 0 } pre)
        {
            var short1 = pre.Length > 60 ? pre[..60] : pre;
            c.Warning = T.Invoke($"这个实例在启动前运行命令（{short1}）来下载或更新 Mod，Kiln 不会运行它，导入的 Mod 可能不全",
                $"This instance runs a pre-launch command ({short1}) to download or update mods. Kiln won't run it, so some mods may be missing");
        }
        if (unknown.Count > 0)
            c.Unsupported = T.Invoke("使用了 Kiln 暂不支持的组件：", "Uses components Kiln doesn't support: ")
                + string.Join(T.Invoke("、", ", "), unknown);
        return c;
    }

    // MARK: CurseForge

    private static Candidate? CurseForgeInstance(string dir)
    {
        Json j;
        try { j = Json.Parse(File.ReadAllBytes(Path.Combine(dir, "minecraftinstance.json"))); }
        catch { return null; }
        // 旧版 Curse / Twitch 客户端的字段首字母大写（Name、MinecraftVersion）
        var loaderBase = j["baseModLoader"];
        string? Field(string key) =>
            loaderBase[key].String ?? loaderBase[char.ToUpperInvariant(key[0]) + key[1..]].String;
        var game = j["gameVersion"].String ?? Field("minecraftVersion");
        if (string.IsNullOrEmpty(game)) return null;
        Loader? loader = null;
        // name：forge-47.2.0、neoforge-21.1.77、fabric-0.15.7-1.20.1、quilt-0.26.0-1.20.1
        var loaderName = Field("name") ?? "";
        var parts = loaderName.Split('-');
        if (parts.Length >= 2)
        {
            switch (parts[0].ToLowerInvariant())
            {
                case "forge": loader = new Loader("Forge", Field("forgeVersion") ?? parts[1]); break;
                case "neoforge": loader = new Loader("NeoForge", parts[1]); break;
                case "fabric": loader = new Loader("Fabric", parts[1]); break;
                case "quilt": loader = new Loader("Quilt", parts[1]); break;
            }
        }
        var c = new Candidate
        {
            Source = Source.CurseForge,
            Name = j["name"].String is { Length: > 0 } n ? n : Path.GetFileName(dir),
            GameVersion = game!,
            LoaderInfo = loader,
            ContentDir = dir,
        };
        // 只有单独设置过内存时才沿用，否则跟随 Kiln 的自动分配
        if ((j["isMemoryOverride"].Bool ?? true) && j["allocatedMemory"].Double is { } memMb && memMb > 0)
            c.MemoryGB = memMb / 1024;
        if (j["javaArgsOverride"].String is { } args) c.JvmArgs = CleanJvmArgs(args);
        if (j["lastPlayed"].String is { } s) c.LastPlayed = ParseDate(s);
        if (loader == null && loaderName.Length > 0)
            c.Unsupported = T.Invoke("使用了 Kiln 暂不支持的加载器：", "Uses a loader Kiln doesn't support: ") + loaderName;
        return c;
    }

    // MARK: 官方启动器

    /// 只导入使用自己游戏目录的配置；使用默认目录的版本在 Kiln 中本来就能看到
    /// requireFiles 为 false 时不检查游戏目录与版本文件是否存在（用于检查解析）
    public static IReadOnlyList<Candidate> OfficialProfiles(string folder, bool requireFiles = true)
    {
        Json j;
        try { j = Json.Parse(File.ReadAllBytes(Path.Combine(folder, "launcher_profiles.json"))); }
        catch { return Array.Empty<Candidate>(); }
        var result = new List<Candidate>();
        foreach (var (_, p) in j["profiles"].Object)
        {
            var dirPath = p["gameDir"].String;
            var versionId = p["lastVersionId"].String;
            if (string.IsNullOrEmpty(dirPath) || versionId == null || versionId.StartsWith("latest-")) continue;
            var dir = Path.GetFullPath(ExpandTilde(dirPath));
            if (string.Equals(dir, Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)) continue;
            if (requireFiles && !Directory.Exists(dir)) continue;
            var instance = new McInstance(folder, versionId);
            if (requireFiles && !instance.Raw.Exists) continue;
            Loader? loader = null;
            if (instance.Loaders.FirstOrDefault(l => new[] { "Fabric", "Quilt", "Forge", "NeoForge" }.Contains(l)) is { } kind
                && instance.LoaderVersion(kind) is { } v)
            {
                loader = new Loader(kind, v);
            }
            var name = p["name"].String is { Length: > 0 } pn ? pn : versionId;
            var extra = instance.Loaders.Where(l => !new[] { "Fabric", "Quilt", "Forge", "NeoForge" }.Contains(l)).ToList();
            var c = new Candidate
            {
                Source = Source.Official,
                Name = name,
                GameVersion = instance.Raw.Exists ? instance.VanillaVersion : versionId,
                LoaderInfo = loader,
                ContentDir = dir,
            };
            if (p["javaArgs"].String is { Length: > 0 } args)
            {
                if (Launcher.Split(args).FirstOrDefault(a => a.StartsWith("-Xmx")) is { } xmx)
                    c.MemoryGB = MemoryGB(xmx[4..]);
                c.JvmArgs = CleanJvmArgs(args);
            }
            if (p["lastUsed"].String is { } s) c.LastPlayed = ParseDate(s);
            if (extra.Count > 0)
                c.Unsupported = T.Invoke($"{string.Join("、", extra)} 安装在版本里，导入后需要重新安装",
                    $"{string.Join(", ", extra)} is installed into the version; reinstall it after importing");
            result.Add(c);
        }
        return result;
    }

    // MARK: 导入

    /// 安装游戏与加载器，并复制游戏目录内容；返回新版本的名称
    public static async Task<string> Run(Candidate c, string name, string folder, Job job)
    {
        var dir = Path.Combine(folder, "versions", name);
        try
        {
            if (Directory.Exists(dir))
                throw new LauncherException(T.Invoke($"已存在名为 {name} 的版本", $"An instance named {name} already exists"));
            if (c.Unsupported is { } reason) throw new LauncherException(reason);
            await CheckAvailable(c);
            var finished = false;
            try
            {
                await Modpack.InstallGame(c.GameVersion,
                    c.LoaderInfo != null ? (c.LoaderInfo.Kind, c.LoaderInfo.Version) : null, name, folder, job);
                var instance = new McInstance(folder, name);
                var setup = instance.Setup;
                setup.Isolation = true;
                setup.JvmArgs = c.JvmArgs;
                setup.MemoryGB = c.MemoryGB;
                setup.LastPlayed = c.LastPlayed;
                setup.ImportedFrom = c.Temporary == null ? c.ContentDir : null;
                LauncherSettings.Shared.SaveInstanceSetup(setup, instance.Dir);

                job.Enter(T.Invoke("复制 Mod、存档与设置", "Copying mods, worlds and settings"), 0, 1);
                await Task.Run(() => CopyContent(c.ContentDir, dir));
                finished = true;
                return name;
            }
            finally
            {
                if (!finished)
                    try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }
        finally
        {
            if (c.Temporary != null)
                try { Directory.Delete(c.Temporary, recursive: true); } catch { }
        }
    }

    /// 安装前确认游戏与加载器版本存在，给出比 HTTP 404 更清楚的错误
    private static async Task CheckAvailable(Candidate c)
    {
        _ = await VersionManifest.Find(c.GameVersion);
        if (c.LoaderInfo is not { } l) return;
        if (l.Kind is not ("Fabric" or "Quilt")) return;
        var kind = l.Kind == "Fabric" ? ModLoaderKind.Fabric : ModLoaderKind.Quilt;
        IReadOnlyList<ModLoaderKindExtensions.LoaderVersion> list;
        try { list = await kind.Versions(c.GameVersion); }
        catch
        {
            throw new LauncherException(T.Invoke(
                $"{l.Kind} 官方不支持 Minecraft {c.GameVersion}（可能使用了 Babric 等第三方分支）",
                $"{l.Kind} doesn't officially support Minecraft {c.GameVersion} (it may use a fork such as Babric)"));
        }
        if (list.Count == 0 || list.All(x => x.Version != l.Version))
            throw new LauncherException(T.Invoke($"找不到 {l.Kind} {l.Version}（Minecraft {c.GameVersion}）",
                $"{l.Kind} {l.Version} for Minecraft {c.GameVersion} wasn't found"));
    }

    /// 逐项复制；跳过符号链接与重解析点
    public static void CopyContent(string src, string dst)
    {
        string[] items;
        try { items = Directory.EnumerateFileSystemEntries(src).ToArray(); }
        catch { return; }
        var baseName = Path.GetFileName(dst);
        foreach (var item in items)
        {
            var name = Path.GetFileName(item);
            if (Skipped.Contains(name) || name == ".kiln" || name.StartsWith("hs_err_pid")) continue;
            if (name == baseName + ".json" || name == baseName + ".jar") continue;
            try
            {
                var attrs = File.GetAttributes(item);
                if ((attrs & FileAttributes.ReparsePoint) != 0) continue;
            }
            catch { }
            var target = Path.Combine(dst, name);
            try
            {
                // 新安装的版本里可能已经有 options.txt 等文件
                if (File.Exists(target)) File.Delete(target);
                else if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                if (Directory.Exists(item)) CopyDirectory(item, target);
                else File.Copy(item, target);
            }
            catch (Exception e)
            {
                Log.log($"[Import] 复制 {name} 失败：{e.Friendly()}");
            }
        }
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        foreach (var d in Directory.EnumerateDirectories(src)) CopyDirectory(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    /// 不与已有版本重名的名称
    public static string AvailableName(string name, string folder)
    {
        var chars = name.Trim().Select(ch => "/\\:!*?\"<>|".Contains(ch) ? '-' : ch);
        var base0 = new string(chars.ToArray()).TrimStart('.');
        if (base0.Length == 0) base0 = "Imported";
        var n = base0;
        var i = 2;
        while (InstanceManager.ValidateName(n, folder) != null) { n = $"{base0} ({i})"; i++; }
        return n;
    }

    // MARK: 工具

    /// 去掉内存与 -XX: 调优参数：内存单独设置，JVM 由 Kiln 按 Java 版本调优。
    /// 其他启动器的 -XX: 参数常常只适用于特定的 Java 或 GC，混用会让 JVM 拒绝启动；保留 -D 等其他参数
    public static string? CleanJvmArgs(string args)
    {
        var rest = Launcher.Split(args).Where(a =>
            !a.StartsWith("-XX:") && !a.StartsWith("-Xmx") && !a.StartsWith("-Xms")
            && !a.StartsWith("-Xmn") && !a.StartsWith("-Xincgc")).ToList();
        return rest.Count == 0 ? null : string.Join(" ", rest);
    }

    /// 解析 key=value 格式的配置（忽略 [General] 等分节）
    private static Dictionary<string, string> Ini(string text)
    {
        var r = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith('#') || line.StartsWith(';')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var value = line[(eq + 1)..].Trim();
            // Qt 的 INI 会给含特殊字符的值加引号
            if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"')) value = value[1..^1];
            r[line[..eq].Trim()] = value;
        }
        return r;
    }

    private static IReadOnlyList<string> Subdirectories(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return Array.Empty<string>();
            return Directory.GetDirectories(dir)
                .Where(d => !Path.GetFileName(d).StartsWith('.')).ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    private static DateTime? ParseDate(string s)
    {
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.UtcDateTime;
        return null;
    }

    /// "4G" / "4096M" → GB
    private static double? MemoryGB(string s)
    {
        if (s.Length == 0) return null;
        var unit = char.ToLowerInvariant(s[^1]);
        var num = unit is 'g' or 'm' or 'k' ? s[..^1] : s;
        if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            if (unit is 'g' or 'm' or 'k') return null;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return null;
            return v / 1024 / 1024 / 1024;
        }
        return unit switch
        {
            'g' => v,
            'm' => v / 1024,
            'k' => v / 1024 / 1024,
            _ => v / 1024 / 1024 / 1024,
        };
    }
}
