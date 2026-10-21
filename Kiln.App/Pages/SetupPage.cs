using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

public sealed class SetupPage : Page
{
    private readonly LauncherSettings _s = LauncherSettings.Shared;
    private static string T(string zh, string en) => Localization.T(zh, en);
    public SetupPage()
    {
        Content = Ui.Tabs("setup", T("设置", "Settings"),
            (T("游戏", "Game"), Game), (T("性能", "Performance"), Performance),
            ("Java", Java), (T("下载", "Downloads"), Downloads), (T("其他", "Other"), Other));
    }
    private UIElement Game()
    {
        var folder = Ui.Box(_s.MinecraftFolder, "", v => _s.MinecraftFolder = v);
        return Ui.Scroll(
            Ui.Card(T("游戏目录", "Game directory"), Ui.Row(folder, Ui.Button(T("浏览…", "Browse…"), async (_, _) =>
            {
                var picker = new KilnFolderPicker();
                
                picker.FileTypeFilter.Add("*"); var result = await picker.PickSingleFolderAsync();
                if (result != null) { _s.MinecraftFolder = result.Path; folder.Text = result.Path; }
            })), Ui.Button(T("从 PCL2 继承配置…", "Inherit PCL2 settings…"), async (_, _) => await App.Pcl2TakeOverDialog(MainWindow.Current!, true))),
            Ui.Card(T("游戏", "Game"),
                Ui.Setting(T("新版本的游戏目录", "Game directory for new instances"), Ui.Combo(new[] { T("全部共用", "Shared by all"), T("Mod 版本与非正式版独立", "Separate for modded & previews"), T("全部独立", "Separate for all") }, (int)_s.Isolation, i => _s.Isolation = (IsolationMode)i)),
                Ui.Setting(T("游戏窗口", "Game window"), Ui.Combo(new[] { T("全屏", "Fullscreen"), T("默认（854×480）", "Default (854×480)"), T("自定义", "Custom") }, (int)_s.WindowModeSetting, i => _s.WindowModeSetting = (WindowMode)i)),
                Ui.Setting(T("窗口大小", "Window size"), Ui.Row(Ui.Num(_s.WindowWidth, 100, 7680, 2, v => _s.WindowWidth = (int)v), Ui.Text("×"), Ui.Num(_s.WindowHeight, 100, 4320, 2, v => _s.WindowHeight = (int)v))),
                Ui.Setting(T("游戏启动后", "After launching"), Ui.Combo(new[] { T("启动后退出启动器", "Quit after launch"), T("游戏运行时隐藏，退出后关闭", "Hide while playing, quit after exit"), T("游戏运行时隐藏，退出后恢复", "Hide while playing, show after exit"), T("启动后最小化", "Minimize after launch"), T("保持打开", "Keep open") }, (int)_s.Visibility, i => _s.Visibility = (VisibilityMode)i)),
                Ui.Setting(T("版本类型文字", "Version type text"), Ui.Box(_s.CustomInfo, "Kiln", v => _s.CustomInfo = v))),
            Ui.Card(T("内存", "Memory"),
                Ui.Setting(T("自动分配", "Automatic"), Ui.Toggle(_s.MemoryAuto, "", "", v => _s.MemoryAuto = v)),
                Ui.Setting(T("手动（GB）", "Manual (GB)"), Ui.Num(_s.MemoryGB, .5, 128, .5, v => { _s.MemoryGB = v; _s.MemoryAuto = false; }))),
            Ui.Card(T("高级", "Advanced"),
                Ui.Setting(T("额外 JVM 参数", "Extra JVM arguments"), Ui.Box(_s.JvmArgs, "-XX:+UseZGC", v => _s.JvmArgs = v)),
                Ui.Setting(T("额外游戏参数", "Extra game arguments"), Ui.Box(_s.GameArgs, "", v => _s.GameArgs = v)),
                Ui.Setting(T("跳过资源检查", "Skip asset check"), Ui.Toggle(_s.SkipAssetCheck, "", "", v => _s.SkipAssetCheck = v))));
    }
    private UIElement Performance() => Ui.Scroll(Ui.Card(T("性能", "Performance"),
        Ui.Setting(T("垃圾回收器", "Garbage collector"), Ui.Combo(new[] { "G1", "ZGC" }, (int)_s.GcMode, i => _s.GcMode = (GCMode)i)),
        Ui.Setting(T("类数据共享 / AOT 缓存", "Class data sharing / AOT cache"), Ui.Toggle(_s.ClassDataSharing, "", "", v => _s.ClassDataSharing = v)),
        Ui.Setting(T("紧凑对象头（Java 24+）", "Compact object headers (Java 24+)"), Ui.Toggle(_s.CompactHeaders, "", "", v => _s.CompactHeaders = v)),
        Ui.Setting(T("快速校验", "Fast verify"), Ui.Toggle(_s.FastVerify, "", "", v => _s.FastVerify = v))));

    private UIElement Java()
    {
        var list = new StackPanel { Spacing = 0 };
        var status = Ui.Text("", 12); status.Foreground = Ui.Brush("KilnTextSecondary");
        var javas = new List<JavaInfo>();
        var scanning = false;
        void Render()
        {
            list.Children.Clear();
            void AddRow(string title, string detail, string path, string glyph, bool removable)
            {
                var selected = string.Equals(_s.JavaPath, path, StringComparison.OrdinalIgnoreCase);
                var row = new Grid { ColumnSpacing = 12, MinHeight = 62, Padding = new Thickness(12, 10, 12, 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 19, Foreground = Ui.Brush(selected ? "KilnAccent" : "KilnTextSecondary") });
                var name = Ui.Text(title, 13, FontWeights.SemiBold); name.MaxLines = 1; name.TextTrimming = TextTrimming.CharacterEllipsis;
                var info = Ui.Text(detail, 11.5); info.MaxLines = 1; info.TextTrimming = TextTrimming.CharacterEllipsis; info.Foreground = Ui.Brush("KilnTextSecondary");
                var labels = Ui.Col(name, info); labels.Spacing = 4; labels.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(labels, 1); row.Children.Add(labels);
                if (selected) { var check = new FontIcon { Glyph = "\uE73E", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Brush("KilnAccent") }; Grid.SetColumn(check, 2); row.Children.Add(check); }
                var choose = new Button { Content = row, Style = (Style)Application.Current.Resources["KilnNavigationButton"], Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Ui.Brush(selected ? "KilnHover" : "KilnPanel"), BorderBrush = Ui.Brush("KilnAccent"), BorderThickness = new Thickness(selected ? 3 : 0, 0, 0, 0) };
                choose.Click += (_, _) => { _s.JavaPath = path; Render(); };
                ToolTipService.SetToolTip(choose, detail);
                var layout = new Grid { Background = Ui.Brush(selected ? "KilnHover" : "KilnPanel") };
                layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                layout.Children.Add(choose);
                if (removable)
                {
                    layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var remove = Ui.Button(T("移除", "Remove"), (_, _) => { _s.CustomJavas = _s.CustomJavas.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList(); if (selected) _s.JavaPath = ""; javas.RemoveAll(j => j.Path == path); Render(); });
                    remove.Width = 64; remove.Margin = new Thickness(0, 0, 12, 0); remove.HorizontalAlignment = HorizontalAlignment.Right; remove.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(remove, 1); layout.Children.Add(remove);
                }
                list.Children.Add(new Border { Child = layout, BorderBrush = Ui.Brush("KilnStroke"), BorderThickness = new Thickness(0, 0, 0, 1) });
            }
            AddRow(T("自动选择（推荐）", "Automatic (recommended)"), T("根据游戏版本选择兼容的 Windows Java；缺少时自动下载。", "Chooses compatible Windows Java for each game version; downloads it when needed."), "", "\uE945", false);
            foreach (var java in javas) AddRow(java.DisplayName, java.Home, java.Path, "\uE950", _s.CustomJavas.Contains(java.Path, StringComparer.OrdinalIgnoreCase));
        }
        async Task Search()
        {
            if (scanning) return; scanning = true; status.Text = T("正在搜索 Java…", "Searching for Java…");
            try
            {
                javas = (await JavaManager.Search()).ToList(); Render();
                status.Text = javas.Count == 0 ? T("未找到 Java。启动游戏时会自动下载。", "No Java found. It will be downloaded automatically when launching.") : T($"已找到 {javas.Count} 个 Java", $"{javas.Count} Java runtimes found");
            }
            catch (Exception e) { status.Text = e.Friendly(); }
            finally { scanning = false; }
        }
        var add = Ui.Button(T("添加…", "Add…"), async (_, _) =>
        {
            var picker = new KilnFileOpenPicker();
             picker.FileTypeFilter.Add(".exe");
            var file = await picker.PickSingleFileAsync(); if (file == null) return;
            if (await Task.Run(() => JavaManager.Inspect(file.Path)) == null) { await Ui.ShowError(XamlRoot, T("无法识别这个 Java", "This Java couldn't be recognized")); return; }
            _s.CustomJavas = _s.CustomJavas.Append(file.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); await Search();
        });
        var refresh = Ui.Button(T("重新扫描", "Rescan"), async (_, _) => await Search());
        add.Width = refresh.Width = 88;
        Render(); _ = Search(); return Ui.Scroll(Ui.CardWithActions("Java", new[] { add, refresh }, list, status));
    }
    private UIElement Downloads()
    {
        var apiKey = new PasswordBox { Password = _s.CurseforgeKey, Width = 320, FontSize = 12.5, MinHeight = 28, PlaceholderText = T("留空使用公共代理；可填写自己的官方密钥", "Public proxy when empty; or your official API key"), CornerRadius = new CornerRadius(2) };
        apiKey.LostFocus += (_, _) => _s.CurseforgeKey = apiKey.Password.Trim();
        var connectionStatus = Ui.Text("", 11.5); connectionStatus.Foreground = Ui.Brush("KilnTextSecondary");
        var test = Ui.Button(T("保存并测试连接", "Save & test connection"), async (sender, _) =>
        {
            var button = (Button)sender; button.IsEnabled = false;
            _s.CurseforgeKey = apiKey.Password.Trim();
            connectionStatus.Text = T("正在连接…", "Connecting…");
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await CurseForge.CheckConnection(_s.CurseforgeKey, cancellation.Token); connectionStatus.Text = _s.CurseforgeKey.Length > 0 ? T("官方 API 连接成功。搜索、版本查询及文件下载均使用此密钥。", "Official API connected. This key is used for search, version lookup and file downloads.") : T("公共代理连接成功（Curse.tools / MCIM）。", "Public proxy connected (Curse.tools / MCIM)."); }
            catch (OperationCanceledException) { connectionStatus.Text = T("连接超时，请检查网络后重试。", "Connection timed out; check the network and retry."); }
            catch (Exception e) { connectionStatus.Text = e.Friendly(); }
            finally { button.IsEnabled = true; }
        });
        var apply = Ui.Button(T("申请官方密钥", "Apply for an API key"), (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://support.curseforge.com/support/solutions/articles/9000208346-about-the-curseforge-api-and-how-to-apply-for-a-key")));
        string[] sources = { T("优先使用镜像（BMCLAPI）", "Prefer mirror (BMCLAPI)"), T("官方源，较慢时改用镜像", "Official, mirror when slow"), T("优先使用官方源", "Prefer official") };
        return Ui.Scroll(Ui.Card(T("下载", "Downloads"),
            Ui.Setting(T("版本列表来源", "Version list source"), Ui.Combo(sources, (int)_s.VersionListSource, i => _s.VersionListSource = (DownloadSource)i)),
            Ui.Setting(T("文件下载来源", "File download source"), Ui.Combo(sources, (int)_s.FileSource, i => _s.FileSource = (DownloadSource)i)),
            Ui.Setting(T("并发下载数", "Concurrent downloads"), Ui.Num(_s.DownloadThreads, 1, 256, 1, v => _s.DownloadThreads = (int)v)),
            Ui.Setting(T("新版本使用启动器语言", "Use launcher language for new instances"), Ui.Toggle(_s.GameLanguageAuto, "", "", v => _s.GameLanguageAuto = v)),
            Ui.Setting(T("CurseForge API 密钥", "CurseForge API key"), apiKey),
            Ui.Setting("", Ui.Row(test, apply)), connectionStatus));
    }
    private UIElement Other() => Ui.Scroll(
        Ui.Card(T("语言", "Language"), Ui.Combo(new[] { "简体中文", "繁體中文", "English" }, _s.Language switch { AppLanguage.ZhHans => 0, AppLanguage.ZhHant => 1, _ => 2 }, i =>
        {
            _s.Language = i switch { 0 => AppLanguage.ZhHans, 1 => AppLanguage.ZhHant, _ => AppLanguage.En };
            DispatcherQueue.TryEnqueue(() => MainWindow.Current!.RefreshLanguage());
        })),
        Ui.Card(T("微软账号登录", "Microsoft sign-in"), Ui.Setting("Azure Client ID", Ui.Box(_s.MsClientId, T("使用内置 ID", "Use the built-in ID"), v => _s.MsClientId = v))),
        Ui.Card(T("通知与快捷方式", "Notifications & shortcuts"), Ui.Setting(T("系统通知", "System notifications"), Ui.Toggle(_s.SystemNotifications, "", "", v => _s.SystemNotifications = v)),
            Ui.Text(T("在版本菜单中创建桌面快捷方式或复制 kiln://launch/版本名 链接。", "Create desktop shortcuts or copy kiln://launch/<name> links from an instance menu."), 11.5)),
        Ui.Card(T("启动器窗口", "Launcher window"), Ui.Setting(T("窗口大小", "Window size"), Ui.Combo(new[] { T("紧凑 · 920 × 620", "Compact · 920 × 620"), T("标准 · 1040 × 680", "Standard · 1040 × 680"), T("宽敞 · 1200 × 800", "Roomy · 1200 × 800") }, _s.LauncherWindowPreset, i => MainWindow.Current!.ApplyWindowPreset(i)))),
        Ui.Card(T("更新", "Updates"), Ui.Setting(T("自动检查更新", "Check for updates automatically"), Ui.Toggle(_s.AutoCheckUpdates, "", "", v => _s.AutoCheckUpdates = v)), Ui.Button(T("立即检查", "Check now"), (_, _) => MainWindow.Current!.NavigateTo("about"))),
        Ui.Card(T("数据与日志", "Data & logs"), Ui.Row(Open(T("打开日志文件夹", "Open log folder"), Paths.Logs), Open(T("打开数据文件夹", "Open data folder"), Paths.Support)), Ui.Setting(T("调试日志", "Debug log"), Ui.Toggle(_s.DebugLog, "", "", v => _s.DebugLog = v))));

    private static Button Open(string title, string path) => Ui.Button(title, async (_, _) => await Windows.System.Launcher.LaunchFolderPathAsync(path));
}
