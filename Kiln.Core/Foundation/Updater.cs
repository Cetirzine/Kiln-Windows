using System.Diagnostics;
using System.Text;

namespace Kiln.Core;

/// 启动器自动更新：从 GitHub Release 获取新版本，校验后替换自身。
/// 原版使用 DMG + bash 替换脚本；Windows 版使用 zip 资产 + PowerShell 替换脚本，流程一致：
/// 等待当前进程退出 → 备份旧版 → 换上新版 → 失败时恢复 → 重新打开。
public static partial class Updater
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
    public static async Task<Release?> Check(string? currentVersion = null, CancellationToken ct = default)
    {
        currentVersion ??= AppInfo.Version;
        try
        {
            var j = Json.Parse(await Net.data(new[] { $"https://api.github.com/repos/{Repository}/releases/latest" }, fresh: true, ct: ct));
            return ParseRelease(j, currentVersion);
        }
        catch (Exception e) when (!ct.IsCancellationRequested && e is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            Log.log("[Update] GitHub API unavailable; checking the official release page: " + e.Friendly());
            return await CheckWebsite(currentVersion, ct);
        }
    }

    public static Release? ParseRelease(Json j, string currentVersion)
    {
        var tag = j["tag_name"].String;
        if (tag == null || j["draft"].Bool == true || j["prerelease"].Bool == true) return null;
        var version = tag.StartsWith('v') ? tag[1..] : tag;
        if (!IsNewer(version, currentVersion)) return null;
        var asset = j["assets"].Array.FirstOrDefault(a => string.Equals(a["name"].String, $"Kiln-Windows-x64-v{version}.zip", StringComparison.OrdinalIgnoreCase));
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
            appDir = ApplicationDirectory(appDir);
            foreach (var directory in new[] { appDir, Path.GetDirectoryName(appDir)! })
            {
                var probe = Path.Combine(directory, ".kiln-write-" + Guid.NewGuid().ToString("N"));
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write)) { }
                File.Delete(probe);
            }
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
        appDir = ApplicationDirectory(appDir);
        var token = job?.Token ?? default; token.ThrowIfCancellationRequested();
        var expected = release.Sha256;
        if (expected == null || expected.Length != 64 || !expected.All(Uri.IsHexDigit))
            throw Verification(T.Invoke("发布包缺少有效的 SHA-256 摘要", "The release has no valid SHA-256 digest"));
        var work = Path.Combine(Paths.Cache, "update-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
        var zip = Path.Combine(work, "update.zip");
        var staged = Path.Combine(Path.GetDirectoryName(appDir)!, ".Kiln-update-" + Guid.NewGuid().ToString("N"));
        var prepared = false;
        try
        {
            job?.Enter(T.Invoke("下载更新", "Downloading update"), 0, 3);
            var item = new DownloadItem(release.ZipUrl, zip, size: release.Size) { Urls = new[] { release.ZipUrl } };
            await Downloader.Run(new[] { item }, job, checkHash: false);
            token.ThrowIfCancellationRequested();
            job?.Enter(T.Invoke("校验更新", "Verifying update"), 1, 3);
            await Task.Run(() =>
            {
                var actual = Hash.Sha256File(zip); token.ThrowIfCancellationRequested();
                if (actual == null || !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw Verification(T.Invoke("SHA-256 不一致", "SHA-256 mismatch"));
                var mount = Path.Combine(work, "extract");
                Zip.ExtractAll(zip, mount);
                token.ThrowIfCancellationRequested();
                var candidates = Directory.EnumerateFiles(mount, ExeName, SearchOption.AllDirectories).ToArray();
                if (candidates.Length != 1)
                    throw Verification(T.Invoke("安装包中没有找到应用", "No app found in the update"));
                var newApp = Path.GetDirectoryName(candidates[0])!;
                var versionInfo = FileVersionInfo.GetVersionInfo(candidates[0]);
                if (versionInfo.ProductName != AppInfo.ShortName)
                    throw Verification(T.Invoke("应用标识不符", "Product name mismatch"));
                if (versionInfo.ProductVersion is not { } pv || !string.Equals(pv.Split('+')[0], release.Version, StringComparison.Ordinal))
                    throw Verification(T.Invoke("版本号不符", "Version mismatch"));
                foreach (var required in new[] { "Kiln.dll", "Kiln.Core.dll", "Microsoft.ui.xaml.dll", "webp.dll", "Assets/AppIcon.ico" })
                    if (!File.Exists(Path.Combine(newApp, required))) throw Verification(T.Invoke($"缺少 {required}", $"Missing {required}"));
                job?.Enter(T.Invoke("准备替换", "Preparing replacement"), 2, 3);
                CopyDirectory(newApp, staged, token);
                foreach (var file in Directory.EnumerateFiles(appDir, "unins*", SearchOption.TopDirectoryOnly))
                {
                    token.ThrowIfCancellationRequested();
                    if (Path.GetExtension(file).ToLowerInvariant() is ".exe" or ".dat" or ".msg")
                        File.Copy(file, Path.Combine(staged, Path.GetFileName(file)), overwrite: true);
                }
            }, token);
            token.ThrowIfCancellationRequested(); prepared = true; return staged;
        }
        finally
        {
            DeleteOwned(work, Paths.Cache, "update-");
            if (!prepared) Discard(staged, appDir);
        }
    }

    private static void CopyDirectory(string src, string dest, CancellationToken token)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(src))
        { token.ThrowIfCancellationRequested(); File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true); }
        foreach (var dir in Directory.EnumerateDirectories(src))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)), token);
    }

    private static string ApplicationDirectory(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (Directory.GetParent(full) == null || full == Path.GetPathRoot(full)) throw new ArgumentException("The app directory cannot be a volume root.");
        return full;
    }
    private static void ValidateSibling(string staged, string appDir)
    {
        appDir = ApplicationDirectory(appDir); staged = ApplicationDirectory(staged);
        if (!string.Equals(Path.GetDirectoryName(staged), Path.GetDirectoryName(appDir), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(staged, appDir, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(staged).StartsWith(".Kiln-update-", StringComparison.Ordinal))
            throw new ArgumentException("The staged update must be an owned sibling of the app directory.");
    }
    private static void DeleteOwned(string path, string parent, string prefix)
    {
        var full = ApplicationDirectory(path);
        if (!string.Equals(Path.GetDirectoryName(full), Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith(prefix, StringComparison.Ordinal)) throw new ArgumentException("Unexpected update cleanup path.");
        try { if (Directory.Exists(full)) Directory.Delete(full, recursive: true); } catch { }
    }
    public static void Discard(string staged, string appDir)
    { ValidateSibling(staged, appDir); DeleteOwned(staged, Path.GetDirectoryName(ApplicationDirectory(appDir))!, ".Kiln-update-"); }

}

