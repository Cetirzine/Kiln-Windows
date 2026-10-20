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

    public void AccountChanged() => AccountsUpdated?.Invoke();
    public void ApplyWindowPreset(int preset)
    {
        preset = Math.Clamp(preset, 0, 2);
        LauncherSettings.Shared.LauncherWindowPreset = preset;
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
        popup.Closed += (_, _) => { _accountPopup = null; AccountsUpdated?.Invoke(); };
        _accountPopup = popup; popup.IsOpen = true;
    }

    public void ShowInstance(McInstance instance)
    {
        _detailInstance = instance;
        LauncherSettings.Shared.SelectedInstance = instance.Name;
        RootFrame.Content = new Border { Padding = new Thickness(28, 48, 28, 24), Child = new InstanceDetailPage(instance) };
    }

    public void ShowGameLog(GameProcess game)
    {
        RootFrame.Content = new Border { Padding = new Thickness(28, 48, 28, 24), Child = new GameLogPage(game) };
    }

    public MainWindow()
    {
        Current = this;
        _ = LauncherSettings.Shared; // Load the saved language before constructing any labels.
        InitializeComponent();
        ContentRegion.Children.Insert(0, new DotGridControl());
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        var caption = AppWindow.TitleBar;
        caption.ButtonBackgroundColor = Microsoft.UI.Colors.White;
        caption.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.White;
        caption.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 30, 30, 29);
        caption.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 98, 98, 94);
        caption.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 227, 227, 224);
        caption.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 30, 30, 29);
        caption.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 210, 210, 206);
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
        for (var i = 0; i < 9; i++)
        {
            var index = i;
            var key = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
            {
                Key = (Windows.System.VirtualKey)((int)Windows.System.VirtualKey.Number1 + i),
                Modifiers = Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Menu,
            };
            key.Invoked += (_, args) => { AccountStore.Shared.SelectAt(index); ReloadCurrentPage(); args.Handled = true; };
            Shell.KeyboardAccelerators.Add(key);
        }
        NavigateTo("home");
    }

    private void BuildNavigation()
    {
        NavigationItems.Children.Clear(); FooterItems.Children.Clear(); _navigation.Clear();
        AddNavigation("home", Localization.T("开始", "Play"), "\uE768");
        AddNavigation("instances", Localization.T("版本", "Instances"), "\uE80A");
        AddNavigation("download", Localization.T("下载", "Download"), "\uE896");
        AddNavigation("setup", Localization.T("设置", "Settings"), "\uE713");
        AddNavigation("jobs", Localization.T("任务", "Tasks"), "\uE9F9", true);
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
        button.Click += (_, _) => NavigateTo(tag);
        _navigation[tag] = button;
        (footer ? FooterItems : NavigationItems).Children.Add(button);
    }

    public static Job NewJob(string title)
    {
        var job = new Job(title);
        lock (JobsGate) Jobs.Add(job);
        return job;
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
            "import" => new ImportPage(),
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

