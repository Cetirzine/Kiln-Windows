using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

/// 下载管理：所有后台任务的进度
public sealed class JobsPage : Page
{
    private readonly StackPanel _list = new() { Spacing = 10 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Dictionary<Job, TaskRow> _rows = new();
    private readonly TextBlock _empty = Ui.Text(Localization.T("没有正在进行的任务。", "No running tasks."));
    private readonly Button _clear;

    public JobsPage()
    {
        _clear = Ui.Button(Localization.T("清除已结束的任务", "Clear finished tasks"), (_, _) => { MainWindow.ClearFinishedJobs(); Reload(); });
        var root = new Grid { RowSpacing = 14 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(Ui.Header(Localization.T("任务", "Tasks"), _clear));
        var scroll = new ScrollViewer { Content = Ui.Col(_empty, _list), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        Content = root;
        _timer.Tick += (_, _) => Reload();
        Loaded += (_, _) => { _timer.Start(); Reload(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    private void Reload()
    {
        var jobs = MainWindow.AllJobs;
        foreach (var old in _rows.Keys.Where(job => !jobs.Contains(job)).ToArray()) { _list.Children.Remove(_rows[old]); _rows.Remove(old); }
        foreach (var job in jobs)
        {
            if (!_rows.TryGetValue(job, out var row)) { row = new TaskRow(job); _rows[job] = row; _list.Children.Insert(0, row); }
            row.Refresh();
        }
        _empty.Visibility = jobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _clear.IsEnabled = jobs.Any(job => job.State != Job.JobState.Running);
    }

    private sealed class TaskRow : UserControl
    {
        private readonly Job _job;
        private readonly TextBlock _status = Ui.Text("", 11.5);
        private readonly TextBlock _detail = Ui.Text("", 11.5);
        private readonly TextBlock _percent = Ui.Text("", 12, FontWeights.SemiBold);
        private readonly FontIcon _icon = new() { FontSize = 18 };
        private readonly ProgressBar _progress = Ui.Progress();
        private readonly Button _cancel;
        public TaskRow(Job job)
        {
            _job = job;
            var grid = new Grid { ColumnSpacing = 12 }; grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.Children.Add(_icon);
            var heading = new Grid(); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.Children.Add(Ui.Text(job.Title, 13, FontWeights.SemiBold)); Grid.SetColumn(_percent, 1); heading.Children.Add(_percent);
            var information = new Grid { ColumnSpacing = 10 }; information.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); information.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); _status.MaxLines = 2; information.Children.Add(_status); Grid.SetColumn(_detail, 1); information.Children.Add(_detail);
            var body = Ui.Col(heading, _progress, information); body.Spacing = 5; Grid.SetColumn(body, 1); grid.Children.Add(body);
            _cancel = Ui.Button("×", (sender, _) => { job.Cancel(); ((Button)sender).IsEnabled = false; }); _cancel.Width = 28; _cancel.Padding = new Thickness(0); ToolTipService.SetToolTip(_cancel, Localization.T("取消", "Cancel")); Grid.SetColumn(_cancel, 2); grid.Children.Add(_cancel);
            Content = Ui.Card("", grid);
        }
        public void Refresh()
        {
            var running = _job.State == Job.JobState.Running;
            _icon.Glyph = _job.State switch { Job.JobState.Success => "\uE73E", Job.JobState.Failed => "\uE7BA", Job.JobState.Cancelled => "\uE711", _ => "\uE896" };
            _icon.Foreground = Ui.Brush(running ? "KilnAccent" : "KilnTextSecondary");
            _status.Text = _job.State switch { Job.JobState.Failed => _job.Error ?? Localization.T("失败", "Failed"), Job.JobState.Cancelled => Localization.T("已取消", "Cancelled"), Job.JobState.Success => Localization.T("已完成", "Completed"), _ => _job.Stage };
            _status.Foreground = _detail.Foreground = Ui.Brush("KilnTextSecondary");
            _detail.Text = running ? _job.FileText + (_job.Speed > 0 ? "  " + Job.FormatSpeed(_job.Speed) : "") : "";
            _percent.Text = running ? $"{_job.Fraction:P0}" : "";
            _progress.Value = _job.Fraction; _progress.Visibility = _cancel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        }
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
            var dialog = new KilnDialog
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

