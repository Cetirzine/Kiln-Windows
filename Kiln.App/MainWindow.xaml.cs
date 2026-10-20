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

    public MainWindow()
    {
        Current = this;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        Title = "Kiln";
        RootFrame.Content = new HomePage();
        Nav.SelectedItem = Nav.MenuItems[0];
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

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag) return;
        NavigateTo(tag);
    }

    public void NavigateTo(string tag)
    {
        // 每次进入页面重新构建，保证数据刷新（与原版各页 onAppear 重新加载一致）
        RootFrame.Content = tag switch
        {
            "download" => new DownloadPage(),
            "instances" => new InstancesPage(),
            "accounts" => new AccountsPage(),
            "setup" => new SetupPage(),
            "about" => new AboutPage(),
            "jobs" => new JobsPage(),
            _ => new HomePage(),
        };
    }

    public void ReloadCurrentPage()
    {
        var tag = (Nav.SelectedItem as NavigationViewItem)?.Tag as string ?? "home";
        NavigateTo(tag);
    }
}
