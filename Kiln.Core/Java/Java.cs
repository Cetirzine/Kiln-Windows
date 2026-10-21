using System.Diagnostics;
using Microsoft.Win32;

namespace Kiln.Core;

public sealed record JavaInfo(string Path, string Version, int Major, string Arch, bool IsMojang)
{
    public string Id => Path;

    public string Home => System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(Path)) ?? "";

    /// 是否带有基础 CDS 归档（动态 CDS / AOT 缓存依赖它；Mojang 的运行时默认没有）
    public bool HasBaseCDS =>
        new[] { System.IO.Path.Combine("bin", "server", "classes.jsa"),
                System.IO.Path.Combine("bin", "server", "classes_nocoops.jsa"),
                System.IO.Path.Combine("lib", "server", "classes.jsa"),
                System.IO.Path.Combine("lib", "server", "classes_nocoops.jsa"),
                System.IO.Path.Combine("jre", "lib", "server", "classes.jsa") }
        .Any(p => File.Exists(System.IO.Path.Combine(Home, p)));

    public string DisplayName
    {
        get
        {
            var kind = Path.Contains("zulu", StringComparison.OrdinalIgnoreCase) ? "Zulu"
                : Path.Contains("temurin", StringComparison.OrdinalIgnoreCase) ? "Temurin"
                : IsMojang ? "Mojang" : "Java";
            var a = Arch switch { "arm64" => "ARM64", "x86_64" => "x64", "x86" => "x86", _ => Arch };
            return $"{kind} {Version} ({a})";
        }
    }

    /// 与原版类似的兼容性判定
    public bool Compatible(int need) => need <= 8 ? Major == 8 : Major >= need;
}

public static class JavaManager
{
    // MARK: 搜索

    /// Windows 上的 Java 搜索位置：官方启动器运行时（含 PCL2 共用）、Kiln 自身运行时、
    /// Program Files 下的各发行版、JAVA_HOME 与注册表
    public static List<string> SearchRoots()
    {
        var roots = new List<string>
        {
            Paths.Runtime,
            System.IO.Path.Combine(Paths.DefaultMinecraft, "runtime"),
            System.IO.Path.Combine(LauncherSettings.Shared.MinecraftFolder, "runtime"),
        };
        var programDirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        };
        foreach (var pd in programDirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(pd)) continue;
            foreach (var name in new[] { "Java", "Eclipse Adoptium", "Zulu", "Microsoft", "Amazon Corretto", "BellSoft", "Semeru" })
            {
                var dir = System.IO.Path.Combine(pd, name);
                if (Directory.Exists(dir)) roots.Add(dir);
            }
        }
        return roots;
    }

    /// 在目录树中查找 bin/java.exe（限制深度，避免过慢）
    public static List<string> FindJavaBinaries(string root, int depth = 6)
    {
        var result = new List<string>();
        void Walk(string url, int d)
        {
            try
            {
                var java = System.IO.Path.Combine(url, "bin", "java.exe");
                if (File.Exists(java))
                {
                    result.Add(System.IO.Path.GetFullPath(java));
                    return;
                }
                if (d <= 0) return;
                foreach (var item in Directory.EnumerateDirectories(url))
                {
                    var n = System.IO.Path.GetFileName(item);
                    if (n.StartsWith('.') || n.StartsWith('$')) continue;
                    if (new[] { "legal", "lib", "include", "man", "jmods", "conf", "bin", "jre" }.Contains(n, StringComparer.OrdinalIgnoreCase)) continue;
                    Walk(item, d - 1);
                }
            }
            catch { }
        }
        Walk(root, depth);
        return result;
    }

    /// 注册表中的 Java 安装位置（JavaSoft / Adoptium / Microsoft 等写入 HKLM）
    private static IEnumerable<string> RegistryJavaHomes()
    {
        var keys = new[]
        {
            @"SOFTWARE\JavaSoft\JDK", @"SOFTWARE\JavaSoft\JRE", @"SOFTWARE\JavaSoft\Java Runtime Environment",
            @"SOFTWARE\Eclipse Adoptium", @"SOFTWARE\Microsoft\JDK",
        };
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                foreach (var k in keys)
                {
                    using var key = baseKey.OpenSubKey(k);
                    if (key == null) continue;
                    foreach (var sub in key.GetSubKeyNames())
                    {
                        using var sk = key.OpenSubKey(sub);
                        if (sk?.GetValue("JavaHome") is string home && Directory.Exists(home))
                            yield return home;
                    }
                }
            }
        }
    }

    public static async Task<IReadOnlyList<JavaInfo>> Search(CancellationToken cancellationToken = default)
    {
        var result = await Task.Run(async () =>
        {
            var paths = new List<string>();
            foreach (var root in SearchRoots()) paths.AddRange(FindJavaBinaries(root));
            foreach (var home in RegistryJavaHomes()) paths.Add(System.IO.Path.Combine(home, "bin", "java.exe"));
            paths.AddRange(LauncherSettings.Shared.CustomJavas);
            var env = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(env)) paths.Add(System.IO.Path.Combine(env, "bin", "java.exe"));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var infos = new List<JavaInfo>();
            foreach (var p in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var real = System.IO.Path.GetFullPath(p);
                    if (!seen.Add(real)) continue;
                    if (await InspectAsync(real, cancellationToken).ConfigureAwait(false) is { } info) infos.Add(info);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch { }
            }
            return infos
                .OrderByDescending(a => a.Major)
                .ThenByDescending(a => a.Version, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);
        return result;
    }

    /// 读取 Java 的版本与架构：优先读 release 文件，否则执行 java -XshowSettings
    public static JavaInfo? Inspect(string path) => InspectAsync(path).GetAwaiter().GetResult();
    internal static async Task<JavaInfo?> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(path)) return null;
            cancellationToken.ThrowIfCancellationRequested();
            var isMojang = IsUnder(path, Paths.Runtime) || IsUnder(path, System.IO.Path.Combine(Paths.DefaultMinecraft, "runtime"));
            var home = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(path)) ?? "";
            var releaseFile = System.IO.Path.Combine(home, "release");
            if (File.Exists(releaseFile))
            {
                var props = new Dictionary<string, string>();
                foreach (var line in File.ReadAllLines(releaseFile))
                {
                    var i = line.IndexOf('=');
                    if (i <= 0) continue;
                    props[line[..i].Trim()] = line[(i + 1)..].Trim().Trim('"');
                }
                if (props.TryGetValue("JAVA_VERSION", out var v) && props.TryGetValue("OS_ARCH", out var a))
                    return new JavaInfo(path, v, MajorOf(v), NormalizeArch(a), isMojang);
            }
            var command = await JavaCommand.Run(path, new[] { "-XshowSettings:properties", "-version" }, TimeSpan.FromSeconds(8), cancellationToken).ConfigureAwait(false);
            if (command.ExitCode != 0) return null;
            var output = command.Output;
            string? Prop(string k)
            {
                foreach (var raw in output.Split('\n'))
                {
                    var t = raw.Trim();
                    var separator = t.IndexOf('=');
                    if (separator >= 0 && t[..separator].Trim() == k) return t[(separator + 1)..].Trim();
                }
                return null;
            }
            var version = Prop("java.version");
            var arch = Prop("os.arch");
            if (version == null || arch == null || version.Length > 128 || arch.Length > 32 || MajorOf(version) <= 0) return null;
            return new JavaInfo(path, version, MajorOf(version), NormalizeArch(arch), isMojang);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    internal static bool IsUnder(string path, string directory) => System.IO.Path.GetFullPath(path).StartsWith(System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(directory)) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static int MajorOf(string v)
    {
        var parts = new List<int>();
        var num = "";
        foreach (var c in v)
        {
            if (char.IsDigit(c)) num += c;
            else
            {
                if (num.Length > 0 && parts.Count < 3) parts.Add(int.Parse(num));
                num = "";
            }
        }
        if (num.Length > 0 && parts.Count < 3) parts.Add(int.Parse(num));
        if (parts.Count == 0) return 0;
        return parts[0] == 1 && parts.Count > 1 ? parts[1] : parts[0];
    }

    public static string NormalizeArch(string a) => a.Trim().ToLowerInvariant() switch
    {
        "aarch64" or "arm64" or "aarch64.dll" => "arm64",
        "amd64" or "x64" or "x86_64" => "x86_64",
        "x86" or "i386" or "i486" or "i586" or "i686" => "x86",
        var other => other,
    };

    // MARK: 选择

    /// 选择合适的 Java；若没有则自动下载 Mojang 提供的 Java 运行时
    public static async Task<JavaInfo> Select(McInstance instance, Job? job = null)
    {
        var need = instance.JavaMajor;
        // 版本独立设置或全局设置中手动指定的 Java
        var manual = instance.Setup.JavaPath ?? LauncherSettings.Shared.JavaPath;
        if (!string.IsNullOrEmpty(manual) && await Task.Run(() => InspectAsync(manual, job?.Token ?? default), job?.Token ?? default).ConfigureAwait(false) is { } manualInfo)
        {
            return manualInfo;
        }
        var all = await Search(job?.Token ?? default).ConfigureAwait(false);
        List<JavaInfo> Rank(IEnumerable<JavaInfo> list) => list
            .OrderByDescending(a => a.Compatible(need))
            .ThenBy(a => Math.Abs(a.Major - need))
            .ThenByDescending(a => a.IsMojang)
            .ToList();

        var arch = instance.PreferredArch;
        if (Rank(all.Where(j => j.Arch == arch && j.Compatible(need))) is { Count: > 0 } best && best[0] is { } b)
        {
            Log.log($"[Java] 选择 {b.Path}（{b.Version}，{b.Arch}）");
            return await Prepared(b, job).ConfigureAwait(false);
        }
        Log.log($"[Java] 没有可用的 Java（需要 {need}，{arch}），开始下载 {instance.JavaComponent}");
        job?.Stage = T.Invoke($"正在下载 Java（{instance.JavaComponent}）", $"Downloading Java ({instance.JavaComponent})");
        return await Prepared(await DownloadRuntime(instance.JavaComponent, arch, job).ConfigureAwait(false), job).ConfigureAwait(false);
    }

    /// 为启动器管理的运行时生成基础 CDS 归档（Mojang 的运行时默认没有），使类数据共享与 AOT 缓存生效
    private static async Task<JavaInfo> Prepared(JavaInfo java, Job? job)
    {
        try
        {
            if (!LauncherSettings.Shared.ClassDataSharing || java.HasBaseCDS || java.Major < 11 || !IsUnder(java.Path, Paths.Runtime)) return java;
            var start = DateTime.UtcNow;
            var result = await JavaCommand.Run(java.Path, new[] { "-Xshare:dump" }, TimeSpan.FromSeconds(90), job?.Token ?? default).ConfigureAwait(false);
            Log.log($"[Java] 基础 CDS 归档（{(DateTime.UtcNow - start).TotalSeconds:0.0} 秒）：exit={result.ExitCode}, ready={java.HasBaseCDS}");
        }
        catch (OperationCanceledException) when (job?.Token.IsCancellationRequested == true) { throw; }
        catch (Exception error) { Log.log("[Java] 无法生成基础 CDS 归档：" + error.Friendly()); }
        return java;
    }

    // MARK: 下载 Mojang Java 运行时

    public const string RuntimeIndex =
        "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

    public static string PlatformKey(string arch) => arch == "arm64" ? "windows-arm64" : "windows-x64";

    public static async Task<JavaInfo> DownloadRuntime(string component, string arch, Job? job = null)
    {
        var index = await Net.json(RuntimeIndex, Net.Kind.File, job?.Token ?? default);
        var platform = PlatformKey(arch);
        var entry = index[platform][component][0];
        if (!entry.Exists && arch == "arm64")
        {
            // ARM64 Windows 上没有该组件时，退回 x64 版本（x64 Java 在 ARM64 Windows 上可正常运行）
            platform = PlatformKey("x86_64");
            entry = index[platform][component][0];
        }
        var manifestUrl = entry["manifest"]["url"].String
            ?? throw new LauncherException(T.Invoke(
                $"Mojang 未提供适用于此电脑的 Java 运行时 {component}",
                $"Mojang does not provide Java runtime {component} for this computer"));
        var manifest = await Net.json(manifestUrl, Net.Kind.File, job?.Token ?? default);
        var root = System.IO.Path.Combine(Paths.Runtime, component, platform);
        var items = new List<DownloadItem>();
        var executables = new List<string>();
        foreach (var (rel, f) in manifest["files"].Object)
        {
            var dest = System.IO.Path.Combine(root, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
            switch (f["type"].String)
            {
                case "directory":
                    Directory.CreateDirectory(dest);
                    break;
                case "link":
                    // Windows 上忽略符号链接项（Mojang 的 Windows 运行时不含 link）
                    break;
                default:
                    var raw = f["downloads"]["raw"];
                    if (raw["url"].String is not { } url) continue;
                    items.Add(new DownloadItem(url, dest, raw["sha1"].String,
                        raw["size"].Double is { } sz ? (long)sz : null));
                    if (f["executable"].Bool == true) executables.Add(dest);
                    break;
            }
        }
        job?.Stage = T.Invoke($"下载 Java {component}", $"Download Java {component}");
        await Downloader.Run(items, job);
        // Windows 上无需设置可执行位
        return FindJavaBinaries(root).Select(Inspect).FirstOrDefault(i => i != null)
            ?? throw new LauncherException(T.Invoke(
                $"Mojang 未提供适用于此电脑的 Java 运行时 {component}",
                $"Mojang does not provide Java runtime {component} for this computer"));
    }
}
