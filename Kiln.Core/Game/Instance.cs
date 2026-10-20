using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Kiln.Core;

/// 版本 JSON 中 rules 的判定环境
public struct RuleContext
{
    /// "arm64" 或 "x86_64"，取决于将要使用的 Java 架构
    public string Arch;
    public bool Demo;
    public bool CustomResolution;

    public RuleContext(string? arch = null, bool demo = false, bool customResolution = true)
    {
        Arch = arch ?? ArchInfo.Native;
        Demo = demo;
        CustomResolution = customResolution;
    }

    public static string OsVersion => Environment.OSVersion.Version.ToString();

    /// 对应原版 McJsonRuleCheck，但以 Windows 为目标系统
    public bool Check(Json rules)
    {
        var list = rules.Array;
        if (list.Count == 0) return true;
        var allowed = false;
        foreach (var rule in list)
        {
            var match = true;
            var os = rule["os"];
            var name = os["name"].String;
            if (name != null)
            {
                if (name == "windows")
                {
                    var v = os["version"].String;
                    if (v != null && !Regex.IsMatch(OsVersion, v)) match = false;
                }
                else if (name != "unknown")
                {
                    match = false;
                }
            }
            var a = os["arch"].String;
            if (a != null)
            {
                switch (a)
                {
                    case "x86": match = match && Arch == "x86"; break;
                    case "arm64" or "aarch64": match = match && Arch == "arm64"; break;
                    case "x86_64" or "x64" or "amd64": match = match && Arch == "x86_64"; break;
                }
            }
            var features = rule["features"].Object;
            if (features.Count > 0)
            {
                foreach (var (k, v) in features)
                {
                    var want = v.Bool ?? true;
                    bool have = k switch
                    {
                        "is_demo_user" => Demo,
                        "has_custom_resolution" => CustomResolution,
                        _ => false, // quick play 等由启动器另行处理
                    };
                    if (want != have) match = false;
                }
            }
            if (rule["action"].String == "allow")
            {
                if (match) allowed = true;
            }
            else if (match)
            {
                allowed = false;
            }
        }
        return allowed;
    }
}

public static class ArchInfo
{
    public static string Native => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => "arm64",
        _ => "x86_64",
    };
}

/// 支持库
public sealed class McLibrary
{
    public string Name = "";
    public string Path = "";
    public string? Url;
    public string? Sha1;
    public long? Size;
    /// 旧式 natives（需要解压到 natives 文件夹）
    public bool IsNatives;
    /// 新式 natives（natives-windows / natives-windows-arm64），既在 classpath 中，也需要按架构解压
    public string? NativeArch;
    public IReadOnlyList<string> Exclude = Array.Empty<string>();

    public DownloadItem? Download =>
        Url is not { Length: > 0 } u ? null : new DownloadItem(u, Path, Sha1, Size);
}

public static class Maven
{
    /// group:artifact:version[:classifier][@ext] → group/artifact/version/artifact-version[-classifier].ext
    public static string? Path(string name, string? classifier = null)
    {
        var main = name;
        var ext = "jar";
        var at = name.LastIndexOf('@');
        if (at >= 0)
        {
            ext = name[(at + 1)..];
            main = name[..at];
        }
        var parts = main.Split(':');
        if (parts.Length < 3) return null;
        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var cls = parts.Length >= 4 ? parts[3] : null;
        if (classifier != null) cls = classifier;
        var file = $"{artifact}-{version}" + (cls != null ? $"-{cls}" : "") + $".{ext}";
        return $"{group}/{artifact}/{version}/{file}";
    }

    /// 用于去重的键（group:artifact[:classifier]）
    public static string Key(string name)
    {
        var parts = name.Split('@')[0].Split(':');
        if (parts.Length < 3) return name;
        return string.Join(":", new[] { parts[0], parts[1] }.Concat(parts.Skip(3)));
    }
}

/// 本地的一个 Minecraft 版本（实例）
public sealed class McInstance : IEquatable<McInstance>
{
    public enum Category
    {
        Modded = 0, Release, Snapshot, Old, AprilFool, Error,
    }

    public string Name { get; }
    /// 游戏根目录（.minecraft）
    public string Folder { get; }
    public string Dir { get; }
    /// 自身的 JSON
    public readonly Json Raw;
    /// 与 inheritsFrom 合并后的 JSON
    public Json Json { get; private set; } = Json.Null;
    public string? Error { get; private set; }
    public IReadOnlyList<string> Parents { get; private set; } = Array.Empty<string>();

    public bool Equals(McInstance? other) => other != null
        && string.Equals(Dir, other.Dir, StringComparison.OrdinalIgnoreCase);
    public override bool Equals(object? obj) => obj is McInstance i && Equals(i);
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Dir);

    public McInstance(string folder, string name)
    {
        Folder = folder;
        Name = name;
        Dir = System.IO.Path.Combine(folder, "versions", name);
        var jsonPath = System.IO.Path.Combine(Dir, $"{name}.json");
        Json raw;
        try
        {
            raw = Json.Parse(File.ReadAllBytes(jsonPath));
        }
        catch
        {
            raw = Json.Null;
            Error = T.Invoke("缺少版本 JSON 文件", "Missing version JSON");
        }
        this.Raw = raw;
        Reload();
    }

    public void Reload()
    {
        if (!Raw.Exists) return;
        Error = null;
        var parents = new List<string>();
        var merged = Raw;
        var current = Raw;
        var depth = 0;
        while (current["inheritsFrom"].String is { Length: > 0 } parent && depth < 8)
        {
            depth++;
            parents.Add(parent);
            var path = System.IO.Path.Combine(Folder, "versions", parent, $"{parent}.json");
            Json pj;
            try
            {
                pj = Json.Parse(File.ReadAllBytes(path));
            }
            catch
            {
                // 依赖的原版尚未下载，启动/补全文件时会自动下载
                Parents = parents;
                Json = merged;
                return;
            }
            merged = Merge(merged, pj);
            current = pj;
        }
        Parents = parents;
        Json = merged;
    }

    public string? MissingParent
    {
        get
        {
            if (Parents.Count == 0 || Parents[^1] is not { } p) return null;
            return File.Exists(System.IO.Path.Combine(Folder, "versions", p, $"{p}.json")) ? null : p;
        }
    }

    public static Json Merge(Json child, Json parent)
    {
        var result = new Dictionary<string, Json>(parent.Object);
        foreach (var (k, v) in child.Object)
        {
            switch (k)
            {
                case "libraries":
                    var libs = new List<Json>(v.Array);
                    libs.AddRange(parent["libraries"].Array);
                    result[k] = Json.From(libs);
                    break;
                case "arguments":
                    var args = new Dictionary<string, Json>(parent["arguments"].Object);
                    foreach (var (ak, av) in v.Object)
                    {
                        var existing = args.TryGetValue(ak, out var cur) ? cur.Array : Array.Empty<Json>();
                        var combined = new List<Json>(existing);
                        combined.AddRange(av.Array);
                        args[ak] = Json.From(combined);
                    }
                    result[k] = Json.From(args);
                    break;
                case "inheritsFrom":
                    continue;
                default:
                    result[k] = v;
                    break;
            }
        }
        if (parent["inheritsFrom"].Exists) result["inheritsFrom"] = parent["inheritsFrom"];
        else result.Remove("inheritsFrom");
        result["id"] = child["id"];
        return Json.From(result);
    }

    // MARK: 版本信息

    public DateTime ReleaseTime
    {
        get
        {
            var s = Json["releaseTime"].String ?? Json["time"].String ?? "";
            return VersionManifest.ParseDate(s) ?? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }
    }

    public string Type => Json["type"].String ?? "release";

    /// 支持 Quick Play（--quickPlaySingleplayer / --quickPlayMultiplayer）：23w14a（1.20 快照）及以后
    public bool SupportsQuickPlay => ReleaseTime > new DateTime(2023, 4, 4, 0, 0, 0, DateTimeKind.Utc);

    /// 原版版本号
    public string VanillaVersion
    {
        get
        {
            if (Raw["clientVersion"].String is { Length: > 0 } c) return c;
            if (Parents.Count > 0) return Parents[^1];
            if (Raw["jar"].String is { Length: > 0 } j) return j;
            // 某些整合包的 patches 字段（HMCL 格式）
            foreach (var p in Raw["patches"].Array)
                if (p["id"].String == "game" && p["version"].String is { } v) return v;
            return Raw["id"].String ?? Name;
        }
    }

    private IReadOnlyList<string> LibraryNames => Json["libraries"].Array.Select(l => l["name"].String ?? "").ToList();

    public IReadOnlyList<string> Loaders
    {
        get
        {
            var r = new List<string>();
            var names = LibraryNames;
            if (names.Any(n => n.StartsWith("net.fabricmc:fabric-loader:", StringComparison.Ordinal))) r.Add("Fabric");
            if (names.Any(n => n.StartsWith("org.quiltmc:quilt-loader:", StringComparison.Ordinal))) r.Add("Quilt");
            if (names.Any(n => n.StartsWith("net.neoforged", StringComparison.Ordinal))) r.Add("NeoForge");
            else if (names.Any(n => n.StartsWith("net.minecraftforge:forge:", StringComparison.Ordinal)
                || n.StartsWith("net.minecraftforge:fmlloader:", StringComparison.Ordinal)
                || n.StartsWith("net.minecraftforge:minecraftforge:", StringComparison.Ordinal))) r.Add("Forge");
            if (names.Any(n => n.ToLowerInvariant().StartsWith("optifine:optifine"))) r.Add("OptiFine");
            if (names.Any(n => n.StartsWith("com.mumfrey:liteloader:", StringComparison.Ordinal))) r.Add("LiteLoader");
            return r;
        }
    }

    public string? LoaderVersion(string loader)
    {
        string prefix;
        switch (loader)
        {
            case "Fabric": prefix = "net.fabricmc:fabric-loader:"; break;
            case "Quilt": prefix = "org.quiltmc:quilt-loader:"; break;
            case "Forge" or "NeoForge":
            {
                // 1.17+：游戏参数中带有版本号
                var key = loader == "Forge" ? "--fml.forgeVersion" : "--fml.neoForgeVersion";
                var args = Json["arguments"]["game"].Array.Select(a => a.String).Where(s => s != null).ToList()!;
                var i = args.IndexOf(key);
                if (i >= 0 && i + 1 < args.Count) return args[i + 1];
                // 1.20.1 的 NeoForge 仍使用 --fml.forgeVersion
                if (loader == "NeoForge")
                {
                    i = args.IndexOf("--fml.forgeVersion");
                    if (i >= 0 && i + 1 < args.Count) return args[i + 1];
                }
                // 旧版：net.minecraftforge:forge:1.12.2-14.23.5.2864；1.20.1 的 NeoForge：net.neoforged:forge:1.20.1-47.1.106
                var libs = loader == "Forge"
                    ? new[] { "net.minecraftforge:forge:" }
                    : new[] { "net.neoforged:neoforge:", "net.neoforged:forge:" };
                var name = LibraryNames.FirstOrDefault(n => libs.Any(l => n.StartsWith(l, StringComparison.Ordinal)));
                var lib = libs.FirstOrDefault(l => name != null && name.StartsWith(l, StringComparison.Ordinal));
                if (name == null || lib == null) return null;
                var v = name[lib.Length..].Split(':')[0];
                var game = VanillaVersion;
                if (v.StartsWith(game + "-")) v = v[(game.Length + 1)..];
                if (v.EndsWith("-" + game)) v = v[..^(game.Length + 1)];
                return v;
            }
            default: return null;
        }
        return LibraryNames.FirstOrDefault(n => n.StartsWith(prefix, StringComparison.Ordinal)) is { Length: > 0 } n
            ? n[prefix.Length..]
            : null;
    }

    public bool IsModded => Loaders.Any(l => l != "OptiFine");

    public Category Kind
    {
        get
        {
            if (Error != null || !Json.Exists) return Category.Error;
            if (IsModded) return Category.Modded;
            if (McVersionKinds.IsAprilFool(VanillaVersion)) return Category.AprilFool;
            return Type switch
            {
                "release" => Category.Release,
                "snapshot" or "pending" => Category.Snapshot,
                _ => Category.Old,
            };
        }
    }

    /// 版本图标：优先使用自定义的方块
    public string Icon
    {
        get
        {
            if (Setup.Icon is { Length: > 0 } custom) return custom;
            return DefaultIcon;
        }
    }

    /// 按版本类型自动选择的图标
    public string DefaultIcon
    {
        get
        {
            if (Kind == Category.Error) return "RedstoneBlock";
            var l = Loaders;
            if (l.Contains("NeoForge")) return "NeoForge";
            if (l.Contains("Fabric") || l.Contains("Quilt")) return "Fabric";
            if (l.Contains("Forge")) return "Anvil";
            if (l.Contains("OptiFine")) return "GrassPath";
            return Kind switch
            {
                Category.Snapshot => "CommandBlock",
                Category.Old => "CobbleStone",
                Category.AprilFool => "GoldBlock",
                _ => "Grass",
            };
        }
    }

    public string Description
    {
        get
        {
            if (Error != null) return Error;
            var parts = new List<string> { VanillaVersion };
            foreach (var l in Loaders) parts.Add(LoaderVersion(l) is { } v ? $"{l} {v}" : l);
            if (parts.Count == 1)
            {
                parts.Add(Kind switch
                {
                    Category.Release => T.Invoke("正式版", "Release"),
                    Category.Snapshot => T.Invoke("快照", "Snapshot"),
                    Category.Old => T.Invoke("远古版", "Old Alpha/Beta"),
                    Category.AprilFool => T.Invoke("愚人节版", "April Fools"),
                    _ => "",
                });
            }
            return string.Join(", ", parts);
        }
    }

    /// 所需的 Java 主版本
    public int JavaMajor => (int)(Json["javaVersion"]["majorVersion"].Int ?? 8);

    public string JavaComponent => Json["javaVersion"]["component"].String ?? "jre-legacy";

    public LauncherSettings.InstanceSetup Setup => LauncherSettings.Shared.InstanceSetupOf(Dir);

    /// 游戏时长的文字：“3 小时 20 分钟”；compact 时为“3.3 小时”。从未玩过时为 null
    public static string? FormatPlayTime(double? seconds, bool compact = false)
    {
        if (seconds is not { } s || s < 1) return null;
        var minutes = (int)(s / 60);
        if (minutes < 1) return T.Invoke("不到 1 分钟", "< 1 min");
        if (minutes < 60) return T.Invoke($"{minutes} 分钟", $"{minutes} min");
        var hours = minutes / 60.0;
        if (compact)
        {
            var h = hours < 100 ? hours.ToString("F1", CultureInfo.InvariantCulture) : ((int)hours).ToString();
            return T.Invoke($"{h} 小时", $"{h} h");
        }
        var hh = minutes / 60;
        var mm = minutes % 60;
        if (mm == 0) return T.Invoke($"{hh} 小时", $"{hh} h");
        return T.Invoke($"{hh} 小时 {mm} 分钟", $"{hh} h {mm} min");
    }

    /// 是否开启版本隔离
    public bool Isolated
    {
        get
        {
            if (Setup.Isolation is { } v) return v;
            // 已有 mods、saves 等文件夹时视为已隔离（与原版的判定思路一致）
            foreach (var sub in new[] { "mods", "saves", "resourcepacks", "options.txt" })
                if (File.Exists(System.IO.Path.Combine(Dir, sub))) return true;
            return LauncherSettings.Shared.Isolation switch
            {
                IsolationMode.None => false,
                IsolationMode.All => true,
                _ => IsModded || Kind == Category.Snapshot || Kind == Category.Old || Kind == Category.AprilFool,
            };
        }
    }

    public string GameDir => Isolated ? Dir : Folder;

    /// 支持 inheritsFrom 的客户端 jar 位置
    public string ClientJar
    {
        get
        {
            var own = System.IO.Path.Combine(Dir, $"{Name}.jar");
            if (File.Exists(own)) return own;
            if (Raw["jar"].String is { Length: > 0 } jar)
                return System.IO.Path.Combine(Folder, "versions", jar, $"{jar}.jar");
            if (Parents.Count > 0)
                return System.IO.Path.Combine(Folder, "versions", Parents[^1], $"{Parents[^1]}.jar");
            return own;
        }
    }

    public string NativesDir => System.IO.Path.Combine(Dir, $"{Name}-natives");

    // MARK: 支持库

    /// 该版本需要的全部支持库（Windows：natives 选择 natives-windows；不含 Apple 芯片的 LWJGL 替换）
    public IReadOnlyList<McLibrary> Libraries(RuleContext ctx)
    {
        var result = new List<McLibrary>();
        var seen = new HashSet<string>();
        var libDir = System.IO.Path.Combine(Folder, "libraries");

        void Add(McLibrary lib, string key)
        {
            if (seen.Add(key)) result.Add(lib);
        }

        McLibrary? Artifact(string name, Json art, Json lib)
        {
            var rel = art["path"].String ?? Maven.Path(name);
            if (rel == null) return null;
            string? url = art["url"].String;
            if (url == null && !lib["downloads"].Exists)
            {
                var baseUrl = lib["url"].String ?? "https://libraries.minecraft.net/";
                url = (baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/") + rel;
            }
            string? nativeArch = null;
            if (name.Contains(":natives-windows"))
                nativeArch = name.EndsWith("arm64") ? "arm64" : "x86_64";
            return new McLibrary
            {
                Name = name, Path = System.IO.Path.Combine(libDir, rel), Url = url,
                Sha1 = art["sha1"].String, Size = art["size"].Double is { } sz ? (long)sz : null,
                IsNatives = false, NativeArch = nativeArch,
            };
        }

        foreach (var lib in Json["libraries"].Array)
        {
            var name = lib["name"].String;
            if (name == null) continue;
            if (lib["rules"].Exists && !ctx.Check(lib["rules"])) continue;
            var natives = lib["natives"];
            if (natives.Exists)
            {
                // 旧式 natives：同一条目中也可能带有需要加入 classpath 的 artifact（如 java-objc-bridge）
                if (lib["downloads"]["artifact"].Exists
                    && Artifact(name, lib["downloads"]["artifact"], lib) is { } a)
                {
                    Add(a, Maven.Key(name));
                }
                var cls = natives["windows"].String;
                if (cls == null) continue;
                cls = cls.Replace("${arch}", "64");
                var art = lib["downloads"]["classifiers"][cls];
                var rel = art["path"].String ?? Maven.Path(name, cls) ?? "";
                var baseUrl = lib["url"].String ?? "https://libraries.minecraft.net/";
                Add(new McLibrary
                {
                    Name = name, Path = System.IO.Path.Combine(libDir, rel),
                    Url = art["url"].String ?? (baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/") + rel,
                    Sha1 = art["sha1"].String, Size = art["size"].Double is { } sz ? (long)sz : null,
                    IsNatives = true, NativeArch = null,
                    Exclude = lib["extract"]["exclude"].Array.Select(e => e.String ?? "").Where(s => s.Length > 0).ToList(),
                }, Maven.Key(name) + ":" + cls);
                continue;
            }
            if (Artifact(name, lib["downloads"]["artifact"], lib) is { } lib2) Add(lib2, Maven.Key(name));
        }
        return result;
    }

    /// 使用的 LWJGL 版本（如 "3.2.2"、"2.9.4-nightly-20150209"）
    public string? LwjglVersion
    {
        get
        {
            foreach (var n in LibraryNames)
                if (n.StartsWith("org.lwjgl:lwjgl:", StringComparison.Ordinal)
                    || n.StartsWith("org.lwjgl.lwjgl:lwjgl:", StringComparison.Ordinal))
                {
                    var parts = n.Split(':');
                    if (parts.Length >= 3) return parts[2];
                }
            return null;
        }
    }

    /// 是否为 LWJGL 2（1.12.2 及以前）
    public bool IsLwjgl2 => LwjglVersion?.StartsWith("2.") ?? false;

    /// 是否需要原生库
    public bool NeedsNatives => Json["libraries"].Array.Any(l =>
        l["natives"].Exists || (l["name"].String ?? "").Contains("natives-windows"));

    // MARK: 资源文件

    public string AssetIndexId => Json["assetIndex"]["id"].String ?? Json["assets"].String ?? "legacy";

    /// 期望的 Java 架构（Windows：跟随本机架构）
    public string PreferredArch => ArchInfo.Native;
}

/// 版本文件夹扫描
public static class InstanceScanner
{
    public static IReadOnlyList<McInstance> Scan(string folder)
    {
        var versions = System.IO.Path.Combine(folder, "versions");
        string[] dirs;
        try { dirs = Directory.GetDirectories(versions); }
        catch { return Array.Empty<McInstance>(); }
        return dirs
            .Select(d => System.IO.Path.GetFileName(d)!)
            .Where(n => n.Length > 0 && !n.StartsWith('.'))
            .Select(n => new McInstance(folder, n))
            .Where(i => i.Raw.Exists || File.Exists(System.IO.Path.Combine(i.Dir, $"{i.Name}.jar")))
            .OrderBy(i => (int)i.Kind)
            .ThenByDescending(i => i.ReleaseTime)
            .ToList();
    }
}
