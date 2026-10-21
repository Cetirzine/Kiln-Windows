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
    private static string T(string zh, string en) => Localization.T(zh, en);
    private static Button Link(string title, string url)
    {
        var button = Ui.Button(title, (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(url)));
        button.Width = 100; button.VerticalAlignment = VerticalAlignment.Center;
        button.Content = new TextBlock { Text = title, TextWrapping = TextWrapping.NoWrap, FontSize = 12.5 };
        return button;
    }

    private static FrameworkElement Credit(string glyph, string title, string detail, Button? link = null)
    {
        var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 8, 10, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 18, Foreground = Ui.Brush("KilnTextSecondary"), VerticalAlignment = VerticalAlignment.Center });
        var info = Ui.Text(detail, 11.5); info.Foreground = Ui.Brush("KilnTextSecondary");
        var labels = Ui.Col(Ui.Text(title, 13, FontWeights.SemiBold), info); labels.Spacing = 4; labels.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(labels, 1); row.Children.Add(labels);
        if (link != null) { Grid.SetColumn(link, 2); row.Children.Add(link); }
        return row;
    }

    public AboutPage()
    {
        var app = new Grid { ColumnSpacing = 14 };
        app.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        app.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        app.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        app.Children.Add(new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/AppIcon.png")), Width = 48, Height = 48, VerticalAlignment = VerticalAlignment.Center });
        var description = Ui.Text(T("为 Windows 打造的 Minecraft: Java Edition 启动器", "A Minecraft: Java Edition launcher built for Windows"), 12); description.Foreground = Ui.Brush("KilnTextSecondary");
        var version = Ui.Text($"{AppInfo.Version} · Windows {Environment.OSVersion.Version} · {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}", 11); version.Foreground = Ui.Brush("KilnTextSecondary");
        var identity = Ui.Col(Ui.Text("Kiln", 18, FontWeights.Bold), description, version, Ui.Text(T($"作者：{AppInfo.Author}", $"By {AppInfo.Author}"), 12, FontWeights.Medium)); identity.Spacing = 3;
        Grid.SetColumn(identity, 1); app.Children.Add(identity);
        var repository = Link("GitHub", "https://github.com/Cetirzine/Kiln-Windows"); repository.Margin = new Thickness(0, 0, 10, 0); Grid.SetColumn(repository, 2); app.Children.Add(repository);
        var thanks = Ui.Col(
            Credit("\uE77B", T("龙腾猫跃", "LTCat (龙腾猫跃)"), T("Plain Craft Launcher 2（PCL2）的作者。本启动器的部分思路受 PCL2 启发。", "Author of Plain Craft Launcher 2 (PCL2). Some ideas in this launcher were inspired by PCL2."), Link(T("主页", "Homepage"), "https://space.bilibili.com/11343203")),
            Credit("\uE7F4", "Plain Craft Launcher 2", T("一款出色的 Minecraft 启动器。", "An excellent Minecraft launcher."), Link("GitHub", "https://github.com/Meloong-Git/PCL")),
            Credit("\uE7B8", "HMCL", T("Forge / NeoForge 安装流程与崩溃分析的实现思路参考了 HMCL。", "The Forge / NeoForge install flow and crash analysis follow approaches used by HMCL."), Link("GitHub", "https://github.com/HMCL-dev/HMCL")),
            Credit("\uE968", "BMCLAPI (bangbang93)", T("提供 Minecraft 镜像下载源。", "Provides the Minecraft download mirror."), Link(T("网站", "Website"), "https://bmclapi.bangbang93.com")),
            Credit("\uE721", "Modrinth", T("Mod、资源包与光影的搜索与下载。", "Search and downloads for mods, resource packs and shaders.")),
            Credit("\uECAD", T("CurseForge 与社区镜像", "CurseForge & community mirrors"), T("CurseForge 上的 Mod 与资源；Curse.tools 提供无需密钥的公共代理。", "Mods and resources on CurseForge; Curse.tools provides a public proxy that needs no API key."), Link("Curse.tools", "https://curse.tools")),
            Credit("\uE8D7", "MCIM（z0z0r4）", T("提供 Minecraft 资源的社区镜像。", "A community mirror for Minecraft resources."), Link("MCIM", "https://mod.mcimirror.top")),
            Credit("\uE72E", "authlib-injector（yushijinhun）", T("外置登录与离线皮肤；在需要时下载，不随应用分发。", "Third-party login and offline skins; downloaded when needed, not bundled."), Link("GitHub", "https://github.com/yushijinhun/authlib-injector")));
        thanks.Spacing = 0;
        var inspiration = Ui.Text(T("界面布局参考了 Lunar Client。", "The layout is inspired by Lunar Client."), 11.5); inspiration.Foreground = Ui.Brush("KilnTextSecondary");
        var legal = Ui.Text(T("Minecraft 是 Mojang Studios 的商标。本软件不是 Mojang 或 Microsoft 的官方产品。请购买正版游戏。", "Minecraft is a trademark of Mojang Studios. This software is not an official Mojang or Microsoft product. Please buy the game."), 12); legal.Foreground = Ui.Brush("KilnTextSecondary");
        var buy = Link(T("购买 Minecraft", "Buy Minecraft"), "https://www.minecraft.net/store/minecraft-java-bedrock-edition-pc"); buy.Width = 160; buy.HorizontalAlignment = HorizontalAlignment.Left;
        var panels = Ui.Col(Ui.Card("", app), Ui.Card(T("致谢", "Thanks"), thanks, inspiration), Ui.Card(T("法律信息", "Legal"), legal, buy, Ui.Text(T("许可：PolyForm Strict 1.0.0", "License: PolyForm Strict 1.0.0"), 11.5))); panels.Spacing = 14;
        var root = new Grid { RowSpacing = 14 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(Ui.Header(T("关于", "About"), Ui.Button(T("检查更新", "Check for updates"), async (_, _) => await CheckUpdate())));
        var scroll = Ui.Scroll(panels); Grid.SetRow(scroll, 1); root.Children.Add(scroll); Content = root;
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

