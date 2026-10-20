using System.Diagnostics;
using Microsoft.Win32;

namespace Kiln.Core;

/// PCL2（Plain Craft Launcher 2）配置接管：
/// 自动找到本机 PCL2 的位置，解析其 Setup.ini（全局 + 各版本独立设置），
/// 把游戏目录、内存、Java、隔离、窗口、启动参数等迁移到 Kiln。
/// 不移动、不修改 PCL2 的任何文件；两个启动器可以共用同一游戏目录。
public static class Pcl2
{
    public sealed record Detected(
        string Root,            // PCL2 程序所在目录
        string GameFolder,      // PCL2 当前使用的游戏目录
        string? ExePath,        // 找到的 PCL2 exe（可能为 null，例如只找到 PCL 配置文件夹）
        IReadOnlyList<string> VersionNames);

    public sealed record TakeOverResult(
        string GameFolder,
        int VersionCount,
        IReadOnlyList<string> Applied,    // 已迁移的全局设置
        IReadOnlyList<string> Skipped);   // 未迁移项及原因

    /// 启动器可见性：已检测到您当前环境有PCL2，是否继承其配置？
    public static string DialogText(Detected d) => T.Invoke(
        $"已检测到您当前环境有PCL2（{d.VersionNames.Count} 个版本，{d.GameFolder}），是否继承其配置？",
        $"PCL2 has been detected on your computer ({d.VersionNames.Count} versions at {d.GameFolder}). Inherit its settings?");

    // MARK: 自动检测

    /// 自动检测本机 PCL2；未安装或找不到时返回 null
    public static Detected? Detect(int maxSeconds = 20)
    {
        var roots = FindRoots(maxSeconds);
        foreach (var (root, exe) in roots)
        {
            var d = FromRoot(root, exe);
            if (d != null) return d;
        }
        return null;
    }

    /// 从已知目录解析 PCL2（目录需包含 PCL\Setup.ini 或 .minecraft）
    public static Detected? FromRoot(string root, string? exe = null)
    {
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var hasSetup = File.Exists(Path.Combine(root, "PCL", "Setup.ini"));
            var game = ResolveGameFolder(root);
            if (!hasSetup && !Directory.Exists(Path.Combine(root, ".minecraft"))) return null;
            var versions = new List<string>();
            try
            {
                versions = Directory.GetDirectories(Path.Combine(game, "versions"))
                    .Select(d => Path.GetFileName(d) ?? "")
                    .Where(n => n.Length > 0 && !n.StartsWith('.'))
                    .ToList();
            }
            catch { }
            return new Detected(root, game, exe, versions);
        }
        catch { return null; }
    }

    /// 解析 PCL2 的游戏目录：Setup.ini 的 LaunchFolderSelect（$ 开头 = 相对启动器目录）
    public static string ResolveGameFolder(string root)
    {
        var ini = ReadIni(Path.Combine(root, "PCL", "Setup.ini"));
        var select = ini.GetValueOrDefault("LaunchFolderSelect");
        if (!string.IsNullOrEmpty(select)) return ExpandPath(select, root);
        return Path.Combine(root, ".minecraft");
    }

    /// PCL2 路径值：`$xxx` 表示相对启动器目录
    public static string ExpandPath(string value, string root)
    {
        value = value.Trim();
        if (value.StartsWith('$'))
            return Path.GetFullPath(Path.Combine(root, value[1..].TrimStart('\\', '/')));
        return Path.GetFullPath(value);
    }

    // MARK: 检测来源（按优先级）

    private static List<(string Root, string? Exe)> FindRoots(int maxSeconds)
    {
        var result = new List<(string, string?)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? dir, string? exe)
        {
            if (dir == null) return;
            try { dir = Path.GetFullPath(dir); } catch { return; }
            if (!Directory.Exists(dir)) return;
            if (!seen.Add(dir)) return;
            result.Add((dir, exe));
        }

        // 1. 正在运行的 PCL2 进程
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var name = p.ProcessName;
                if (!name.Contains("Plain Craft Launcher", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("PCL", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("PCL2", StringComparison.OrdinalIgnoreCase)) continue;
                Add(Path.GetDirectoryName(p.MainModule?.FileName), p.MainModule?.FileName);
            }
            catch { }
        }

        // 2. 常见目录浅层扫描（桌面含 OneDrive 桌面、下载、文档、各盘根目录、Program Files 等）
        var bases = new List<string>();
        foreach (var special in new[]
                 {
                     Environment.SpecialFolder.Desktop, Environment.SpecialFolder.MyDocuments,
                     Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData,
                 })
        {
            try { bases.Add(Environment.GetFolderPath(special)); } catch { }
        }
        // OneDrive 桌面（真实检测中 PCL2 常放这里）
        var oneDesktop = Environment.GetEnvironmentVariable("OneDrive");
        if (oneDesktop != null) bases.Add(oneDesktop);
        try { bases.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")); } catch { }
        try
        {
            bases.AddRange(DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => d.RootDirectory.FullName));
        }
        catch { }

        var deadline = Stopwatch.StartNew();
        foreach (var b in bases.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (deadline.Elapsed.TotalSeconds > maxSeconds) break;
            try
            {
                // 深度 2：目录本身 / 子目录 / 孙目录
                if (ScanForPcl(b, out var exe1) is { } hit1) Add(hit1, exe1);
                foreach (var sub in Directory.EnumerateDirectories(b).Take(200))
                {
                    if (IsSystemDir(sub)) continue;
                    if (ScanForPcl(sub, out var exe2) is { } hit2) { Add(hit2, exe2); continue; }
                    if (deadline.Elapsed.TotalSeconds > maxSeconds) break;
                    foreach (var sub2 in Directory.EnumerateDirectories(sub).Take(100))
                    {
                        if (IsSystemDir(sub2)) continue;
                        if (ScanForPcl(sub2, out var exe3) is { } hit3) Add(hit3, exe3);
                        if (deadline.Elapsed.TotalSeconds > maxSeconds) break;
                    }
                }
            }
            catch { }
        }

        // 3. MUICache：资源管理器打开过的 exe 路径
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Muicache");
            if (key != null)
                foreach (var name in key.GetValueNames())
                {
                    if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!LooksLikePclExe(Path.GetFileName(name))) continue;
                    Add(Path.GetDirectoryName(name), name);
                }
        }
        catch { }

        // 4. 桌面与「开始」菜单快捷方式
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
            dynamic shell = Activator.CreateInstance(shellType)!;
            var linkDirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            };
            foreach (var dir in linkDirs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories).Take(200))
                {
                    try
                    {
                        if (!LooksLikePclExe(Path.GetFileNameWithoutExtension(lnk))) continue;
                        var target = (string)shell.CreateShortcut(lnk).TargetPath;
                        if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            Add(Path.GetDirectoryName(target), target);
                    }
                    catch { }
                }
            }
        }
        catch { }

        // 5. PCL2 注册表（HKCU\Software\PCL）里缓存的部分路径类设置不含安装位置，仅作兜底验证
        return result;
    }

    private static bool IsSystemDir(string path)
    {
        var n = Path.GetFileName(path);
        return n.Equals("Windows", StringComparison.OrdinalIgnoreCase)
            || n.Equals("Program Files", StringComparison.OrdinalIgnoreCase)
            || n.Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase)
            || n.Equals("ProgramData", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith('$');
    }

    private static bool LooksLikePclExe(string fileName) =>
        fileName.StartsWith("Plain Craft Launcher", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("PCL.exe", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("PCL2.exe", StringComparison.OrdinalIgnoreCase);

    /// 在某目录内找 PCL2（exe 或 PCL\Setup.ini）
    private static string? ScanForPcl(string dir, out string? exe)
    {
        exe = null;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.exe"))
                if (LooksLikePclExe(Path.GetFileName(file)))
                {
                    exe = file;
                    return dir;
                }
            if (File.Exists(Path.Combine(dir, "PCL", "Setup.ini"))) return dir;
        }
        catch { }
        return null;
    }

    // MARK: INI 解析（PCL2 格式：每行 Key:Value）

    public static Dictionary<string, string> ReadIni(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(path)) return result;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                var i = line.IndexOf(':');
                if (i <= 0) continue;
                result[line[..i].Trim()] = line[(i + 1)..].Trim();
            }
        }
        catch { }
        return result;
    }

    private static Dictionary<string, string> ReadRegistrySettings()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\PCL");
            if (key != null)
                foreach (var name in key.GetValueNames())
                    result[name] = key.GetValue(name)?.ToString() ?? "";
        }
        catch { }
        return result;
    }

    // MARK: 接管

    /// 继承 PCL2 的配置。不移动文件；PCL2 侧不受任何影响。
    public static TakeOverResult TakeOver(Detected pcl)
    {
        var applied = new List<string>();
        var skipped = new List<string>();
        var s = LauncherSettings.Shared;
        var ini = ReadIni(Path.Combine(pcl.Root, "PCL", "Setup.ini"));
        var reg = ReadRegistrySettings();
        string? Ini(string key) => ini.TryGetValue(key, out var v) ? v : null;
        string? Reg(string key) => reg.TryGetValue(key, out var v) ? v : null;

        // 游戏目录与额外目录
        s.MinecraftFolder = pcl.GameFolder;
        applied.Add($"minecraftFolder = {pcl.GameFolder}");
        var folders = Reg("LaunchFolders");
        if (!string.IsNullOrEmpty(folders))
        {
            var extra = folders.Split('|', ',')
                .Select(f => ExpandPath(f.Trim(), pcl.Root))
                .Where(Directory.Exists).ToList();
            if (extra.Count > 0)
            {
                s.ExtraFolders = extra;
                applied.Add($"extraFolders = {extra.Count} 个");
            }
        }

        // 内存：LaunchRamType 0=自动 1=自定义（GB）
        if (Ini("LaunchRamType") == "1")
        {
            s.MemoryAuto = false;
            if (double.TryParse(Ini("LaunchRamCustom"), System.Globalization.CultureInfo.InvariantCulture, out var gb))
                s.MemoryGB = gb;
            applied.Add($"memory = {s.MemoryGB:0.#} GB（自定义）");
        }
        else
        {
            applied.Add("memory = 自动");
        }

        // Java：LaunchArgumentJavaSelect（空/“自动”=自动选择）；LaunchArgumentJavaAll 为自定义 Java 列表
        var javaSel = Ini("LaunchArgumentJavaSelect") ?? Reg("LaunchArgumentJavaSelect");
        if (!string.IsNullOrEmpty(javaSel) && javaSel != "自动" && File.Exists(javaSel))
        {
            s.JavaPath = javaSel;
            applied.Add($"java = {javaSel}");
        }
        try
        {
            if (Reg("LaunchArgumentJavaAll") is { Length: > 2 } json)
            {
                var arr = Json.Parse(json).Array;
                var javas = arr.Select(j => j["PathJava"].String ?? j["Path"].String ?? j.String)
                    .Where(p => p != null && File.Exists(p!)).Select(p => p!).ToList();
                if (javas.Count > 0)
                {
                    s.CustomJavas = javas;
                    applied.Add($"customJavas = {javas.Count} 个");
                }
            }
        }
        catch { }

        // 隔离：LaunchArgumentIndieV2 0=关闭 1=隔离可装Mod版本 2=隔离非正式版 3=隔离所有 4=智能
        var indie = Ini("LaunchArgumentIndieV2");
        if (int.TryParse(indie, out var indieV))
        {
            s.Isolation = indieV switch
            {
                0 => IsolationMode.None,
                3 => IsolationMode.All,
                _ => IsolationMode.ModdedAndPreview,   // 1、2、4（智能）均映射到 Kiln 的“隔离 Mod 版与快照”
            };
            applied.Add($"isolation = {s.Isolation}");
        }

        // 窗口：WindowType 0=全屏 1=默认 2=与启动器一致 3=自定义 4=最大化
        if (int.TryParse(Ini("LaunchArgumentWindowType"), out var win))
        {
            s.WindowModeSetting = win switch { 0 => WindowMode.Fullscreen, 3 => WindowMode.Custom, _ => WindowMode.DefaultSize };
            if (win == 3)
            {
                if (int.TryParse(Ini("LaunchArgumentWindowWidth"), out var w)) s.WindowWidth = w;
                if (int.TryParse(Ini("LaunchArgumentWindowHeight"), out var h)) s.WindowHeight = h;
            }
            applied.Add($"window = {s.WindowModeSetting}" + (win == 3 ? $" ({s.WindowWidth}×{s.WindowHeight})" : ""));
        }

        // 启动器可见性：0=启动后关闭 1/2/3=隐藏 4=最小化 5=保持
        if (int.TryParse(Reg("LaunchArgumentVisible") ?? Ini("LaunchArgumentVisible"), out var vis))
        {
            s.Visibility = vis switch
            {
                0 => VisibilityMode.CloseOnStart,
                1 => VisibilityMode.HideAndClose,
                2 or 3 => VisibilityMode.HideAndReopen,
                4 => VisibilityMode.Minimize,
                _ => VisibilityMode.Keep,
            };
            applied.Add($"visibility = {s.Visibility}");
        }

        // GC：LaunchAdvanceGC 0/1=自动(ZGC 优先) 2=仅G1 3=自定义 4=优化G1
        if (int.TryParse(Ini("LaunchAdvanceGC"), out var gc) && (gc == 0 || gc == 1))
        {
            s.GcMode = GCMode.Zgc;
            applied.Add("gc = ZGC（自动）");
        }
        else
        {
            s.GcMode = GCMode.G1;
            applied.Add("gc = G1");
        }

        // 自定义参数（PCL2 的默认 JVM 参数为启动器自身的兼容性参数，不迁移；仅迁移用户加的）
        const string pclDefaultJvm =
            "-XX:-OmitStackTraceInFastThrow -Djdk.lang.Process.allowAmbiguousCommands=True -Dfml.ignoreInvalidMinecraftCertificates=True -Dfml.ignorePatchDiscrepancies=True";
        var jvm = Ini("LaunchAdvanceJvm");
        if (!string.IsNullOrEmpty(jvm) && jvm != pclDefaultJvm)
        {
            s.JvmArgs = jvm;
            applied.Add("jvmArgs（自定义）");
        }
        var gameArgs = Ini("LaunchAdvanceGame");
        if (!string.IsNullOrEmpty(gameArgs))
        {
            s.GameArgs = gameArgs;
            applied.Add("gameArgs（自定义）");
        }

        // 下载线程与下载源
        if (int.TryParse(Reg("ToolDownloadThread") ?? Ini("ToolDownloadThread"), out var threads))
        {
            s.DownloadThreads = threads;
            applied.Add($"downloadThreads = {threads}");
        }
        var source = Reg("ToolDownloadSource");
        if (source == "2")
        {
            s.VersionListSource = DownloadSource.MirrorFirst;
            s.FileSource = DownloadSource.MirrorFirst;
            applied.Add("downloadSource = 镜像优先");
        }

        // 自定义信息（版本类型后缀）
        var info = Ini("LaunchArgumentInfo");
        if (!string.IsNullOrEmpty(info) && info != "PCL")
        {
            s.CustomInfo = info;
            applied.Add($"customInfo = {info}");
        }

        // 各版本独立设置（versions\<名称>\PCL\Setup.ini → .kiln\setup.json）
        var migrated = 0;
        foreach (var name in pcl.VersionNames)
        {
            var dir = Path.Combine(pcl.GameFolder, "versions", name);
            var vIni = ReadIni(Path.Combine(dir, "PCL", "Setup.ini"));
            if (vIni.Count == 0) continue;
            var setup = s.InstanceSetupOf(dir);
            var changed = false;
            if (vIni.TryGetValue("VersionArgumentIndieV2", out var indieStr)
                && bool.TryParse(indieStr, out var isolated))
            {
                setup.Isolation = isolated;
                changed = true;
            }
            if (vIni.TryGetValue("VersionRamType", out var ramStr) && int.TryParse(ramStr, out var ramType))
            {
                if (ramType == 1 && vIni.TryGetValue("VersionRamCustom", out var customStr)
                    && double.TryParse(customStr, System.Globalization.CultureInfo.InvariantCulture, out var gb))
                {
                    setup.MemoryGB = gb;
                    changed = true;
                }
                // 0=该版本自动 → 跟随全局（不写）
            }
            if (vIni.TryGetValue("VersionArgumentJavaSelect", out var vJava)
                && vJava != "使用全局设置" && vJava != "自动" && vJava.Length > 0 && File.Exists(vJava))
            {
                setup.JavaPath = vJava;
                changed = true;
            }
            if (vIni.TryGetValue("VersionAdvanceJvm", out var vJvm) && vJvm.Length > 0)
            {
                setup.JvmArgs = vJvm;
                changed = true;
            }
            if (vIni.TryGetValue("VersionAdvanceGame", out var vGame) && vGame.Length > 0)
            {
                setup.GameArgs = vGame;
                changed = true;
            }
            if (vIni.TryGetValue("VersionServerEnter", out var server) && server.Length > 0)
            {
                setup.ServerEnter = server;
                changed = true;
            }
            if (vIni.TryGetValue("VersionArgumentInfo", out var vInfo) && vInfo.Length > 0)
            {
                setup.CustomInfo = vInfo;
                changed = true;
            }
            if (changed)
            {
                s.SaveInstanceSetup(setup, dir);
                migrated++;
            }
        }
        if (migrated > 0) applied.Add($"版本独立设置 = {migrated} 个版本");
        if (pcl.VersionNames.Count == 0) skipped.Add("PCL2 中没有版本");

        skipped.Add("账号不迁移（PCL2 加密存储）；首次启动请在 Kiln 中重新登录");
        skipped.Add("PCL2 主题、背景、音乐等界面设置不适用于 Kiln，未迁移");

        // 标记已完成，弹窗只出现一次
        s.Pcl2TakenOver = true;
        s.Pcl2Root = pcl.Root;

        return new TakeOverResult(pcl.GameFolder, pcl.VersionNames.Count, applied, skipped);
    }
}
