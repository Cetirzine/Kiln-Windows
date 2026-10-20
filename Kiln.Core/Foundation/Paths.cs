namespace Kiln.Core;

/// 启动器的目录布局（Windows）。
/// 与原版 macOS 的对应关系：~/Library/Application Support → %APPDATA%（漫游），
/// ~/Library/Logs、~/Library/Caches → %LOCALAPPDATA%（本机）。
public static class Paths
{
    /// Windows 上官方启动器的默认游戏目录
    public static readonly string DefaultMinecraft =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");

    private static string Ensure(string path)
    {
        try { Directory.CreateDirectory(path); } catch { }
        return path;
    }

    /// 启动器自身的数据目录（Java 运行时、日志、账号等）
    public static readonly string Support =
        Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.ShortName));

    public static string Runtime => Path.Combine(Support, "runtime");

    public static readonly string Logs = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.ShortName, "Logs"));

    public static readonly string Cache = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.ShortName, "Cache"));
}
