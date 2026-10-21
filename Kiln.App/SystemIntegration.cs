using System.Runtime.InteropServices;
using Kiln.Core;
using Microsoft.UI.Xaml;

namespace Kiln.App;

/// 系统集成（对应原版 SystemIntegration）：
/// - 后台原生通知、点击回到任务或日志；前台轻提示
/// - kiln:// 完整启动参数、已有窗口转交、原生桌面快捷方式
/// - 启动时自动检查更新
internal static class SystemIntegration
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    public static void InitializeAppIdentity(string? appId = null) => SetCurrentProcessExplicitAppUserModelID(appId ?? ShellShortcuts.AppId);

    public static string ActivationChannelName
    {
        get
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return $"Kiln.Activation.v1.{identity.User!.Value}.{System.Diagnostics.Process.GetCurrentProcess().SessionId}";
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
    public static void AllowActivation(int processId) => AllowSetForegroundWindow((uint)processId);
    public static void BringToFront(MainWindow window)
    {
        window.AppWindow.Show();
        if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        window.Activate(); SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(window));
    }

    internal static async Task<InstanceChannel?> ConnectActivation(string name, string message)
    {
        var channel = new InstanceChannel(name);
        try
        {
            if (channel.TryOwn()) return channel;
            try { await InstanceChannel.Send(name, message, beforeSend: AllowActivation); }
            catch { if (channel.TryOwn()) return channel; throw; }
            channel.Dispose(); return null;
        }
        catch { channel.Dispose(); throw; }
    }
    public static async void CreateShortcut(McInstance instance)
    {
        try
        {
            await DotScene.BlockTextures.Shared.Ensure(new[] { instance.Icon });
            var path = await CreateShortcutFile(instance, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Path.Combine(Paths.Support, "shell-icons"));
            Hint.Show(Localization.T($"已在桌面创建 {instance.Name} 的快捷方式", $"Created a desktop shortcut for {instance.Name}"), HintKind.Success);
            await Pages.InstanceCardUi.Reveal(path);
        }
        catch (Exception e) { Hint.Show(e.Friendly(), HintKind.Error); }
    }

    internal static void CopyLaunchLink(McInstance instance)
    {
        var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(LaunchLink.Create(instance.Name));
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
        Hint.Show(Localization.T("已复制启动链接", "Launch link copied"), HintKind.Success);
    }

    internal static async Task<string> CreateShortcutFile(McInstance instance, string desktop, string icons)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Launcher executable is unavailable.");
        var icon = await ShortcutIcons.Create(instance.Icon, icons);
        var name = string.Concat(instance.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || char.IsControl(c) ? '_' : c)).Trim(' ', '.');
        if (name.Length > 80) name = name[..(char.IsHighSurrogate(name[79]) ? 79 : 80)];
        if (name.Length == 0) name = "Minecraft";
        Directory.CreateDirectory(desktop);
        for (var number = 1; number <= 10000; number++)
        {
            var path = Path.Combine(desktop, "Kiln - " + name + (number == 1 ? "" : $" ({number})") + ".lnk");
            if (Directory.Exists(path)) continue;
            try { using var reservation = new FileStream(path, FileMode.CreateNew, FileAccess.Write); }
            catch (IOException) when (File.Exists(path) || Directory.Exists(path)) { continue; }
            try { ShellShortcuts.Write(path, exe, "\"" + LaunchLink.Create(instance.Name) + "\"", icon, instance.Name); return path; }
            catch { File.Delete(path); throw; }
        }
        throw new IOException("No free shortcut name is available.");
    }
    private static bool _flashUnavailable;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    private const uint FLASHW_ALL = 3;
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    internal static bool IsForeground
    {
        get { GetWindowThreadProcessId(GetForegroundWindow(), out var process); return process == Environment.ProcessId; }
    }
    private static DesktopNotifications? _notifications;

    /// 前台已有轻提示；后台任务完成/崩溃使用系统通知，遵循用户与系统设置。
    public static void Notify(string title, string message, string? action = null, bool force = false)
    {
        try
        {
            if (!LauncherSettings.Shared.SystemNotifications || MainWindow.Current?.IsDiagnostics == true) return;
            var window = App.MainAppWindow;
            if (window == null) return;
            if (!window.DispatcherQueue.HasThreadAccess) { window.DispatcherQueue.TryEnqueue(() => Notify(title, message, action, force)); return; }
            if (!force && IsForeground) return;
            try
            {
                _notifications ??= new DesktopNotifications(ShellShortcuts.AppId, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Kiln", "Kiln.lnk"));
                _notifications.Show(title, message, "kiln://" + (action is "tasks" or "log" ? action : "show"));
                return;
            }
            catch (Exception e) { Log.log("[Notify] " + e.Friendly()); }
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var info = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = hwnd,
                dwFlags = FLASHW_ALL,
                uCount = 5,
                dwTimeout = 0,
            };
            FlashWindowEx(ref info);
        }
        catch
        {
            if (!_flashUnavailable)
            {
                _flashUnavailable = true;
                Log.log($"[Notify] {title}：{message}");
            }
        }
    }

    /// 注册 kiln:// 协议（免打包应用：写 HKCU）
    public static void RegisterProtocol()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null) return;
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\kiln");
            key.SetValue("", "URL:Kiln");
            key.SetValue("URL Protocol", "");
            using var cmd = key.CreateSubKey(@"shell\open\command");
            cmd.SetValue("", $"\"{exe}\" \"%1\"");
            Log.log("[System] 已注册 kiln:// 协议");
        }
        catch (Exception e)
        {
            Log.log($"[System] 注册协议失败：{e.Friendly()}");
        }
    }

    /// 启动时自动检查更新（对应原版 autoCheckUpdates）
    public static Task AutoUpdateCheck(MainWindow window) => AppUpdates.Check(window, userInitiated: false);
}
