using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

/// 下载管理：所有后台任务的进度
public sealed class JobsPage : Page
{
    private readonly StackPanel _list = new() { Spacing = 10 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public JobsPage()
    {
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(0, 16, 0, 0) };
        panel.Children.Add(Ui.Text(Localization.T("下载管理", "Downloads"), 26, FontWeights.SemiBold));
        panel.Children.Add(_list);
        Content = new ScrollViewer { Content = panel };
        Loaded += (_, _) => { _timer.Tick += (_, _) => Reload(); _timer.Start(); Reload(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    private void Reload()
    {
        _list.Children.Clear();
        foreach (var job in MainWindow.AllJobs.AsEnumerable().Reverse())
        {
            var state = job.State switch
            {
                Job.JobState.Success => " ✓",
                Job.JobState.Failed => $" ✗ {job.Error}",
                Job.JobState.Cancelled => " ⊘",
                _ => "",
            };
            var line = Ui.Col(
                Ui.Text(job.Title + state, 14, FontWeights.SemiBold),
                Ui.Text($"{job.Stage}  {job.FileText}  {Job.FormatSpeed(job.Speed)}", 12));
            var progress = Ui.Progress((float)job.Fraction);
            var box = Ui.Col(line, progress);
            box.Margin = new Thickness(0, 6, 0, 6);
            _list.Children.Add(box);
        }
        if (MainWindow.AllJobs.Count == 0)
            _list.Children.Add(Ui.Text(Localization.T("没有正在进行的任务。", "No running tasks."), 13));
    }
}

/// 关于页
public sealed class AboutPage : Page
{
    public AboutPage()
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 24, 0, 0) };
        panel.Children.Add(Ui.Text("Kiln", 40, FontWeights.Bold));
        panel.Children.Add(Ui.Text($"Windows · {AppInfo.Version}", 14));
        panel.Children.Add(Ui.Text(Localization.T($"作者 {AppInfo.Author}，为 Windows 打造的原生 Minecraft 启动器（C# / WinUI 3 移植版）",
            $"By {AppInfo.Author}. A native Minecraft launcher for Windows (C# / WinUI 3 port)."), 13));
        panel.Children.Add(Ui.Row(
            Ui.Button("GitHub", (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/Cetirzine/kiln-launcher")))));
        panel.Children.Add(Ui.Button(Localization.T("检查更新", "Check for updates"), async (_, _) => await CheckUpdate()));
        panel.Children.Add(Ui.Text(Localization.T("许可：PolyForm Strict 1.0.0", "License: PolyForm Strict 1.0.0"), 12));
        Content = new ScrollViewer { Content = panel };
    }

    private async Task CheckUpdate()
    {
        try
        {
            var release = await Updater.Check();
            if (release == null)
            {
                await App.ShowInfo(App.MainAppWindow!, Localization.T("已是最新版本。", "Up to date."));
                return;
            }
            LauncherSettings.Shared.LastUpdateCheck = DateTime.UtcNow;
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Localization.T($"新版本 {release.Version}", $"Update {release.Version}"),
                Content = release.Notes,
                PrimaryButtonText = Localization.T("更新", "Update"),
                CloseButtonText = Localization.T("以后再说", "Later"),
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            var appDir = Updater.CurrentApp ?? throw new LauncherException(
                Localization.T("无法定位程序目录。", "Couldn't locate the app directory."));
            Updater.CheckReplaceable(appDir);
            var job = MainWindow.NewJob(Localization.T($"更新到 {release.Version}", $"Update to {release.Version}"));
            var staged = await Updater.Prepare(release, appDir, job);
            Updater.LaunchReplacement(staged, appDir);
            Application.Current.Exit();
        }
        catch (Exception e)
        {
            await Ui.ShowError(XamlRoot, e);
        }
    }
}

