using System.Runtime.InteropServices;
using Kiln.Core;
using Microsoft.UI.Xaml;

namespace Kiln.App;

/// 系统集成（对应原版 SystemIntegration）：
/// - 后台提醒：目前闪烁任务栏，系统通知接入状态见 PORT-PARITY.md
/// - kiln:// 完整启动参数、已有窗口转交、原生桌面快捷方式
/// - 启动时自动检查更新
internal static class SystemIntegration
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    public static void InitializeAppIdentity() => SetCurrentProcessExplicitAppUserModelID(ShellShortcuts.AppId);

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

    /// 后台提醒（对应原版的系统通知）：任务栏闪烁引起注意
    public static void Notify(string title, string message)
    {
        try
        {
            if (!LauncherSettings.Shared.SystemNotifications) return;
            var window = App.MainAppWindow;
            if (window == null) return;
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
    public static async Task AutoUpdateCheck(MainWindow window)
    {
        try
        {
            if (!LauncherSettings.Shared.AutoCheckUpdates) return;
            // 原版自动检查间隔为 6 小时；失败的检查不延后下次重试。
            if (LauncherSettings.Shared.LastUpdateCheck is { } last
                && (DateTime.UtcNow - last).TotalHours < 6) return;
            var release = await Updater.Check();
            LauncherSettings.Shared.LastUpdateCheck = DateTime.UtcNow;
            if (release == null) return;
            var dialog = new KilnDialog
            {
                XamlRoot = window.Content.XamlRoot,
                Title = Localization.T($"新版本 {release.Version}", $"Update {release.Version}"),
                Content = release.Notes,
                PrimaryButtonText = Localization.T("更新", "Update"),
                SecondaryButtonText = Localization.T("查看", "View"),
                CloseButtonText = Localization.T("以后再说", "Later"),
            };
            var result = await dialog.ShowAsync();
            if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Secondary)
            {
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri(release.PageUrl));
                return;
            }
            if (result != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary) return;
            var appDir = Updater.CurrentApp;
            if (appDir == null) return;
            Updater.CheckReplaceable(appDir);
            var job = MainWindow.NewJob(Localization.T($"更新到 {release.Version}", $"Update to {release.Version}"));
            var staged = await Updater.Prepare(release, appDir, job);
            Updater.LaunchReplacement(staged, appDir);
            Application.Current.Exit();
        }
        catch (Exception e)
        {
            Log.log($"[Update] 自动检查失败：{e.Friendly()}");
        }
    }
}
