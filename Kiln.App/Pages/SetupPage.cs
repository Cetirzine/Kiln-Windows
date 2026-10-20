using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

/// 设置页：与原版 SetupPage 对应的全部启动器设置
public sealed class SetupPage : Page
{
    public SetupPage()
    {
        var s = LauncherSettings.Shared;
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 16, 0, 0) };
        panel.Children.Add(Ui.Text(Localization.T("设置", "Settings"), 26, FontWeights.SemiBold));

        // 启动
        panel.Children.Add(Ui.Text(Localization.T("游戏目录", "Game directory"), 17, FontWeights.SemiBold));
        var folderBox = Ui.Box(s.MinecraftFolder, "", v => s.MinecraftFolder = v);
        var folderRow = Ui.Row(folderBox, Ui.Button(Localization.T("浏览…", "Browse…"), async (_, _) =>
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow!));
            picker.FileTypeFilter.Add("*");
            var folder = await picker.PickSingleFolderAsync();
            if (folder != null) { s.MinecraftFolder = folder.Path; folderBox.Text = folder.Path; }
        }));
        panel.Children.Add(folderRow);

        panel.Children.Add(Ui.Setting(Localization.T("语言", "Language"),
            Ui.Combo(new[] { "简体中文", "繁體中文", "English" },
                s.Language switch { AppLanguage.ZhHans => 0, AppLanguage.ZhHant => 1, _ => 2 },
                i => s.Language = i switch { 0 => AppLanguage.ZhHans, 1 => AppLanguage.ZhHant, _ => AppLanguage.En })));
        panel.Children.Add(Ui.Setting(Localization.T("新版本隔离", "Isolation for new instances"),
            Ui.Combo(new[] { Localization.T("不隔离", "None"), Localization.T("隔离 Mod 版与快照", "Modded & snapshots"), Localization.T("全部隔离", "All") },
                (int)s.Isolation, i => s.Isolation = (IsolationMode)i)));
        panel.Children.Add(Ui.Setting(Localization.T("自动分配内存", "Automatic memory"),
            Ui.Toggle(s.MemoryAuto, "", "", v => s.MemoryAuto = v)));
        panel.Children.Add(Ui.Setting(Localization.T("内存（GB）", "Memory (GB)"),
            Ui.Num(s.MemoryGB, 0.5, 64, 0.5, v => { s.MemoryAuto = false; s.MemoryGB = v; })));
        panel.Children.Add(Ui.Setting(Localization.T("游戏窗口", "Game window"),
            Ui.Combo(new[] { Localization.T("全屏", "Fullscreen"), Localization.T("默认大小", "Default"), Localization.T("自定义", "Custom") },
                (int)s.WindowModeSetting, i => s.WindowModeSetting = (WindowMode)i)));
        panel.Children.Add(Ui.Setting(Localization.T("窗口宽度", "Window width"),
            Ui.Num(s.WindowWidth, 100, 7680, 2, v => s.WindowWidth = (int)v)));
        panel.Children.Add(Ui.Setting(Localization.T("窗口高度", "Window height"),
            Ui.Num(s.WindowHeight, 100, 4320, 2, v => s.WindowHeight = (int)v)));
        panel.Children.Add(Ui.Setting(Localization.T("启动器可见性", "Launcher visibility"),
            Ui.Combo(new[]
            {
                Localization.T("启动后关闭", "Close on launch"),
                Localization.T("隐藏，游戏退出后关闭", "Hide, close after game"),
                Localization.T("隐藏，游戏退出后显示", "Hide, reopen after game"),
                Localization.T("最小化", "Minimize"),
                Localization.T("保持打开", "Keep open"),
            }, (int)s.Visibility, i => s.Visibility = (VisibilityMode)i)));

        // 性能
        panel.Children.Add(Ui.Text(Localization.T("性能", "Performance"), 17, FontWeights.SemiBold));
        panel.Children.Add(Ui.Setting(Localization.T("垃圾回收器", "Garbage collector"),
            Ui.Combo(new[] { "G1", "ZGC" }, (int)s.GcMode, i => s.GcMode = (GCMode)i)));
        panel.Children.Add(Ui.Setting(Localization.T("类数据共享 / AOT 缓存", "Class data sharing / AOT cache"),
            Ui.Toggle(s.ClassDataSharing, "", "", v => s.ClassDataSharing = v)));
        panel.Children.Add(Ui.Setting(Localization.T("紧凑对象头（Java 24+）", "Compact object headers (Java 24+)"),
            Ui.Toggle(s.CompactHeaders, "", "", v => s.CompactHeaders = v)));
        panel.Children.Add(Ui.Setting(Localization.T("快速校验（只重新校验变化过的文件）", "Fast verify"),
            Ui.Toggle(s.FastVerify, "", "", v => s.FastVerify = v)));

        // 下载
        panel.Children.Add(Ui.Text(Localization.T("下载", "Download"), 17, FontWeights.SemiBold));
        panel.Children.Add(Ui.Setting(Localization.T("版本列表源", "Version list source"),
            Ui.Combo(new[] { Localization.T("镜像优先", "Mirror first"), Localization.T("官方缓慢时用镜像", "Mirror if official is slow"), Localization.T("官方优先", "Official first") },
                (int)s.VersionListSource, i => s.VersionListSource = (DownloadSource)i)));
        panel.Children.Add(Ui.Setting(Localization.T("文件下载源", "File download source"),
            Ui.Combo(new[] { Localization.T("镜像优先", "Mirror first"), Localization.T("官方缓慢时用镜像", "Mirror if official is slow"), Localization.T("官方优先", "Official first") },
                (int)s.FileSource, i => s.FileSource = (DownloadSource)i)));
        panel.Children.Add(Ui.Setting(Localization.T("下载并发数", "Download threads"),
            Ui.Num(s.DownloadThreads, 1, 256, 1, v => s.DownloadThreads = (int)v)));
        panel.Children.Add(Ui.Setting(Localization.T("CurseForge API 密钥（空 = MCIM 镜像）", "CurseForge API key (empty = MCIM mirror)"),
            Ui.Box(s.CurseforgeKey, "", v => s.CurseforgeKey = v)));
        panel.Children.Add(Ui.Setting(Localization.T("微软登录 Client ID（空 = 内置）", "Microsoft login Client ID (empty = built-in)"),
            Ui.Box(s.MsClientId, "", v => s.MsClientId = v)));

        // 系统
        panel.Children.Add(Ui.Text(Localization.T("系统", "System"), 17, FontWeights.SemiBold));
        panel.Children.Add(Ui.Setting(Localization.T("自动检查更新", "Check for updates"),
            Ui.Toggle(s.AutoCheckUpdates, "", "", v => s.AutoCheckUpdates = v)));
        panel.Children.Add(Ui.Setting(Localization.T("系统通知", "System notifications"),
            Ui.Toggle(s.SystemNotifications, "", "", v => s.SystemNotifications = v)));
        panel.Children.Add(Ui.Setting(Localization.T("调试日志", "Debug log"),
            Ui.Toggle(s.DebugLog, "", "", v => s.DebugLog = v)));
        panel.Children.Add(Ui.Setting(Localization.T("游戏语言跟随启动器", "Game language follows launcher"),
            Ui.Toggle(s.GameLanguageAuto, "", "", v => s.GameLanguageAuto = v)));
        panel.Children.Add(Ui.Row(Ui.Button(Localization.T("打开日志文件夹", "Open log folder"), (_, _) =>
        {
            try { _ = Windows.System.Launcher.LaunchUriAsync(new Uri(Paths.Logs)); } catch { }
        })));

        Content = new ScrollViewer { Content = panel };
    }
}

