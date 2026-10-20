using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

/// 主页：当前版本、开始游戏与账号选择（对应原版 HomePage；像素场景待复刻）
public sealed class HomePage : Page
{
    private readonly ComboBox _versionBox = new() { MinWidth = 300 };
    private readonly ComboBox _accountBox = new() { MinWidth = 220 };
    private readonly TextBlock _desc = Ui.Text("", 14);
    private readonly TextBlock _playTime = Ui.Text("", 13);
    private readonly Button _playButton = new() { MinHeight = 48, MinWidth = 200 };
    private readonly ProgressBar _progress = Ui.Progress();
    private readonly TextBlock _progressText = Ui.Text("", 13);

    private List<McInstance> _instances = new();
    private List<Account> _accounts = new();

    public HomePage()
    {
        BuildUi();
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        _instances = InstanceScanner.Scan(LauncherSettings.Shared.MinecraftFolder).ToList();
        _versionBox.Items.Clear();
        foreach (var i in _instances) _versionBox.Items.Add(i.Name);
        var selectedName = LauncherSettings.Shared.SelectedInstance;
        var selected = _instances.FirstOrDefault(i => i.Name == selectedName) ?? _instances.FirstOrDefault();
        _versionBox.SelectedIndex = _instances.IndexOf(selected!);
        _accounts = AccountStore.Shared.Accounts;
        _accountBox.Items.Clear();
        foreach (var a in _accounts) _accountBox.Items.Add($"{a.Name}（{a.TypeText}）");
        var sel = AccountStore.Shared.Selected;
        _accountBox.SelectedIndex = sel == null ? -1 : _accounts.IndexOf(sel);
        UpdateInfo();
    }

    private McInstance? Current =>
        _versionBox.SelectedIndex >= 0 && _versionBox.SelectedIndex < _instances.Count
            ? _instances[_versionBox.SelectedIndex]
            : null;

    private void UpdateInfo()
    {
        var inst = Current;
        _desc.Text = inst?.Description ?? Localization.T("还没有版本，去“下载”安装一个吧", "No instances yet — install one in Download");
        var setup = inst?.Setup;
        _playTime.Text = McInstance.FormatPlayTime(setup?.PlayTime) is { } pt
            ? Localization.T($"已游玩 {pt}", $"Played {pt}") : "";
        _playButton.IsEnabled = inst != null && _accountBox.SelectedIndex >= 0;
    }

    private void BuildUi()
    {
        var title = Ui.Text("Kiln", 40, FontWeights.Bold);
        _versionBox.SelectionChanged += (_, _) => { UpdateInfo(); };
        _accountBox.SelectionChanged += (_, _) =>
        {
            if (_accountBox.SelectedIndex >= 0 && _accountBox.SelectedIndex < _accounts.Count)
                AccountStore.Shared.Select(_accounts[_accountBox.SelectedIndex]);
            UpdateInfo();
        };
        _playButton.Content = Localization.T("开始游戏", "Play");
        _playButton.Click += async (_, _) => await Play();
        _progress.Visibility = Visibility.Collapsed;
        _progressText.Visibility = Visibility.Collapsed;

        var panel = new StackPanel { Spacing = 16, Margin = new Thickness(0, 24, 0, 0) };
        panel.Children.Add(title);
        panel.Children.Add(Ui.Row(Ui.Text(Localization.T("版本", "Instance"), 14), _versionBox,
            Ui.Text(Localization.T("账号", "Account"), 14), _accountBox));
        panel.Children.Add(_desc);
        panel.Children.Add(_playTime);
        panel.Children.Add(_playButton);
        panel.Children.Add(_progress);
        panel.Children.Add(_progressText);
        panel.Children.Add(Ui.Text(
            Localization.T("游戏启动时可以继续使用启动器；游戏窗口出现后会按设置的可见性处理本窗口。", "Keep using Kiln while the game starts; the window follows your visibility setting."),
            12));
        Content = new ScrollViewer { Content = panel };
    }

    private async Task Play()
    {
        var inst = Current;
        var account = _accountBox.SelectedIndex >= 0 ? _accounts[_accountBox.SelectedIndex] : null;
        if (inst == null || account == null) return;
        var job = MainWindow.NewJob(Localization.T($"启动 {inst.Name}", $"Play {inst.Name}"));
        _playButton.IsEnabled = false;
        _playButton.Content = Localization.T("启动中…", "Launching…");
        _progress.Visibility = Visibility.Visible;
        _progressText.Visibility = Visibility.Visible;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            _progress.Value = job.Fraction;
            _progressText.Text = $"{job.Stage}  {job.FileText}  {Job.FormatSpeed(job.Speed)}";
        };
        timer.Start();
        try
        {
            var game = await Launcher.Launch(inst, account, new LaunchOptions(), job, new LaunchCallbacks
            {
                OnWindow = g => DispatcherQueue.TryEnqueue(() =>
                {
                    timer.Stop();
                    _playButton.Content = Localization.T("运行中（点击结束）", "Running (click to stop)");
                    _playButton.IsEnabled = true;
                    _playButton.Click -= StopGame;
                    _playButton.Click += StopGame;
                    ApplyVisibility();
                    _currentGame = g;
                }),
                OnExit = g => DispatcherQueue.TryEnqueue(async () =>
                {
                    timer.Stop();
                    _progress.Value = 0;
                    _progress.Visibility = Visibility.Collapsed;
                    _progressText.Visibility = Visibility.Collapsed;
                    _playButton.Content = Localization.T("开始游戏", "Play");
                    _playButton.Click -= StopGame;
                    RestoreVisibility();
                    UpdateInfo();
                    if (g.State == GameProcess.GameState.Crashed)
                    {
                        var finding = CrashAnalyzer.Analyze(g.Lines, inst.GameDir);
                        await Ui.ShowError(XamlRoot,
                            Localization.T("游戏崩溃了。", "The game crashed.")
                            + (finding != null ? "\n" + finding.Reason + (finding.Detail != null ? $"（{finding.Detail}）" : "") : "")
                            + "\n\n" + string.Join("\n", g.Lines.TakeLast(10)));
                    }
                }),
            });
            if (game == null) throw new LauncherException("export only");
            LauncherSettings.Shared.SelectedInstance = inst.Name;
        }
        catch (Exception e)
        {
            timer.Stop();
            _progress.Visibility = Visibility.Collapsed;
            _progressText.Visibility = Visibility.Collapsed;
            _playButton.Content = Localization.T("开始游戏", "Play");
            _playButton.IsEnabled = true;
            await Ui.ShowError(XamlRoot, e);
        }
    }

    private GameProcess? _currentGame;

    private void StopGame(object sender, RoutedEventArgs e) => _currentGame?.Kill();

    private void ApplyVisibility()
    {
        // 游戏窗口出现后按设置处理启动器窗口
        switch (LauncherSettings.Shared.Visibility)
        {
            case VisibilityMode.CloseOnStart:
                Application.Current.Exit();
                break;
            case VisibilityMode.HideAndClose or VisibilityMode.HideAndReopen:
                App.MainAppWindow!.AppWindow.Hide();
                break;
            case VisibilityMode.Minimize:
                App.MainAppWindow!.AppWindow.Show(false);
                break;
        }
    }

    private void RestoreVisibility()
    {
        if (LauncherSettings.Shared.Visibility is VisibilityMode.HideAndReopen or VisibilityMode.HideAndClose or VisibilityMode.Minimize)
            App.MainAppWindow!.AppWindow.Show();
    }
}

