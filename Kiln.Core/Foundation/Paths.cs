namespace Kiln.Core;

/// 启动器的目录布局（Windows）。
/// 与原版 macOS 的对应关系：~/Library/Application Support → %APPDATA%（漫游），
/// ~/Library/Logs、~/Library/Caches → %LOCALAPPDATA%（本机）。
public static class Paths
{
    // Only the separate game verification worker may redirect launcher data.
    private static readonly string? DiagnosticRoot = ResolveDiagnosticRoot();
    private static string? ResolveDiagnosticRoot()
    {
        if (System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name != "Kiln.SelfTest") return null;
        var args = Environment.GetCommandLineArgs(); var mode = Array.IndexOf(args, "--game-smoke-worker");
        if (mode < 0) return null;
        var supplied = Environment.GetEnvironmentVariable("KILN_DIAGNOSTIC_ROOT") ?? throw new InvalidOperationException("Missing isolated game data root");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(supplied)); var leaf = Path.GetFileName(root);
        if (mode + 1 >= args.Length || !Path.GetFullPath(args[mode + 1]).Equals(root, StringComparison.OrdinalIgnoreCase) || !leaf.StartsWith("kiln-game-smoke-", StringComparison.Ordinal) || !Guid.TryParseExact(leaf["kiln-game-smoke-".Length..], "N", out _) || !File.Exists(Path.Combine(root, ".kiln-game-smoke")) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Expected an owned, marked game verification directory");
        return root;
    }
    public static string Downloads
    {
        get
        {
            if (DiagnosticRoot != null) return Ensure(Path.Combine(DiagnosticRoot, "Downloads"));
            try
            {
                var saved = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders", "{374DE290-123F-4565-9164-39C4925E467B}", null) as string;
                if (!string.IsNullOrWhiteSpace(saved)) return Path.GetFullPath(Environment.ExpandEnvironmentVariables(saved));
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }
    /// Windows 上官方启动器的默认游戏目录
    public static readonly string DefaultMinecraft =
        DiagnosticRoot == null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft") : Path.Combine(DiagnosticRoot, "game");

    private static string Ensure(string path)
    {
        try { Directory.CreateDirectory(path); } catch { }
        return path;
    }

    /// 启动器自身的数据目录（Java 运行时、日志、账号等）
    public static readonly string Support =
        Ensure(DiagnosticRoot == null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.ShortName) : Path.Combine(DiagnosticRoot, "Data"));

    public static string Runtime => Path.Combine(Support, "runtime");

    public static readonly string Logs = Ensure(DiagnosticRoot == null ? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.ShortName, "Logs") : Path.Combine(DiagnosticRoot, "Logs"));

    public static readonly string Cache = Ensure(DiagnosticRoot == null ? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.ShortName, "Cache") : Path.Combine(DiagnosticRoot, "Cache"));
}
