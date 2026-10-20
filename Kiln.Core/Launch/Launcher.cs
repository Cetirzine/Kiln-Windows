using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Kiln.Core;

/// 一次运行中的游戏
public sealed class GameProcess
{
    public enum GameState { Starting, Running, Exited, Crashed }

    public Guid Id { get; } = Guid.NewGuid();
    public string InstanceName { get; }
    private readonly object _gate = new();
    private readonly List<string> _lines = new();
    public event Action<GameProcess.GameState>? StateChanged;
    private GameProcess.GameState _state = GameProcess.GameState.Starting;
    public GameState State
    {
        get => _state;
        set { _state = value; StateChanged?.Invoke(value); }
    }
    /// 由启动器主动结束（不算崩溃）
    public bool Killed { get; set; }
    public int? ExitCode { get; set; }
    public DateTime StartedAt { get; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    public Process? Process { get; set; }
    public string? LogFilePath { get; set; }
    /// 启动时的 Java 与参数（令牌已隐藏），用于导出诊断包
    public McInstance? Instance { get; set; }
    public JavaInfo? Java { get; set; }
    public string? CommandLine { get; set; }
    /// 登录令牌，只保存在内存中，导出诊断包时用于隐藏
    public string? Secret { get; set; }
    private readonly Log4jParser _parser = new();

    public GameProcess(string instanceName) => InstanceName = instanceName;

    public IReadOnlyList<string> Lines { get { lock (_gate) return _lines; } }

    public void Append(string text)
    {
        lock (_gate)
        {
            _lines.AddRange(_parser.Feed(text));
            if (_lines.Count > 5000) _lines.RemoveRange(0, _lines.Count - 5000);
        }
    }

    public void Kill()
    {
        Killed = true;
        try { Process?.Kill(entireProcessTree: true); } catch { }
    }
}

public sealed class LaunchOptions
{
    public string? ServerIp;
    /// 直接进入的单人存档（saves 下的文件夹名，1.20 及以上）
    public string? World;
    public bool Demo;
    /// 仅导出启动脚本
    public string? ExportScript;
}

/// 游戏进程事件，由界面层决定如何响应（隐藏窗口、弹出崩溃提示等）
public sealed class LaunchCallbacks
{
    public Action<GameProcess> OnWindow = _ => { };
    public Action<GameProcess> OnExit = _ => { };
}

public static class Launcher
{
    // MARK: 主流程

    /// 启动游戏。返回 null 表示仅导出了脚本。
    public static async Task<GameProcess?> Launch(McInstance instance, Account account, LaunchOptions options, Job job,
        LaunchCallbacks callbacks)
    {
        const int stages = 5;
        job.Enter(T.Invoke("检查版本", "Checking instance"), 0, stages);
        Precheck(instance, account);

        job.Enter(T.Invoke("登录", "Signing in"), 1, stages);
        var login = await AccountStore.Shared.Login(account);

        job.Enter(T.Invoke("准备 Java", "Preparing Java"), 2, stages);
        var java = await JavaManager.Select(instance, job);
        Log.log($"[Launch] Java：{java.Path}（{java.Version}，{java.Arch}）");

        job.Enter(T.Invoke("检查文件", "Verifying files"), 3, stages);
        await Installer.CompleteFiles(instance, job, stageOffset: 3, stageCount: stages + 2, arch: java.Arch, quick: true);
        job.Enter(T.Invoke("正在启动", "Launching"), 4, stages);
        job.Token.ThrowIfCancellationRequested();

        var ctx = new RuleContext(java.Arch, options.Demo,
            LauncherSettings.Shared.WindowModeSetting != WindowMode.Fullscreen);
        var libs = instance.Libraries(ctx);
        ExtractNatives(instance, libs, java.Arch);
        var args = BuildArguments(instance, java, login, libs, ctx, options);
        Log.log($"[Launch] 启动参数：{Redact(string.Join(" ", args), login)}");

        if (options.ExportScript is { } script)
        {
            ExportScript(java, args, instance.GameDir, script);
            return null;
        }
        LauncherSettings.Shared.SelectedInstance = instance.Name;
        var setup = instance.Setup;
        setup.LastPlayed = DateTime.UtcNow;
        LauncherSettings.Shared.SaveInstanceSetup(setup, instance.Dir);
        var game = Run(instance, java, args, login, callbacks);
        game.Instance = instance;
        game.Java = java;
        game.CommandLine = Redact(string.Join(" ", args), login);
        if (login.UserType != "legacy") game.Secret = login.AccessToken;
        return game;
    }

    /// 累计游戏时长（秒）
    public static void AddPlayTime(GameProcess game, McInstance instance)
    {
        var seconds = ((game.EndedAt ?? DateTime.UtcNow) - game.StartedAt).TotalSeconds;
        if (seconds <= 0 || !Directory.Exists(instance.Dir)) return;
        var setup = instance.Setup;
        setup.PlayTime = (setup.PlayTime ?? 0) + seconds;
        LauncherSettings.Shared.SaveInstanceSetup(setup, instance.Dir);
    }

    public static void Precheck(McInstance instance, Account account)
    {
        // jar URL 中的 “!” 会破坏类路径解析（Windows 路径必然含盘符冒号，分隔符为分号，无需检查冒号）
        if (instance.Dir.Contains('!'))
            throw new LauncherException(T.Invoke($"游戏路径中不能包含 !（{instance.Dir}）",
                $"The game path can't contain ! ({instance.Dir})"));
        if (instance.Error is { } error)
            throw new LauncherException(T.Invoke($"该版本存在问题：{error}", $"This instance is broken: {error}"));
        if (account.Kind == Account.AccountKind.Offline && AccountStore.ValidateOffline(account.Name) is { } e)
            throw new LauncherException(e);
    }

    // MARK: natives

    public static void ExtractNatives(McInstance instance, IReadOnlyList<McLibrary> libs, string arch)
    {
        var dir = instance.NativesDir;
        try { Directory.Delete(dir, recursive: true); } catch { }
        Directory.CreateDirectory(dir);
        foreach (var lib in libs.Where(l => l.IsNatives || l.NativeArch == arch))
        {
            if (!File.Exists(lib.Path)) continue;
            // 展平提取（新版 LWJGL 的 natives 位于 windows/x64/... 下）
            try
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(lib.Path);
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith('/')) continue;
                    if (!entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                    entry.ExtractToFile(Path.Combine(dir, Path.GetFileName(entry.FullName)), overwrite: true);
                }
            }
            catch { }
        }
    }

    // MARK: 参数

    public static int MemoryMB(McInstance instance)
    {
        if (instance.Setup.MemoryGB is { } m && m > 0) return (int)(m * 1024);
        if (!LauncherSettings.Shared.MemoryAuto) return (int)(LauncherSettings.Shared.MemoryGB * 1024);
        var totalGB = TotalPhysicalBytes / 1024.0 / 1024 / 1024;
        double gb;
        if (instance.IsModded)
        {
            var mods = 0;
            try { mods = Directory.GetFiles(Path.Combine(instance.GameDir, "mods")).Length; } catch { }
            gb = Math.Min(Math.Min(3 + mods * 0.025, 10), totalGB * 0.5);
        }
        else
        {
            gb = Math.Min(4, Math.Max(2, totalGB * 0.25));
        }
        return (int)Math.Max(1, gb) * 1024;
    }

    private static ulong TotalPhysicalBytes
    {
        get
        {
            try
            {
                var (info, _) = MemoryStatus.Query();
                if (info != null) return info.UllTotalPhys;
            }
            catch { }
            return 8L * 1024 * 1024 * 1024;
        }
    }

    public static List<string> Split(string s)
    {
        var result = new List<string>();
        var current = "";
        char? quote = null;
        var hasToken = false;
        foreach (var c in s)
        {
            if (quote is { } q)
            {
                if (c == q) quote = null;
                else current += c;
            }
            else if (c == '"' || c == '\'')
            {
                quote = c;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (hasToken || current.Length > 0)
                {
                    result.Add(current);
                    current = "";
                    hasToken = false;
                }
            }
            else
            {
                current += c;
            }
        }
        if (hasToken || current.Length > 0) result.Add(current);
        return result;
    }

    private static List<string> Collect(IReadOnlyList<Json> list, RuleContext ctx)
    {
        var output = new List<string>();
        foreach (var item in list)
        {
            if (item.String is { } s) { output.Add(s); continue; }
            if (item["rules"].Exists && !ctx.Check(item["rules"])) continue;
            var v = item["value"];
            if (v.String is { } vs) output.Add(vs);
            else output.AddRange(v.Array.Select(a => a.String).Where(a => a != null).Select(a => a!));
        }
        return output;
    }

    /// 针对 Java 版本调优的 JVM 参数（借鉴 Lunar / Badlion 的做法）
    public static List<string> PerformanceArguments(JavaInfo java, int memoryMB, McInstance instance,
        IReadOnlyList<string> custom, string classpath = "", IReadOnlyList<string>? existing = null)
    {
        existing ??= Array.Empty<string>();
        var args = new List<string>();
        var s = LauncherSettings.Shared;
        if (!custom.Any(a => a.StartsWith("-Xmx")))
        {
            args.Add($"-Xmx{memoryMB}m");
            // 初始堆设为一半，减少启动阶段的反复扩容
            if (!custom.Any(a => a.StartsWith("-Xms"))) args.Add($"-Xms{Math.Max(512, memoryMB / 2)}m");
        }
        if (!custom.Any(a => a.Contains("GC") && a.StartsWith("-XX:+Use")))
        {
            var useZgc = s.GcMode == GCMode.Zgc && java.Major >= 21;
            if (useZgc)
            {
                args.Add("-XX:+UseZGC");
                // Java 21~22 需要显式开启分代 ZGC；23 起默认分代，24 起该参数被移除
                if (java.Major <= 22) args.Add("-XX:+ZGenerational");
            }
            else
            {
                // Mojang 官方启动器的 G1 参数，并根据堆大小调整区域大小
                args.AddRange(new[]
                {
                    "-XX:+UseG1GC", "-XX:+UnlockExperimentalVMOptions", "-XX:G1NewSizePercent=20", "-XX:G1ReservePercent=20",
                    $"-XX:MaxGCPauseMillis=50", $"-XX:G1HeapRegionSize={(memoryMB >= 6144 ? 16 : 8)}M", "-XX:+ParallelRefProcEnabled",
                });
            }
        }
        args.Add("-XX:-OmitStackTraceInFastThrow");
        // Java 24+ 默认会对 sun.misc.Unsafe 的内存访问发出警告，LWJGL 与部分 Mod 仍在使用
        if (java.Major >= 24 && !custom.Concat(existing).Any(a => a.StartsWith("--sun-misc-unsafe-memory-access")))
            args.Add("--sun-misc-unsafe-memory-access=allow");
        // Java 24+：紧凑对象头，显著降低堆内存占用（25 起为正式功能）
        if (s.CompactHeaders && java.Major >= 25) args.Add("-XX:+UseCompactObjectHeaders");
        else if (s.CompactHeaders && java.Major == 24)
            args.AddRange(new[] { "-XX:+UnlockExperimentalVMOptions", "-XX:+UseCompactObjectHeaders" });
        // Java agent（如 authlib-injector）与动态 CDS / AOT 缓存的生成不兼容
        var hasAgent = existing.Concat(custom).Any(a => a.StartsWith("-javaagent:"));
        if (s.ClassDataSharing && java.HasBaseCDS && !hasAgent)
        {
            var dir = Path.Combine(instance.Dir, ".kiln");
            try { Directory.CreateDirectory(dir); } catch { }
            if (java.Major >= 25 && !args.Contains("-XX:+UseZGC"))
            {
                // Java 25+：AOT 缓存（JEP 483/514/515）。首次运行记录类加载与方法分析，退出时写入缓存，之后的启动直接复用。
                // 缓存与 classpath、Java 版本和 JVM 参数绑定，变化后换用新文件。
                var key = Convert.ToHexString(SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(java.Path + java.Version + classpath + string.Join(" ", args))))[..12].ToLowerInvariant();
                var aot = Path.Combine(dir, $"aot-{key}.aot");
                if (File.Exists(aot))
                {
                    args.Add($"-XX:AOTCache={aot}");
                }
                else
                {
                    // 清理过期的缓存
                    try
                    {
                        foreach (var f in Directory.EnumerateFiles(dir))
                            if (Path.GetFileName(f).StartsWith("aot-"))
                                try { File.Delete(f); } catch { }
                    }
                    catch { }
                    args.Add($"-XX:AOTCacheOutput={aot}");
                }
            }
            else if (java.Major >= 19)
            {
                // Java 19~24：动态 CDS 归档，自动生成并在 classpath 变化时重建
                var jsa = Path.Combine(dir, $"cds-{java.Major}-{java.Arch}.jsa");
                args.AddRange(new[] { "-XX:+AutoCreateSharedArchive", $"-XX:SharedArchiveFile={jsa}" });
            }
        }
        return args;
    }

    public static List<string> BuildArguments(McInstance instance, JavaInfo java, LoginResult login,
        IReadOnlyList<McLibrary> libs, RuleContext ctx, LaunchOptions options)
    {
        var json = instance.Json;
        var s = LauncherSettings.Shared;
        var setup = instance.Setup;
        var folder = instance.Folder;
        var assets = Path.Combine(folder, "assets");
        var jvm = new List<string>();

        if (json["arguments"]["jvm"].Exists)
        {
            jvm.AddRange(Collect(json["arguments"]["jvm"].Array, ctx));
        }
        else
        {
            jvm.AddRange(new[] { "-Djava.library.path=${natives_directory}", "-cp", "${classpath}" });
        }
        var custom = Split(string.IsNullOrEmpty(setup.JvmArgs) ? s.JvmArgs : setup.JvmArgs);
        var classpathKey = string.Join(";", libs.Select(l => l.Path));
        jvm.AddRange(PerformanceArguments(java, MemoryMB(instance), instance, custom, classpathKey, jvm.Concat(login.JvmArgs).ToList()));
        jvm.AddRange(new[]
        {
            "-Dfml.ignoreInvalidMinecraftCertificates=true", "-Dfml.ignorePatchDiscrepancies=true",
            "-Dlog4j2.formatMsgNoLookups=true", "-Dorg.lwjgl.librarypath=${natives_directory}",
        });
        if (json["logging"]["client"]["argument"].String is { } logArg
            && json["logging"]["client"]["file"]["id"].String is { } logId)
        {
            var path = Path.Combine(assets, "log_configs", logId);
            if (File.Exists(path)) jvm.Add(logArg.Replace("${path}", path));
        }
        if (java.Major == 8 && instance.Loaders.Contains("Forge") && LegacyJarSecurity() is { } security)
        {
            jvm.Add($"-Djava.security.properties={security}");
        }
        // 外置登录 / 离线皮肤：authlib-injector
        jvm.AddRange(login.JvmArgs);
        jvm.AddRange(custom);
        if (json["mainClass"].String is not { } mainClass)
            throw new LauncherException(T.Invoke("版本 JSON 中缺少 mainClass", "mainClass is missing from the version JSON"));

        var game = new List<string>();
        if (json["minecraftArguments"].String is { Length: > 0 } old)
        {
            game.AddRange(old.Split(' '));
            if (s.WindowModeSetting != WindowMode.Fullscreen && !game.Contains("--width"))
                game.AddRange(new[] { "--width", "${resolution_width}", "--height", "${resolution_height}" });
        }
        if (json["arguments"]["game"].Exists)
        {
            game.AddRange(Collect(json["arguments"]["game"].Array, ctx));
        }
        // 自定义信息：为空时移除 --versionType
        var info = string.IsNullOrEmpty(setup.CustomInfo) ? s.CustomInfo : setup.CustomInfo;
        if (info.Length == 0)
        {
            var i = game.IndexOf("--versionType");
            if (i >= 0)
            {
                game.RemoveAt(i);
                if (i < game.Count) game.RemoveAt(i);
            }
        }
        if (s.WindowModeSetting == WindowMode.Fullscreen) game.Add("--fullscreen");
        if (options.Demo && !game.Contains("--demo")) game.Add("--demo");
        var server = options.World != null ? "" : options.ServerIp ?? setup.ServerEnter ?? "";
        if (options.World is { } world && instance.SupportsQuickPlay)
            game.AddRange(new[] { "--quickPlaySingleplayer", world });
        if (server.Length > 0)
        {
            if (instance.SupportsQuickPlay)
            {
                game.AddRange(new[] { "--quickPlayMultiplayer", server });
            }
            else
            {
                var parts = server.Split(':');
                game.AddRange(new[] { "--server", parts[0], "--port", parts.Length > 1 ? parts[1] : "25565" });
            }
        }
        game.AddRange(Split(string.IsNullOrEmpty(setup.GameArgs) ? s.GameArgs : setup.GameArgs));

        var cp = libs.Where(l => !l.IsNatives && File.Exists(l.Path)).Select(l => l.Path).ToList();
        var jar = instance.ClientJar;
        if (File.Exists(jar)) cp.Add(jar);
        (int Width, int Height) size = (854, 480);
        if (s.WindowModeSetting == WindowMode.Custom) size = (Math.Max(100, s.WindowWidth), Math.Max(100, s.WindowHeight));

        var replace = new Dictionary<string, string>
        {
            ["${classpath_separator}"] = ";",
            ["${natives_directory}"] = instance.NativesDir,
            ["${library_directory}"] = Path.Combine(folder, "libraries"),
            ["${libraries_directory}"] = Path.Combine(folder, "libraries"),
            ["${launcher_name}"] = AppInfo.ShortName,
            ["${launcher_version}"] = AppInfo.Version,
            ["${version_name}"] = instance.Name,
            ["${game_directory}"] = instance.GameDir,
            ["${assets_root}"] = assets,
            ["${game_assets}"] = Path.Combine(assets, "virtual", instance.AssetIndexId),
            ["${assets_index_name}"] = instance.AssetIndexId,
            ["${user_properties}"] = "{}",
            ["${auth_player_name}"] = login.Name,
            ["${auth_uuid}"] = login.Uuid,
            ["${auth_access_token}"] = login.AccessToken,
            ["${access_token}"] = login.AccessToken,
            ["${auth_session}"] = login.AccessToken,
            ["${auth_xuid}"] = login.Xuid,
            ["${clientid}"] = "",
            ["${user_type}"] = login.UserType,
            ["${version_type}"] = info.Length == 0 ? AppInfo.ShortName : info,
            ["${resolution_width}"] = size.Width.ToString(),
            ["${resolution_height}"] = size.Height.ToString(),
            ["${primary_jar}"] = jar,
            ["${classpath}"] = string.Join(";", cp),
        };
        string Apply(string a)
        {
            if (!a.Contains("${")) return a;
            var r = a;
            foreach (var (k, v) in replace)
                if (r.Contains(k)) r = r.Replace(k, v);
            return r;
        }
        var result = jvm.Select(Apply).ToList();
        result.Add(mainClass);
        result.AddRange(game.Select(Apply));
        return result;
    }

    /// 新版 Java 8（8u351 起）不再承认 SHA-1 签名的 jar，而 1.7.10 ~ 1.12.2 时期的 Mod 大多用 SHA-1 签名。
    /// Forge 会因此报告签名不符，Reika 系列（DragonAPI）等检查自身签名的 Mod 就会拒绝加载。
    /// 只针对 jar 签名恢复当时的规则（TLS 等其他规则不变），并且只用于 Java 8 上的 Forge。
    private static string? LegacyJarSecurity()
    {
        var url = Path.Combine(Paths.Support, "legacy-jar.security");
        var text = "# Kiln：旧版 Forge Mod 的 jar 签名（SHA-1）\njdk.jar.disabledAlgorithms=MD2, MD5, RSA keySize < 1024, DSA keySize < 1024, include jdk.disabled.namedCurves\n";
        try
        {
            if (File.ReadAllText(url) != text) File.WriteAllText(url, text);
        }
        catch
        {
            try { File.WriteAllText(url, text); } catch { return null; }
        }
        return url;
    }

    public static string Redact(string s, LoginResult login) =>
        login.UserType == "legacy" ? s : s.Replace(login.AccessToken, "********");

    public static void ExportScript(JavaInfo java, IReadOnlyList<string> args, string cwd, string url)
    {
        static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        var text = "@echo off\r\nrem Generated by " + AppInfo.ShortName + "\r\ncd /d "
                   + Q(cwd) + "\r\n" + Q(java.Path) + " " + string.Join(" ", args.Select(Q)) + "\r\n";
        File.WriteAllText(url, text, System.Text.Encoding.UTF8);
    }

    // MARK: 运行

    public static GameProcess Run(McInstance instance, JavaInfo java, IReadOnlyList<string> args, LoginResult login,
        LaunchCallbacks callbacks)
    {
        var game = GameProcessFactory(instance, java, args, login, callbacks);
        return game;
    }

    private static GameProcess GameProcessFactory(McInstance instance, JavaInfo java, IReadOnlyList<string> args,
        LoginResult login, LaunchCallbacks callbacks)
    {
        var game = new GameProcess(instance.Name);
        var info = new ProcessStartInfo
        {
            FileName = java.Path,
            Arguments = string.Join(" ", args.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a)),
            WorkingDirectory = instance.GameDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try { Directory.CreateDirectory(instance.GameDir); } catch { }
        var logUrl = Path.Combine(Paths.Logs, "LatestLaunch.log");
        try { File.WriteAllText(logUrl, ""); } catch { }
        game.LogFilePath = logUrl;
        var redactToken = login.UserType == "legacy" ? null : login.AccessToken;

        var p = new Process { StartInfo = info, EnableRaisingEvents = true };
        var logWriter = TryOpenWrite(logUrl);
        void HandleText(string text)
        {
            if (redactToken != null) text = text.Replace(redactToken, "********");
            try { logWriter?.Write(System.Text.Encoding.UTF8.GetBytes(text)); logWriter?.Flush(); } catch { }
            game.Append(text);
            if (game.State == GameProcess.GameState.Starting && WindowAppeared(text))
            {
                game.State = GameProcess.GameState.Running;
                callbacks.OnWindow(game);
            }
        }
        p.OutputDataReceived += (_, e) => { if (e.Data != null) HandleText(e.Data + "\n"); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) HandleText(e.Data + "\n"); };
        p.Start();
        // 高优先级：对应原版的高 QoS 调度
        try { p.PriorityClass = ProcessPriorityClass.High; } catch { }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        game.Process = p;
        p.Exited += (_, _) =>
        {
            try { p.WaitForExit(); } catch { }
            try { logWriter?.Dispose(); } catch { }
            var code = p.ExitCode;
            game.ExitCode = code;
            game.EndedAt = DateTime.UtcNow;
            game.State = code == 0 || game.Killed ? GameProcess.GameState.Exited : GameProcess.GameState.Crashed;
            Log.log($"[Launch] 游戏已退出，返回值 {code}");
            AddPlayTime(game, instance);
            callbacks.OnExit(game);
        };
        // 部分版本没有可识别的日志，20 秒后视为已启动
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(20));
            if (game.State == GameProcess.GameState.Starting)
            {
                game.State = GameProcess.GameState.Running;
                callbacks.OnWindow(game);
            }
        });
        return game;
    }

    private static FileStream? TryOpenWrite(string path)
    {
        try { return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read); }
        catch { return null; }
    }

    public static bool WindowAppeared(string text) =>
        new[] { "Setting user:", "LWJGL Version", "Backend library:", "OpenAL initialized", "Sound engine started", "Narrator library" }
            .Any(t => text.Contains(t));
}

/// 将 log4j 的 XML 日志（版本 JSON 中 logging 配置的格式）转换为普通文本行
public sealed class Log4jParser
{
    private string _buffer = "";

    public List<string> Feed(string text)
    {
        _buffer += text;
        var output = new List<string>();
        while (true)
        {
            var start = _buffer.IndexOf("<log4j:Event", StringComparison.Ordinal);
            if (start >= 0)
            {
                // 事件之前的普通文本
                output.AddRange(PlainLines(_buffer[..start]));
                var end = _buffer.IndexOf("</log4j:Event>", start, StringComparison.Ordinal);
                if (end < 0)
                {
                    _buffer = _buffer[start..];
                    return output;
                }
                output.Add(Format(_buffer[start..(end + "</log4j:Event>".Length)]));
                _buffer = _buffer[(end + "</log4j:Event>".Length)..];
            }
            else
            {
                // 没有 XML 事件：保留最后不完整的一行
                var lastNewline = _buffer.LastIndexOf('\n');
                if (lastNewline < 0)
                {
                    if (_buffer.Length > 8192)
                    {
                        output.AddRange(PlainLines(_buffer));
                        _buffer = "";
                    }
                    return output;
                }
                output.AddRange(PlainLines(_buffer[..(lastNewline + 1)]));
                _buffer = _buffer[(lastNewline + 1)..];
                return output;
            }
        }
    }

    private static IEnumerable<string> PlainLines(string s) =>
        s.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);

    private static string? Attr(string xml, string name)
    {
        var marker = name + "=\"";
        var i = xml.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return null;
        var rest = xml[(i + marker.Length)..];
        var end = rest.IndexOf('"');
        return end < 0 ? null : rest[..end];
    }

    private static string Format(string xml)
    {
        var level = Attr(xml, "level") ?? "INFO";
        var thread = Attr(xml, "thread") ?? "main";
        var time = "";
        if (Attr(xml, "timestamp") is { } ts && double.TryParse(ts, System.Globalization.CultureInfo.InvariantCulture, out var ms))
            time = DateTime.UnixEpoch.AddMilliseconds(ms).ToLocalTime().ToString("HH:mm:ss");
        var message = "";
        var a = xml.IndexOf("<![CDATA[", StringComparison.Ordinal);
        if (a >= 0)
        {
            var b = xml.IndexOf("]]>", a, StringComparison.Ordinal);
            if (b >= 0) message = xml[(a + "<![CDATA[".Length)..b];
        }
        var ta = xml.IndexOf("<log4j:Throwable><![CDATA[", StringComparison.Ordinal);
        if (ta >= 0)
        {
            var tb = xml.IndexOf("]]>", ta, StringComparison.Ordinal);
            if (tb >= 0) message += "\n" + xml[(ta + "<log4j:Throwable><![CDATA[".Length)..tb];
        }
        return $"[{time}] [{thread}/{level}]: {message}";
    }
}

internal static class MemoryStatus
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal class MEMORYSTATUSEX
    {
        public uint DwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>();
        public uint DwMemoryLoad;
        public ulong UllTotalPhys;
        public ulong UllAvailPhys;
        public ulong UllTotalPageFile;
        public ulong UllAvailPageFile;
        public ulong UllTotalVirtual;
        public ulong UllAvailVirtual;
        public ulong UllAvailExtendedVirtual;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([System.Runtime.InteropServices.In] [System.Runtime.InteropServices.Out] MEMORYSTATUSEX lpBuffer);

    public static (MEMORYSTATUSEX? Info, uint Load) Query()
    {
        var s = new MEMORYSTATUSEX();
        return GlobalMemoryStatusEx(s) ? (s, s.DwMemoryLoad) : (null, 0);
    }
}
