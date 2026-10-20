using Kiln.App.Pages;
using Kiln.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Kiln.App;

/// <summary>主窗口：侧边栏导航（对应原版 MainView），底部有下载管理</summary>
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
        RootFrame.Navigate(typeof(HomePage));
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
        var page = tag switch
        {
            "download" => typeof(DownloadPage),
            "instances" => typeof(InstancesPage),
            "accounts" => typeof(AccountsPage),
            "setup" => typeof(SetupPage),
            "about" => typeof(AboutPage),
            "jobs" => typeof(JobsPage),
            _ => typeof(HomePage),
        };
        RootFrame.Navigate(page);
    }

    public void ReloadCurrentPage()
    {
        var tag = (Nav.SelectedItem as NavigationViewItem)?.Tag as string ?? "home";
        RootFrame.BackStack.Clear();
        NavigateTo(tag);
    }
}

