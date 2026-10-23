using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

public sealed class SetupPage : Page
{
    private readonly LauncherSettings _s;
    private readonly IReadOnlyList<JavaInfo>? _javaFixtures;
    private bool Live => ReferenceEquals(_s, LauncherSettings.Shared);
    private static string T(string zh, string en) => Localization.T(zh, en);
    public SetupPage() : this(LauncherSettings.Shared) { }
    internal SetupPage(LauncherSettings settings, IReadOnlyList<JavaInfo>? javaFixtures = null)
    {
        _s = settings; _javaFixtures = javaFixtures;
        Content = Ui.Tabs("setup", T("设置", "Settings"),
            (T("游戏", "Game"), Game), (T("性能", "Performance"), Performance),
            ("Java", Java), (T("下载", "Downloads"), Downloads), (T("其他", "Other"), Other));
    }
    internal UIElement Section(int index) => index switch { 0 => Game(), 1 => Performance(), 2 => Java(), 3 => Downloads(), _ => Other() };
    private UIElement Game()
    {
        var folder = Ui.Box(_s.MinecraftFolder, "", v => _s.MinecraftFolder = v);
        var dimensions = new Grid { Name = "GameWindowDimensions", Width = 320, ColumnSpacing = 8 };
        dimensions.ColumnDefinitions.Add(new ColumnDefinition()); dimensions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); dimensions.ColumnDefinitions.Add(new ColumnDefinition());
        var width = Ui.Num(_s.WindowWidth, 100, 7680, 2, v => _s.WindowWidth = (int)v); var height = Ui.Num(_s.WindowHeight, 100, 4320, 2, v => _s.WindowHeight = (int)v);
        width.Name = "GameWindowWidth"; height.Name = "GameWindowHeight"; width.MinWidth = height.MinWidth = 0; width.HorizontalAlignment = height.HorizontalAlignment = HorizontalAlignment.Stretch;
        dimensions.Children.Add(width); var times = Ui.Text("×"); times.VerticalAlignment = VerticalAlignment.Center; times.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(times, 1); dimensions.Children.Add(times); Grid.SetColumn(height, 2); dimensions.Children.Add(height);
        var sizeRow = Ui.Setting(T("窗口大小", "Window size"), dimensions); sizeRow.Name = "CustomGameWindow"; sizeRow.Visibility = _s.WindowModeSetting == WindowMode.Custom ? Visibility.Visible : Visibility.Collapsed;
        var mode = Ui.Combo(new[] { T("全屏", "Fullscreen"), T("默认（854×480）", "Default (854×480)"), T("自定义", "Custom") }, (int)_s.WindowModeSetting, i => { _s.WindowModeSetting = (WindowMode)i; sizeRow.Visibility = i == (int)WindowMode.Custom ? Visibility.Visible : Visibility.Collapsed; }); mode.Name = "GameWindowMode";
        var total = Diagnostics.PhysicalMemoryGB;
        var memory = new Slider { Name = "ManualMemory", Minimum = 1, Maximum = Math.Max(_s.MemoryGB, Math.Max(2, total * .8)), StepFrequency = .5, SmallChange = .5, LargeChange = 1, Value = _s.MemoryGB };
        var memoryLabel = Ui.Text($"{memory.Value:0.0} GB", 12); memoryLabel.MinWidth = 56; memoryLabel.VerticalAlignment = VerticalAlignment.Center; memoryLabel.Foreground = Ui.Brush("KilnTextSecondary");
        var memoryControl = ValueSlider(memory, memoryLabel); var manual = Ui.Setting(T("手动", "Manual"), memoryControl); manual.Name = "ManualMemoryRow"; manual.Visibility = _s.MemoryAuto ? Visibility.Collapsed : Visibility.Visible;
        var bar = new MemoryAllocationBar(total); bar.Update(_s.MemoryAuto ? null : _s.MemoryGB);
        memory.ValueChanged += (_, _) => { _s.MemoryGB = memory.Value; memoryLabel.Text = $"{memory.Value:0.0} GB"; bar.Update(_s.MemoryAuto ? null : memory.Value); };
        var auto = Ui.Toggle(_s.MemoryAuto, "", "", value => { _s.MemoryAuto = value; manual.Visibility = value ? Visibility.Collapsed : Visibility.Visible; bar.Update(value ? null : _s.MemoryGB); }); auto.Name = "AutomaticMemory";
        return Ui.Scroll(
            Ui.Card(T("游戏目录", "Game directory"), Ui.Row(folder, Ui.Button(T("浏览…", "Browse…"), async (_, _) =>
            {
                var picker = new KilnFolderPicker();
                
                picker.FileTypeFilter.Add("*"); var result = await picker.PickSingleFolderAsync();
                if (result != null) { _s.MinecraftFolder = result.Path; folder.Text = result.Path; }
            })), Ui.Button(T("从 PCL2 继承配置…", "Inherit PCL2 settings…"), async (_, _) => await App.Pcl2TakeOverDialog(MainWindow.Current!, true))),
            Ui.Card(T("游戏", "Game"),
                Ui.Setting(T("新版本的游戏目录", "Game directory for new instances"), Ui.Combo(new[] { T("全部共用", "Shared by all"), T("Mod 版本与非正式版独立", "Separate for modded & previews"), T("全部独立", "Separate for all") }, (int)_s.Isolation, i => _s.Isolation = (IsolationMode)i), T("新安装的版本是否使用自己的游戏目录（存档、Mod、设置各自独立）", "Whether new instances get their own game directory (separate worlds, mods and options)")),
                Ui.Setting(T("游戏窗口", "Game window"), mode), sizeRow,
                Ui.Setting(T("游戏启动后", "After launching"), Ui.Combo(new[] { T("启动后退出启动器", "Quit after launch"), T("游戏运行时隐藏，退出后关闭", "Hide while playing, quit after exit"), T("游戏运行时隐藏，退出后恢复", "Hide while playing, show after exit"), T("启动后最小化", "Minimize after launch"), T("保持打开", "Keep open") }, (int)_s.Visibility, i => _s.Visibility = (VisibilityMode)i)),
                Ui.Setting(T("版本类型文字", "Version type text"), Ui.Box(_s.CustomInfo, "Kiln", v => _s.CustomInfo = v), T("显示在游戏主菜单左下角与 F3 调试界面", "Shown in the title screen corner and the F3 screen"))),
            Ui.Card(T("内存", "Memory"),
                Ui.Setting(T("自动分配", "Automatic"), auto, T("根据 Mod 数量与物理内存大小自动分配", "Allocates automatically based on mods and physical memory")), manual, bar),
            Ui.Card(T("高级", "Advanced"),
                Ui.Setting(T("额外 JVM 参数", "Extra JVM arguments"), Ui.Box(_s.JvmArgs, "-XX:+UseZGC", v => _s.JvmArgs = v), T("追加在 JVM 参数末尾；填写 -Xmx 或 GC 参数会覆盖自动设置", "Appended to the JVM arguments; -Xmx or GC flags override the automatic ones")),
                Ui.Setting(T("额外游戏参数", "Extra game arguments"), Ui.Box(_s.GameArgs, "", v => _s.GameArgs = v), T("追加在游戏参数末尾", "Appended to the game arguments")),
                Ui.Setting(T("跳过资源检查", "Skip asset check"), Ui.Toggle(_s.SkipAssetCheck, "", "", v => _s.SkipAssetCheck = v), T("跳过资源文件检查（仅在网络不佳时使用）", "Skip checking asset files (only for poor connections)"))));
    }
    private UIElement Performance()
    {
        var gc = Ui.Combo(new[] { T("G1（推荐）", "G1 (recommended)"), T("分代 ZGC", "Generational ZGC") }, (int)_s.GcMode, i => _s.GcMode = (GCMode)i);
        var choices = Ui.Segmented(gc, null); choices.Width = 320; choices.Name = "GarbageCollectorChoice";
        return Ui.Scroll(Ui.Card(T("Java 虚拟机", "Java virtual machine"),
            Ui.Setting(T("垃圾回收器", "Garbage collector"), choices, T("G1 适合大多数情况；分代 ZGC 停顿更短，但占用更多内存（需要 Java 21+）。", "G1 suits most setups; generational ZGC has shorter pauses but uses more memory (Java 21+).")),
            Ui.Setting(T("类数据共享（CDS）", "Class data sharing (CDS)"), Ui.Toggle(_s.ClassDataSharing, "", "", v => _s.ClassDataSharing = v), T("Java 19+ 可为兼容的版本生成类缓存，之后启动更快。", "With Java 19+, compatible instances can create a class archive so later launches start faster.")),
            Ui.Setting(T("紧凑对象头", "Compact object headers"), Ui.Toggle(_s.CompactHeaders, "", "", v => _s.CompactHeaders = v), T("Java 24+ 可减少堆内存占用。", "With Java 24+, reduces heap usage."))),
            Ui.Card(T("启动速度", "Launch speed"), Ui.Setting(T("快速校验", "Fast verification"), Ui.Toggle(_s.FastVerify, "", "", v => _s.FastVerify = v), T("文件未被修改时跳过 SHA-1 校验。", "Skip SHA-1 checks for files that haven't changed."))));
    }

    private static Grid ValueSlider(Slider slider, TextBlock value)
    {
        var grid = new Grid { Width = 320, ColumnSpacing = 10 }; grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.Children.Add(slider); Grid.SetColumn(value, 1); grid.Children.Add(value); return grid;
    }

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
                var row = new Grid { Name = "JavaRow", ColumnSpacing = 12, MinHeight = 50, Padding = new Thickness(10, 7, 12, 7) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14, Foreground = Ui.Brush("KilnTextSecondary") });
                var name = Ui.Text(title, 13, selected ? FontWeights.SemiBold : FontWeights.Medium); name.MaxLines = 1; name.TextTrimming = TextTrimming.CharacterEllipsis;
                var info = Ui.Text(detail, 11.5); info.MaxLines = 2; info.TextTrimming = TextTrimming.CharacterEllipsis; info.Foreground = Ui.Brush("KilnTextSecondary");
                var labels = Ui.Col(name, info); labels.Spacing = 2; labels.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(labels, 1); row.Children.Add(labels);
                if (selected) { var check = new FontIcon { Name = "JavaSelectedCheck", Glyph = "\uE73E", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Brush("KilnAccent") }; Grid.SetColumn(check, 2); row.Children.Add(check); }
                var choose = new Button { Name = "JavaChoice", Tag = path, Content = row, Style = (Style)Application.Current.Resources["KilnNavigationButton"], Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Ui.Brush(selected ? "KilnAccentSoft" : "KilnPanel"), BorderBrush = Ui.Brush("KilnAccent"), BorderThickness = new Thickness(selected ? 3 : 0, 0, 0, 0) };
                choose.Click += (_, _) => { _s.JavaPath = path; Render(); };
                ToolTipService.SetToolTip(choose, detail);
                var layout = new Grid { Background = Ui.Brush(selected ? "KilnAccentSoft" : "KilnPanel") };
                choose.PointerEntered += (_, _) => { if (!selected) choose.Background = Ui.Brush("KilnPanelSecondary"); };
                choose.PointerExited += (_, _) => { if (!selected) choose.Background = Ui.Brush("KilnPanel"); };
                layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                layout.Children.Add(choose);
                if (removable)
                {
                    layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var remove = Ui.Button(T("移除", "Remove"), (_, _) => { _s.CustomJavas = _s.CustomJavas.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList(); if (selected) _s.JavaPath = ""; javas.RemoveAll(j => j.Path == path); Render(); });
                    remove.Content = "−"; remove.Width = 28; remove.Padding = new Thickness(0); remove.Margin = new Thickness(0, 0, 12, 0); remove.HorizontalAlignment = HorizontalAlignment.Right; remove.VerticalAlignment = VerticalAlignment.Center; ToolTipService.SetToolTip(remove, T("移除", "Remove")); Grid.SetColumn(remove, 1); layout.Children.Add(remove);
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
                javas = (_javaFixtures ?? (Live ? await JavaManager.Search() : Array.Empty<JavaInfo>())).ToList(); Render();
                status.Text = javas.Count == 0 ? T("未找到 Java。启动游戏时会自动下载。", "No Java found. It will be downloaded automatically when launching.") : T($"已找到 {javas.Count} 个 Java", $"{javas.Count} Java runtimes found");
            }
            catch (Exception e) { status.Text = e.Friendly(); }
            finally { scanning = false; }
        }
        var add = Ui.Button(T("添加…", "Add…"), async (_, _) =>
        {
            var picked = await KilnFileBrowser.Pick(false, false, false, new[] { ".exe" }, allowFolders: true, title: T("选择 Java 可执行文件或 JDK 文件夹", "Choose a Java executable or JDK folder")); if (picked.Length == 0) return;
            var chosen = picked[0];
            var candidates = Directory.Exists(chosen) ? new[] { Path.Combine(chosen, "bin", "java.exe"), Path.Combine(chosen, "bin", "javaw.exe") } : new[] { Path.GetFileName(chosen).Equals("javaw.exe", StringComparison.OrdinalIgnoreCase) ? Path.Combine(Path.GetDirectoryName(chosen)!, "java.exe") : chosen, chosen };
            var java = await Task.Run(() => candidates.Select(JavaManager.Inspect).FirstOrDefault(j => j != null));
            if (java == null) { Hint.Show(T("无法识别这个 Java", "This Java couldn't be recognized"), HintKind.Error); return; }
            _s.CustomJavas = _s.CustomJavas.Append(java.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); Hint.Show(T("已添加 Java", "Java added"), HintKind.Success); await Search();
        });
        var refresh = Ui.Button(T("重新扫描", "Rescan"), async (_, _) => await Search());
        add.Width = refresh.Width = 88;
        Render(); _ = Search(); return Ui.Scroll(Ui.CardWithActions("Java", new[] { add, refresh }, list, status));
    }
    private UIElement Downloads()
    {
        var threads = new Slider { Name = "DownloadConcurrency", Minimum = 1, Maximum = Math.Max(128, _s.DownloadThreads), StepFrequency = 1, SmallChange = 1, LargeChange = 8, Value = _s.DownloadThreads };
        var threadCount = Ui.Text(_s.DownloadThreads.ToString(), 12); threadCount.MinWidth = 32; threadCount.VerticalAlignment = VerticalAlignment.Center; threadCount.Foreground = Ui.Brush("KilnTextSecondary");
        threads.ValueChanged += (_, _) => { _s.DownloadThreads = (int)threads.Value; threadCount.Text = _s.DownloadThreads.ToString(); };
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
            Ui.Setting(T("并发下载数", "Concurrent downloads"), ValueSlider(threads, threadCount), T("同时进行的下载数量", "How many files to download at once")),
            Ui.Setting(T("新版本使用启动器语言", "Use launcher language for new instances"), Ui.Toggle(_s.GameLanguageAuto, "", "", v => _s.GameLanguageAuto = v)),
            Ui.Setting(T("CurseForge API 密钥", "CurseForge API key"), apiKey, T("可选。留空使用社区公共代理；填写官方密钥后使用官方接口。", "Optional. Uses the public community proxy when empty, or the official API with your key.")),
            Ui.Setting("", EqualActions(test, apply, width: 320)), connectionStatus));
    }
    private UIElement Other()
    {
        var languages = Ui.Combo(new[] { "简体中文", "繁體中文", "English" }, _s.Language switch { AppLanguage.ZhHans => 0, AppLanguage.ZhHant => 1, _ => 2 }, i =>
        {
            _s.Language = i switch { 0 => AppLanguage.ZhHans, 1 => AppLanguage.ZhHant, _ => AppLanguage.En };
            if (Live) DispatcherQueue.TryEnqueue(() => MainWindow.Current!.RefreshLanguage());
        });
        var languageChoice = Ui.Segmented(languages, null); languageChoice.Name = "LanguageChoice"; languageChoice.Width = 320;
        var clear = Ui.Button(T("清理缓存", "Clear cache"), async (sender, _) =>
        {
            var button = (Button)sender; button.IsEnabled = false;
            try
            {
                CacheMaintenance.EnsureIdle(MainWindow.AllJobs, MainWindow.Games);
                if (CacheMaintenance.ContainsGameData(Paths.Cache, new[] { _s.MinecraftFolder }.Concat(_s.ExtraFolders))) throw new LauncherException(T("游戏数据位于缓存目录内，无法清理。", "Game data is inside the cache directory; it cannot be cleared."));
                if (!Live) return;
                await Task.Run(() => CacheMaintenance.ClearContents(Paths.Cache)); Hint.Show(T("缓存已清理", "Cache cleared"), HintKind.Success);
            }
            catch (Exception error) { Hint.Show(error.Friendly(), HintKind.Error); }
            finally { button.IsEnabled = true; }
        }); clear.Name = "ClearCache";
        return Ui.Scroll(
        Ui.Card(T("语言", "Language"), Ui.Setting(T("界面语言", "Interface language"), languageChoice, T("繁体中文由简体中文自动转换生成。", "Traditional Chinese is converted automatically from Simplified Chinese."))),
        Ui.Card(T("微软账号登录", "Microsoft sign-in"), Ui.Setting("Azure Client ID", Ui.Box(_s.MsClientId, T("使用内置 ID", "Use the built-in ID"), v => _s.MsClientId = v), _s.HasEmbeddedClientId ? T("启动器已内置 Client ID。只有想换用自己的应用时才需要填写。", "A Client ID is built into the launcher. Fill this in only to use your own app instead.") : T("微软登录需要在 Azure 注册并获准使用 Minecraft API 的应用 ID。", "Microsoft sign-in needs an Azure app ID approved for the Minecraft API."))),
        Ui.Card(T("通知与快捷方式", "Notifications & shortcuts"), Ui.Setting(T("系统通知", "System notifications"), Ui.Toggle(_s.SystemNotifications, "", "", v => _s.SystemNotifications = v), T("启动器在后台时，任务完成或失败、游戏崩溃会发送通知", "When the launcher is in the background, get notified when tasks finish or fail and when the game crashes")),
            Ui.Text(T("在版本菜单中创建桌面快捷方式或复制 kiln://launch/版本名 链接。", "Create desktop shortcuts or copy kiln://launch/<name> links from an instance menu."), 11.5)),
        Ui.Card(T("启动器窗口", "Launcher window"), Ui.Setting(T("窗口大小", "Window size"), Ui.Combo(new[] { T("紧凑 · 920 × 620", "Compact · 920 × 620"), T("标准 · 1040 × 680", "Standard · 1040 × 680"), T("宽敞 · 1200 × 800", "Roomy · 1200 × 800") }, _s.LauncherWindowPreset, i => { if (Live) MainWindow.Current!.ApplyWindowPreset(i); else _s.LauncherWindowPreset = i; }))),
        Ui.Card(T("更新", "Updates"), Ui.Setting(T("自动检查更新", "Check for updates automatically"), Ui.Toggle(_s.AutoCheckUpdates, "", "", v => _s.AutoCheckUpdates = v), T($"当前版本 {AppInfo.Version}。有新版本时会提示，确认后自动下载并替换。", $"Current version {AppInfo.Version}. Updates download and replace the app after confirmation.")), Ui.Setting("", Ui.Button(T("立即检查", "Check now"), async (sender, _) => { var button = (Button)sender; button.IsEnabled = false; try { if (Live) await AppUpdates.Check(MainWindow.Current!, true); } finally { button.IsEnabled = true; } }))),
        Ui.Card(T("数据与日志", "Data & logs"), EqualActions(new[] { Open(T("打开日志文件夹", "Open log folder"), Paths.Logs), Open(T("打开数据文件夹", "Open data folder"), Paths.Support), clear }), Ui.Setting(T("调试日志", "Debug log"), Ui.Toggle(_s.DebugLog, "", "", v => _s.DebugLog = v))));
    }

    private static Grid EqualActions(Button first, Button second, double width) => EqualActions(new[] { first, second }, width);
    private static Grid EqualActions(Button[] buttons, double width = double.NaN)
    {
        var grid = new Grid { Width = width, ColumnSpacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var button in buttons)
        {
            if (button.Content is string label) { button.Content = new TextBlock { Text = label, FontSize = 12.5, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 }; ToolTipService.SetToolTip(button, label); }
            grid.ColumnDefinitions.Add(new ColumnDefinition()); button.MinWidth = 0; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Center; Grid.SetColumn(button, grid.Children.Count); grid.Children.Add(button);
        }
        return grid;
    }

    private static Button Open(string title, string path) => Ui.Button(title, async (_, _) => await Windows.System.Launcher.LaunchFolderPathAsync(path));
}
