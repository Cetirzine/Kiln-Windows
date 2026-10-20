using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Kiln.Core;

/// 诊断包：把游戏日志、崩溃报告、启动器日志、Mod 列表和系统信息打包成 zip，方便发给别人帮忙看。
/// 登录令牌会被替换为 ********，用户目录替换为 ~。
public static class Diagnostics
{
    /// 单个文件最多保留的大小（取末尾）
    private const int MaxFileSize = 4 * 1024 * 1024;

    /// 导出诊断包。game 为空时只收集游戏目录中的日志
    public static async Task Export(McInstance instance, GameProcess? game, string output)
    {
        var info = Summary(instance, game);
        var secret = game?.Secret;
        var since = game?.StartedAt ?? DateTime.UtcNow.AddDays(-1);
        var outputLog = game?.LogFilePath;
        var lines = game?.Lines ?? Array.Empty<string>();
        var gameDir = instance.GameDir;
        await Task.Run(() =>
        {
            var root = Path.Combine(Paths.Cache, "diagnostics-" + Guid.NewGuid().ToString("N"));
            var dir = Path.Combine(root, Path.GetFileNameWithoutExtension(output));
            Directory.CreateDirectory(dir);
            try
            {
                void Write(string text, string name) =>
                    File.WriteAllText(Path.Combine(dir, name), Scrub(text, secret));
                void Copy(string src, string name)
                {
                    try
                    {
                        if (!File.Exists(src)) return;
                        using var fs = File.OpenRead(src);
                        var take = (int)Math.Min(fs.Length, MaxFileSize);
                        fs.Seek(fs.Length - take, SeekOrigin.Begin);
                        using var ms = new MemoryStream();
                        fs.CopyTo(ms);
                        Write(System.Text.Encoding.UTF8.GetString(ms.ToArray()), name);
                    }
                    catch { }
                }

                Write(info, "info.txt");
                Copy(Log.LogFile, "launcher.log");
                // 游戏输出：优先使用本次启动保存的完整输出
                if (outputLog != null && File.Exists(outputLog)) Copy(outputLog, "game-output.log");
                else if (lines.Count > 0) Write(string.Join("\n", lines), "game-output.log");
                Copy(Path.Combine(gameDir, "logs", "latest.log"), "latest.log");
                var report = Newest(Path.Combine(gameDir, "crash-reports"), since, f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
                if (report != null) Copy(report, "crash-report-" + Path.GetFileName(report));
                // JVM 自身崩溃时的报告
                var hs = Newest(gameDir, since, f => Path.GetFileName(f).StartsWith("hs_err_pid"));
                if (hs != null) Copy(hs, Path.GetFileName(hs));

                try { File.Delete(output); } catch { }
                using (var zip = ZipFile.Open(output, ZipArchiveMode.Create))
                    foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                        zip.CreateEntryFromFile(f, Path.GetFileName(f));
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        });
    }

    /// 诊断信息正文
    public static string Summary(McInstance instance, GameProcess? game)
    {
        var s = new List<string>
        {
            $"{AppInfo.ShortName} {AppInfo.Version} — " + T.Invoke("诊断信息", "Diagnostics"),
            T.Invoke("生成时间：", "Generated: ") + DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ssZ"),
            "",
            $"Windows {RuleContext.OsVersion} · {CpuName} · {ArchInfo.Native} · "
                + T.Invoke("内存", "Memory") + $" {(TotalPhysicalGB)} GB",
            T.Invoke("版本：", "Instance: ") + instance.Name
                + T.Invoke($"（{instance.Description}）", $" ({instance.Description})"),
            T.Invoke("游戏目录：", "Game directory: ") + instance.GameDir
                + (instance.Isolated ? T.Invoke("（独立）", " (separate)") : ""),
            T.Invoke("分配内存：", "Memory allocated: ") + $"{Launcher.MemoryMB(instance)} MB",
        };
        if (game?.Java is { } j) s.Add($"Java: {j.Path} ({j.Version}, {j.Arch})");
        if (game != null)
        {
            var seconds = (int)((game.EndedAt ?? DateTime.UtcNow) - game.StartedAt).TotalSeconds;
            s.Add(T.Invoke("运行时长：", "Ran for: ") + $"{seconds} s"
                + (game.ExitCode is { } c ? T.Invoke("，返回值 ", ", exit code ") + c : ""));
            if (CrashAnalyzer.Analyze(game.Lines, instance.GameDir) is { } f)
                s.Add(T.Invoke("可能原因：", "Likely cause: ") + f.Reason
                    + (f.Detail != null ? T.Invoke($"（{f.Detail}）", $" ({f.Detail})") : ""));
            if (game.CommandLine is { } c2) s.AddRange(new[] { "", T.Invoke("启动参数：", "Launch arguments:"), c2 });
        }
        foreach (var (title, sub) in new[]
                 {
                     (T.Invoke("Mod", "Mods"), "mods"),
                     (T.Invoke("资源包", "Resource packs"), "resourcepacks"),
                     (T.Invoke("光影", "Shaders"), "shaderpacks"),
                 })
        {
            List<string> files;
            try
            {
                files = Directory.EnumerateFileSystemEntries(Path.Combine(instance.GameDir, sub))
                    .Select(Path.GetFileName)
                    .Where(n => n != null && !n!.StartsWith('.'))
                    .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                    .ToList()!;
            }
            catch { continue; }
            if (files.Count == 0) continue;
            s.Add("");
            s.Add(T.Invoke($"{title}（{files.Count}）：", $"{title} ({files.Count}):"));
            s.AddRange(files.Select(f => "  " + f));
        }
        return string.Join("\n", s) + "\n";
    }

    /// 隐藏令牌与用户目录
    public static string Scrub(string text, string? secret)
    {
        var t = text;
        if (secret is { Length: >= 8 }) t = t.Replace(secret, "********");
        t = Regex.Replace(t, @"(--accessToken\s+)\S+", "$1********");
        t = Regex.Replace(t, @"(Session ID is token:)[^)\s]+", "$1********");
        t = Regex.Replace(t, @"()eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]+", "$1********");
        return t.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~");
    }

    private static string? Newest(string dir, DateTime since, Func<string, bool> match)
    {
        try
        {
            if (!Directory.Exists(dir)) return null;
            var latest = Directory.EnumerateFiles(dir).Where(match)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (latest == null || File.GetLastWriteTimeUtc(latest) < since.AddSeconds(-60)) return null;
            return latest;
        }
        catch { return null; }
    }

    /// 处理器型号，如 “Intel Core i7-13700K”
    public static string CpuName
    {
        get
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine
                    .OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                return key?.GetValue("ProcessorNameString") as string ?? "?";
            }
            catch { return "?"; }
        }
    }

    public static long TotalPhysicalGB
    {
        get
        {
            var (info, _) = MemoryStatus.Query();
            return (long)(info?.UllTotalPhys ?? 0) / 1024 / 1024 / 1024;
        }
    }
}
