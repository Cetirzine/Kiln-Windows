using System.Diagnostics;
using System.Text;

namespace Kiln.Core;

/// 启动器自动更新：从 GitHub Release 获取新版本，校验后替换自身。
/// 原版使用 DMG + bash 替换脚本；Windows 版使用 zip 资产 + PowerShell 替换脚本，流程一致：
/// 等待当前进程退出 → 备份旧版 → 换上新版 → 失败时恢复 → 重新打开。
public static class Updater
{
    public const string Repository = "Cetirzine/Kiln-Windows";
    public const string ExeName = AppInfo.ShortName + ".exe";

    public sealed record Release(string Version, string Notes, string PageUrl, string ZipUrl, string ZipName,
        string? Sha256, long? Size);

    public static LauncherException NotWritable(string path) =>
        new(T.Invoke($"没有权限替换 {path}，请手动下载新版本。",
            $"No permission to replace {path}. Download the new version manually."));

    public static LauncherException Verification(string reason) =>
        new(T.Invoke($"更新包校验失败：{reason}", $"The update failed verification: {reason}"));

    // MARK: 检查

    /// 获取最新版本；比当前版本新时返回 Release
    public static async Task<Release?> Check()
    {
        var j = Json.Parse(await Net.data(
            new[] { $"https://api.github.com/repos/{Repository}/releases/latest" }, fresh: true));
        var tag = j["tag_name"].String;
        if (tag == null || j["draft"].Bool == true || j["prerelease"].Bool == true) return null;
        var version = tag.StartsWith('v') ? tag[1..] : tag;
        if (!IsNewer(version, AppInfo.Version)) return null;
        var asset = j["assets"].Array.FirstOrDefault(a => (a["name"].String ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        if (asset.IsNull) return null;
        var url = asset["browser_download_url"].String;
        if (url == null) return null;
        var digest = asset["digest"].String is { } dg && dg.StartsWith("sha256:") ? dg["sha256:".Length..] : null;
        return new Release(version, j["body"].String ?? "", j["html_url"].String ?? "", url,
            asset["name"].String ?? $"Kiln-{version}.zip", digest?.ToLowerInvariant(), asset["size"].Double is { } sz ? (long)sz : null);
    }

    /// 语义化版本比较（1.2.10 > 1.2.9），对应原版的 .numeric 比较
    public static bool IsNewer(string a, string b) => NumericCompare(a, b) > 0;

    internal static int NumericCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length || j < b.Length)
        {
            if (i < a.Length && char.IsDigit(a[i]))
            {
                if (j >= b.Length || !char.IsDigit(b[j])) return 1;
                int ai = i, bj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var an = a[ai..i].TrimStart('0');
                var bn = b[bj..j].TrimStart('0');
                var c = an.Length != bn.Length ? an.Length.CompareTo(bn.Length)
                    : string.CompareOrdinal(an, bn);
                if (c != 0) return c;
            }
            else if (j < b.Length && char.IsDigit(b[j]))
            {
                return -1;
            }
            else
            {
                var ca = i < a.Length ? a[i] : '\0';
                var cb = j < b.Length ? b[j] : '\0';
                if (ca != cb) return ca.CompareTo(cb);
                i++; j++;
            }
        }
        return 0;
    }

    /// 当前运行的程序所在目录；不是 exe 运行时返回 null
    public static string? CurrentApp => Environment.ProcessPath is { } exe && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        ? Path.GetDirectoryName(exe)
        : null;

    /// 检查能否原地替换当前程序
    public static void CheckReplaceable(string appDir)
    {
        try
        {
            var probe = Path.Combine(appDir, ".kiln-write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
        }
        catch
        {
            throw NotWritable(appDir);
        }
    }

    // MARK: 下载与准备

    /// 下载并校验新版本，复制到当前程序旁边，返回准备好的新目录位置
    public static async Task<string> Prepare(Release release, string appDir, Job? job = null)
    {
        var zip = Path.Combine(Paths.Cache, release.ZipName);
        try { File.Delete(zip); } catch { }
        var item = new DownloadItem(release.ZipUrl, zip, size: release.Size) { Urls = new[] { release.ZipUrl } };
        await Downloader.Run(new[] { item }, job, checkHash: false);
        try
        {
            // 校验 SHA-256（GitHub 为每个附件提供摘要）
            if (release.Sha256 is { } expected)
            {
                var actual = Hash.Sha256File(zip);
                if (actual == null || actual != expected)
                    throw Verification(T.Invoke("SHA-256 不一致", "SHA-256 mismatch"));
            }

            // 解压 zip，取出程序目录
            var mount = Path.Combine(Paths.Cache, "update-extract-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(mount);
            try
            {
                Zip.ExtractAll(zip, mount);
                // 新版可能在压缩包根目录或唯一的一级子目录中
                var newApp = Directory.EnumerateFiles(mount, ExeName, SearchOption.AllDirectories)
                    .Select(Path.GetDirectoryName)
                    .FirstOrDefault();
                if (newApp == null)
                    throw Verification(T.Invoke("安装包中没有找到应用", "No app found in the update"));
                var versionInfo = FileVersionInfo.GetVersionInfo(Path.Combine(newApp, ExeName));
                if (versionInfo.ProductName != AppInfo.ShortName)
                    throw Verification(T.Invoke("应用标识不符", "Product name mismatch"));
                if (versionInfo.ProductVersion is not { } pv || !string.Equals(pv.Split('+')[0], release.Version, StringComparison.Ordinal))
                    throw Verification(T.Invoke("版本号不符", "Version mismatch"));

                // 复制到当前程序旁边（同一卷上，替换时只需重命名）
                var staged = Path.Combine(Path.GetDirectoryName(appDir)!, $".Kiln-update-{release.Version}");
                try { Directory.Delete(staged, recursive: true); } catch { }
                CopyDirectory(newApp, staged);
                return staged;
            }
            finally
            {
                try { Directory.Delete(mount, recursive: true); } catch { }
            }
        }
        finally
        {
            try { File.Delete(zip); } catch { }
        }
    }

    private static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(src))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(src))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    // MARK: 替换

    /// 生成替换脚本：等待当前进程退出 → 备份旧版 → 换上新版 → 失败时恢复 → 重新打开
    public static string ReplaceScript(string staged, string appDir, int pid, bool relaunch)
    {
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        var backup = Path.Combine(Path.GetDirectoryName(appDir)!, ".Kiln-previous");
        return $@"
# 由 Kiln 自动更新生成
Wait-Process -Id {pid} -ErrorAction SilentlyContinue
if (Test-Path {Q(backup)}) {{ Remove-Item -Recurse -Force {Q(backup)} }}
$ok = $false
try {{
    Move-Item {Q(appDir)} {Q(backup)}
    Move-Item {Q(staged)} {Q(appDir)}
    $ok = $true
}} catch {{
    # 替换失败：恢复旧版
    if (-not (Test-Path {Q(appDir)})) {{ Move-Item {Q(backup)} {Q(appDir)} }}
    Remove-Item -Recurse -Force {Q(staged)} -ErrorAction SilentlyContinue
}}
if ($ok) {{ Remove-Item -Recurse -Force {Q(backup)} -ErrorAction SilentlyContinue }}
{(relaunch ? $"Start-Process {Q(Path.Combine(appDir, ExeName))}" : "")}
";
    }

    /// 启动替换脚本（脱离当前进程运行），调用方随后退出应用
    public static void LaunchReplacement(string staged, string appDir, bool relaunch = true)
    {
        var script = Path.Combine(Paths.Cache, "kiln-update.ps1");
        File.WriteAllText(script, ReplaceScript(staged, appDir,
            Environment.ProcessId, relaunch), new UTF8Encoding(false));
        var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"",
                UseShellExecute = true,
                CreateNoWindow = true,
            },
        };
        p.Start();
        Log.log($"[Update] 已启动替换脚本：{Path.GetFileName(staged)} → {appDir}");
    }
}

