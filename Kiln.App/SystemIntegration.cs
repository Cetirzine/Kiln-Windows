using System.Runtime.InteropServices;
using Kiln.Core;
using Microsoft.UI.Xaml;

namespace Kiln.App;

/// 系统集成（对应原版 SystemIntegration）：
/// - 后台提醒：启动器在后台时，任务完成 / 游戏崩溃时闪烁任务栏并显示通知横幅（应用内信息条）
/// - kiln:// 协议：kiln://launch/<版本名> 直接启动某个版本（桌面快捷方式 / 命令行）
/// - 启动时自动检查更新
internal static class SystemIntegration
{
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

    /// 处理 kiln://launch/<版本名> 激活参数
    public static string? ParseLaunchUri(string[] args)
    {
        foreach (var arg in args)
        {
            if (!arg.StartsWith("kiln://", StringComparison.OrdinalIgnoreCase)) continue;
            var rest = arg["kiln://".Length..];
            if (rest.StartsWith("launch/", StringComparison.OrdinalIgnoreCase))
            {
                var name = Uri.UnescapeDataString(rest["launch/".Length..].TrimEnd('/'));
                return name.Length > 0 ? name : null;
            }
        }
        return null;
    }

    /// 启动时自动检查更新（对应原版 autoCheckUpdates）
    public static async Task AutoUpdateCheck(MainWindow window)
    {
        try
        {
            if (!LauncherSettings.Shared.AutoCheckUpdates) return;
            // 距上次检查不足 24 小时则跳过
            if (LauncherSettings.Shared.LastUpdateCheck is { } last
                && (DateTime.UtcNow - last).TotalHours < 24) return;
            LauncherSettings.Shared.LastUpdateCheck = DateTime.UtcNow;
            var release = await Updater.Check();
            if (release == null) return;
            var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
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
