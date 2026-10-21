using Kiln.App.Pages;
using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App;

/// <summary>主窗口：侧边栏导航（对应原版 MainView），底部有下载管理。
/// 页面为纯代码构建（无 XAML 文件），Release 下 Frame.Navigate(Type) 无法从 XAML 元数据解析，
/// 因此直接创建页面实例设置 Content。</summary>
public sealed partial class MainWindow : Window
{
    private static readonly List<Job> Jobs = new();
    private static readonly object JobsGate = new();

    public new static MainWindow? Current { get; private set; }
    private string _currentPage = "home";
    private readonly Dictionary<string, Button> _navigation = new();
    public static List<GameProcess> Games { get; } = new();
    private McInstance? _detailInstance;
    public static Job? LaunchJob { get; set; }
    public event Action? AccountsUpdated;
    private Microsoft.UI.Xaml.Controls.Primitives.Popup? _accountPopup;
    private double? _captionBackdropLuminance;
    private bool _whiteCaption;
    private bool _diagnosticsRunning;
    internal bool IsDiagnostics { get; }
    private readonly TaskbarProgress? _taskbar;
    internal TaskbarProgress? Taskbar => _taskbar;
    internal event Action<Job, JobFeedback?>? JobCompleted;
    private readonly HintOverlay _hints = new();
    internal void ShowHint(string text, HintKind kind)
    {
        if (DispatcherQueue.HasThreadAccess) _hints.Show(text, kind);
        else DispatcherQueue.TryEnqueue(() => _hints.Show(text, kind));
    }
    internal void SetDiagnosticsRunning(bool running)
    {
        _diagnosticsRunning = running; Shell.IsHitTestVisible = !running;
        DragRegion.Children.Clear();
        if (running) DragRegion.Children.Add(new TextBlock { Text = Localization.T("正在运行界面诊断，请稍候…", "Running UI diagnostics, please wait…"), FontSize = 11, Foreground = Ui.Brush("KilnTextSecondary"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
    }
    private readonly DispatcherTimer _jobsTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private object? _jobsIdleContent;
    private StackPanel? _jobsProgressContent;
    private Border? _jobsProgressFill;
    private TextBlock? _jobsProgressLabel;

    internal void RefreshJobIndicator()
    {
        if (!_navigation.TryGetValue("jobs", out var button)) return;
        var running = AllJobs.Where(j => j.State == Job.JobState.Running).ToList();
        var progress = JobProgress.From(running); _taskbar?.Update(progress);
        if (running.Count == 0) { button.Content = _jobsIdleContent; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, Localization.T("任务", "Tasks")); return; }
        _jobsProgressFill!.Width = 34 * progress.Fraction;
        _jobsProgressLabel!.Text = Localization.T($"{running.Count} 个任务", $"{running.Count} task{(running.Count == 1 ? "" : "s")}");
        button.Content = _jobsProgressContent;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, _jobsProgressLabel.Text);
    }

    internal void SetCaptionBackdrop(double? luminance = null)
    {
        _captionBackdropLuminance = luminance;
        ApplyCaptionContrast();
    }

    private void ApplyCaptionContrast()
    {
        if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported()) return;
        // Hysteresis keeps moving highlights in the pixel scene from flashing the glyphs.
        _whiteCaption = _captionBackdropLuminance is double luminance
            ? luminance < (_whiteCaption ? .21 : .15)
            : Shell.ActualTheme == ElementTheme.Dark;
        var foreground = _whiteCaption ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        var caption = AppWindow.TitleBar;
        caption.ButtonBackgroundColor = caption.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        caption.ButtonForegroundColor = caption.ButtonInactiveForegroundColor = foreground;
        caption.ButtonHoverForegroundColor = caption.ButtonPressedForegroundColor = foreground;
        caption.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(32, foreground.R, foreground.G, foreground.B);
        caption.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(56, foreground.R, foreground.G, foreground.B);
    }

    public void AccountChanged() => AccountsUpdated?.Invoke();
    public async Task CloseAccountsAsync()
    {
        var popup = _accountPopup;
        if (popup == null || !popup.IsOpen) return;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnClosed(object? sender, object args) => closed.TrySetResult();
        popup.Closed += OnClosed;
        try
        {
            if (!popup.DispatcherQueue.TryEnqueue(() => popup.IsOpen = false)) return;
            await closed.Task;
        }
        finally { popup.Closed -= OnClosed; popup.Child = null; }
    }
    public void ApplyWindowPreset(int preset)
    {
        preset = Math.Clamp(preset, 0, 2);
        if (!IsDiagnostics) LauncherSettings.Shared.LauncherWindowPreset = preset;
        var (width, height) = preset switch { 0 => (920, 620), 2 => (1200, 800), _ => (1040, 680) };
        var scale = Shell.XamlRoot?.RasterizationScale ?? 1;
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.Restore();
            presenter.IsResizable = false;
        }
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(height * scale)));
    }
    public void ShowAccounts(FrameworkElement anchor)
    {
        if (_accountPopup?.IsOpen == true) { _accountPopup.IsOpen = false; return; }
        var root = Shell.XamlRoot;
        var origin = anchor.TransformToVisual(root.Content).TransformPoint(new Windows.Foundation.Point(0, anchor.ActualHeight));
        var popup = new Microsoft.UI.Xaml.Controls.Primitives.Popup { XamlRoot = root, IsLightDismissEnabled = true, ShouldConstrainToRootBounds = true };
        var accounts = new AccountsPage(true) { MaxHeight = Math.Max(200, root.Size.Height - 100) };
        popup.Child = new Border { Width = 330, Padding = new Thickness(16), Background = Ui.Brush("KilnPanel"), BorderBrush = Ui.Brush("KilnEdge"), BorderThickness = new Thickness(1.5), Child = accounts };
        popup.HorizontalOffset = Math.Clamp(origin.X + anchor.ActualWidth - 330, 12, Math.Max(12, root.Size.Width - 342));
        popup.VerticalOffset = Math.Clamp(origin.Y + 6, 12, Math.Max(12, root.Size.Height - 520));
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_accountPopup, popup)) _accountPopup = null;
            popup.DispatcherQueue.TryEnqueue(() => popup.Child = null);
            AccountsUpdated?.Invoke();
        };
        _accountPopup = popup; popup.IsOpen = true;
    }

    public void ShowInstance(McInstance instance)
    {
        SetCaptionBackdrop();
        _detailInstance = instance;
        if (!IsDiagnostics) LauncherSettings.Shared.SelectedInstance = instance.Name;
        RootFrame.Content = new Border { Padding = new Thickness(28, 48, 28, 24), Child = new InstanceDetailPage(instance) };
    }

    public void ShowGameLog(GameProcess game)
    {
        SetCaptionBackdrop();
        RootFrame.Content = new Border { Padding = new Thickness(28, 48, 28, 24), Child = new GameLogPage(game) };
    }

    internal void ShowDiagnostics(UIElement content) => RootFrame.Content = content;

    public MainWindow(bool diagnostics = false)
    {
        Current = this;
        _ = LauncherSettings.Shared; // Load the saved language before constructing any labels.
        InitializeComponent();
        Ui.ApplyTheme(Shell.ActualTheme);
        IsDiagnostics = diagnostics;
        try { _taskbar = new TaskbarProgress(WinRT.Interop.WindowNative.GetWindowHandle(this)); }
        catch (Exception e) { Log.log("[Taskbar] " + e.Friendly()); }
        if (diagnostics) SetDiagnosticsRunning(true);
        Shell.PreviewKeyDown += (_, args) => { if (_diagnosticsRunning) args.Handled = true; };
        ContentRegion.Children.Insert(0, new DotGridControl());
        ContentRegion.Children.Add(_hints);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        ApplyCaptionContrast();
        Shell.ActualThemeChanged += (_, _) => { Ui.ApplyTheme(Shell.ActualTheme); ApplyCaptionContrast(); };
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        Title = "Kiln";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1040, 680));
        Shell.Loaded += (_, _) =>
        {
            ApplyWindowPreset(LauncherSettings.Shared.LauncherWindowPreset);
        };
        ContentRegion.SizeChanged += (_, args) => ContentRegion.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, args.NewSize.Width, args.NewSize.Height),
        };
        BuildNavigation();
        _jobsTimer.Tick += (_, _) => RefreshJobIndicator();
        Shell.Loaded += (_, _) => _jobsTimer.Start();
        Closed += (_, _) => { _jobsTimer.Stop(); _taskbar?.Dispose(); };
        for (var i = 0; i < 9; i++)
        {
            var index = i;
            var key = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
            {
                Key = (Windows.System.VirtualKey)((int)Windows.System.VirtualKey.Number1 + i),
                Modifiers = Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Menu,
            };
            key.Invoked += (_, args) => { if (!_diagnosticsRunning && !KilnDialog.IsOpen) { AccountStore.Shared.SelectAt(index); ReloadCurrentPage(); } args.Handled = true; };
            Shell.KeyboardAccelerators.Add(key);
        }
        if (!diagnostics) NavigateTo("home");
    }

    private void BuildNavigation()
    {
        NavigationItems.Children.Clear(); FooterItems.Children.Clear(); _navigation.Clear();
        AddNavigation("home", Localization.T("开始", "Play"), "\uE768");
        AddNavigation("instances", Localization.T("版本", "Instances"), "\uE80A");
        AddNavigation("download", Localization.T("下载", "Download"), "\uE896");
        AddNavigation("setup", Localization.T("设置", "Settings"), "\uE713");
        AddNavigation("jobs", Localization.T("任务", "Tasks"), "\uE9F9", true);
        _jobsIdleContent = _navigation["jobs"].Content;
        _jobsProgressFill = new Border { Name = "SidebarJobFill", Height = 3, Width = 0, Background = Ui.Brush("KilnAccent"), HorizontalAlignment = HorizontalAlignment.Left };
        var track = new Grid { Width = 34, Height = 3, Background = Ui.Brush("KilnStroke"), HorizontalAlignment = HorizontalAlignment.Center }; track.Children.Add(_jobsProgressFill);
        _jobsProgressLabel = Ui.Text("", 9.5); _jobsProgressLabel.HorizontalAlignment = HorizontalAlignment.Center; _jobsProgressLabel.Foreground = Ui.Brush("KilnTextSecondary");
        _jobsProgressContent = Ui.Col(track, _jobsProgressLabel); _jobsProgressContent.Spacing = 4; _jobsProgressContent.VerticalAlignment = VerticalAlignment.Center;
        RefreshJobIndicator();
        AddNavigation("about", Localization.T("关于", "About"), "\uE946", true);
    }

    private void AddNavigation(string tag, string title, string glyph, bool footer = false)
    {
        var label = Ui.Col(new FontIcon { Glyph = glyph, FontSize = 15 }, Ui.Text(title, 10));
        label.Spacing = 5;
        foreach (FrameworkElement child in label.Children) child.HorizontalAlignment = HorizontalAlignment.Center;
        var button = new Button
        {
            Content = label, Width = 76, Height = 50, Padding = new Thickness(0),
            Style = (Style)Application.Current.Resources["KilnNavigationButton"],
        };
        button.Click += (_, _) => { if (!_diagnosticsRunning) NavigateTo(tag); };
        _navigation[tag] = button;
        (footer ? FooterItems : NavigationItems).Children.Add(button);
    }

    public static Job NewJob(string title, bool silent = false)
    {
        var job = new Job(title, silent); job.StateChanged += JobStateChanged;
        lock (JobsGate) Jobs.Add(job);
        Current?.DispatcherQueue.TryEnqueue(() => Current?.RefreshJobIndicator());
        return job;
    }

    private static void JobStateChanged(Job job, Job.JobState state)
    {
        if (state == Job.JobState.Running) return;
        job.StateChanged -= JobStateChanged;
        Current?.DispatcherQueue.TryEnqueue(() => Current?.CompleteJob(job));
    }

    private void CompleteJob(Job job)
    {
        job.Stage = job.State switch { Job.JobState.Success => Localization.T("完成", "Done"), Job.JobState.Cancelled => Localization.T("已取消", "Cancelled"), _ => Localization.T("失败", "Failed") };
        RefreshJobIndicator();
        var feedback = JobFeedback.From(job); JobCompleted?.Invoke(job, feedback);
        if (IsDiagnostics || feedback == null) return;
        Hint.Show(feedback.Hint, feedback.Kind); SystemIntegration.Notify(feedback.Title, feedback.Body, "tasks");
    }

    public static IReadOnlyList<Job> AllJobs
    {
        get { lock (JobsGate) return Jobs.ToList(); }
    }

    public static void ClearFinishedJobs()
    {
        lock (JobsGate) Jobs.RemoveAll(job => job.State != Job.JobState.Running);
    }

    public void NavigateTo(string tag)
    {
        if (tag == "import") { _ = ImportPage.ShowDialog(Shell.XamlRoot); return; }
        SetCaptionBackdrop();
        _detailInstance = null;
        _currentPage = tag;
        foreach (var (key, button) in _navigation)
        {
            button.BorderThickness = new Thickness(key == tag ? 3 : 0, 0, 0, 0);
            button.BorderBrush = Ui.Brush("KilnAccent");
            button.Foreground = Ui.Brush(key == tag ? "KilnText" : "KilnTextSecondary");
        }
        // 每次进入页面重新构建，保证数据刷新（与原版各页 onAppear 重新加载一致）
        RootFrame.Content = tag switch
        {
            "download" => new DownloadPage(),
            "mods" => new DownloadPage(Modrinth.ProjectType.Mod),
            "resourcepacks" => new DownloadPage(Modrinth.ProjectType.ResourcePack),
            "shaders" => new DownloadPage(Modrinth.ProjectType.Shader),
            "modpacks" => new DownloadPage(Modrinth.ProjectType.Modpack),
            "instances" => new InstancesPage(),
            "accounts" => new AccountsPage(),
            "setup" => new SetupPage(),
            "about" => new AboutPage(),
            "jobs" => new JobsPage(),
            _ => new HomePage(),
        };
        if (RootFrame.Content is Page page && tag != "home")
            RootFrame.Content = new Border { Padding = new Thickness(28, 48, 28, 24), Child = page };
    }

    public void ReloadCurrentPage()
    {
        NavigateTo(_currentPage);
    }

    public void RefreshLanguage()
    {
        BuildNavigation();
        if (_detailInstance != null) ShowInstance(_detailInstance);
        else ReloadCurrentPage();
    }
}

