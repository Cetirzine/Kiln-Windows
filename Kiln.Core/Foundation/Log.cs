using System.Text;

namespace Kiln.Core;

/// 启动器日志：写入 %LOCALAPPDATA%\Kiln\Logs\Log1.txt（上次启动的为 Log2.txt）
public static class Log
{
    private static readonly object Gate = new();
    private static FileStream? _handle;

    static Log()
    {
        try
        {
            var path = LogFile;
            if (File.Exists(path))
            {
                var old = Path.Combine(Paths.Logs, "Log2.txt");
                try { File.Delete(old); } catch { }
                try { File.Move(path, old); } catch { }
            }
            _handle = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        }
        catch { _handle = null; }
    }

    public static string LogFile => Path.Combine(Paths.Logs, "Log1.txt");

    public static void Write(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n";
        lock (Gate)
        {
            if (_handle != null)
            {
                try { _handle.Write(Encoding.UTF8.GetBytes(line)); _handle.Flush(); } catch { }
            }
#if DEBUG
            Console.Error.Write(line);
#endif
        }
    }

    public static void log(string message) => Write(message);
}

public sealed class LauncherException : Exception
{
    public LauncherException(string message) : base(message) { }
}

public static class ExceptionExtensions
{
    /// 获取较为友好的错误描述
    public static string Friendly(this Exception e) => e switch
    {
        LauncherException le => le.Message,
        OperationCanceledException => "已取消",
        HttpRequestException h when h.InnerException is System.Net.Sockets.SocketException se =>
            $"{h.Message} ({se.SocketErrorCode})",
        _ => e.Message,
    };
}
