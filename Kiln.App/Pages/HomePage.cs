using Kiln.App.DotScene;
using Kiln.Core.DotScene;
using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kiln.App.Pages;

/// 主页：当前版本的横版像素场景、物品栏式版本切换、开始游戏与账号选择（对应原版 HomePage）
public sealed class HomePage : Page
{
    private readonly DotSceneControl _scene = new() { Height = 352, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ListView _hotbar = new()
    {
        SelectionMode = ListViewSelectionMode.Single,
        MinHeight = 84,
    };
    private bool _hotbarWired;
    private readonly ComboBox _accountBox = new() { MinWidth = 220 };
    private readonly TextBlock _desc = Ui.Text("", 14);
    private readonly TextBlock _playTime = Ui.Text("", 13);
    private readonly Button _playButton = new() { MinHeight = 48, MinWidth = 220 };
    private readonly ProgressBar _progress = Ui.Progress();
    private readonly TextBlock _progressText = Ui.Text("", 13);

    private bool _accountBoxWired;
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
        _hotbar.Items.Clear();
        foreach (var (index, i) in _instances.Select((inst, idx) => (idx, inst)))
        {
            var block = BlockIconControl.For(i.Icon, 44);
            var cell = new StackPanel { Spacing = 2, Margin = new Thickness(4) };
            var slot = new Grid { MinWidth = 68, MinHeight = 68, Padding = new Thickness(6) };
            slot.Children.Add(block);
            if (index < 9)
            {
                var num = Ui.Text((index + 1).ToString(), 10);
                num.VerticalAlignment = VerticalAlignment.Top;
                num.HorizontalAlignment = HorizontalAlignment.Left;
                slot.Children.Add(num);
            }
            cell.Children.Add(slot);
            cell.Children.Add(Ui.Text(TrimName(i.Name), 11));
            ToolTipService.SetToolTip(cell, $"{i.Name} — {i.Description}");
            _hotbar.Items.Add(cell);
        }
        var selectedName = LauncherSettings.Shared.SelectedInstance;
        var selected = _instances.FirstOrDefault(i => i.Name == selectedName) ?? _instances.FirstOrDefault();
        var selIndex = _instances.IndexOf(selected!);
        if (!_hotbarWired)
        {
            _hotbarWired = true;
            _hotbar.SelectionChanged += (_, _) => { UpdateInfo(); LoadCast(); };
        }
        _hotbar.SelectedIndex = selIndex;
        _accounts = AccountStore.Shared.Accounts;
        _accountBox.Items.Clear();
        foreach (var a in _accounts) _accountBox.Items.Add($"{a.Name}（{a.TypeText}）");
        var sel = AccountStore.Shared.Selected;
        if (!_accountBoxWired)
        {
            _accountBoxWired = true;
            _accountBox.SelectionChanged += (_, _) =>
            {
                if (_accountBox.SelectedIndex >= 0 && _accountBox.SelectedIndex < _accounts.Count)
                    AccountStore.Shared.Select(_accounts[_accountBox.SelectedIndex]);
                UpdateInfo();
            };
        }
        _accountBox.SelectedIndex = sel == null ? -1 : _accounts.IndexOf(sel);
        UpdateInfo();
        LoadCast();
    }

    private static string TrimName(string name) => name.Length > 14 ? name[..13] + "…" : name;

    /// 场景人物：登录的玩家用他自己的皮肤（离线皮肤 / 默认外观）
    private void LoadCast()
    {
        var inst = Current;
        _scene.Scene = DotScenes.SceneFor(inst?.VanillaVersion ?? "");
        var look = LoadPlayerSkin();
        _scene.Cast = new SceneCast { Player = look, Author = PlayerLook.Alex };
    }

    private PlayerLook LoadPlayerSkin()
    {
        try
        {
            var account = AccountStore.Shared.Selected;
            if (account?.Skin is { IsEmpty: false } skin)
            {
                var (path, slim) = (skin.File, skin.Slim);
                if (skin.Premium is { } premium)
                {
                    var t = Skins.Premium(premium).GetAwaiter().GetResult();
                    path = Path.GetFileName(t.Url);
                    slim = t.Slim;
                }
                if (path != null && File.Exists(Skins.SkinPath(path)))
                {
                    var look = DecodeSkin(Skins.SkinPath(path), slim);
                    if (look != null) return look;
                }
            }
        }
        catch { }
        return PlayerLook.Steve;
    }

    internal static PlayerLook? DecodeSkin(string path, bool slim)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return DecodeSkin(fs, slim);
        }
        catch { return null; }
    }

    internal static PlayerLook? DecodeSkin(Stream stream, bool slim)
    {
        try
        {
            var decoder = Task.Run(() => Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream.AsRandomAccessStream()).AsTask()).Result;
            var pixels = Task.Run(async () =>
            {
                var data = await decoder.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                    Windows.Graphics.Imaging.BitmapAlphaMode.Straight,
                    new Windows.Graphics.Imaging.BitmapTransform(), Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation, 0);
                return data.DetachPixelData();
            }).Result;
            return PlayerLook.FromSkin(pixels!, (int)decoder.PixelWidth, (int)decoder.PixelHeight, slim);
        }
        catch { return null; }
    }

    private McInstance? Current =>
        _hotbar.SelectedIndex >= 0 && _hotbar.SelectedIndex < _instances.Count
            ? _instances[_hotbar.SelectedIndex]
            : null;

    private void UpdateInfo()
    {
        var inst = Current;
        if (inst != null && LauncherSettings.Shared.SelectedInstance != inst.Name)
            LauncherSettings.Shared.SelectedInstance = inst.Name;
        _desc.Text = inst?.Description ?? Localization.T("还没有版本，去“下载”安装一个吧", "No instances yet — install one in Download");
        var setup = inst?.Setup;
        _playTime.Text = McInstance.FormatPlayTime(setup?.PlayTime) is { } pt
            ? Localization.T($"已游玩 {pt}", $"Played {pt}") : "";
        _playButton.IsEnabled = inst != null && _accountBox.SelectedIndex >= 0;
    }

    private void BuildUi()
    {
        // 物品栏式横向列表
        _hotbar.ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
            "<ItemsStackPanel Orientation='Horizontal'/></ItemsPanelTemplate>");
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(_scene);
        // 物品栏式版本切换（按 1–9）
        var hotbarBorder = new Border
        {
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 2, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = _hotbar,
        };
        panel.Children.Add(hotbarBorder);
        panel.Children.Add(Ui.Row(Ui.Text(Localization.T("账号", "Account"), 14), _accountBox, _playTime));
        panel.Children.Add(_desc);
        _playButton.Content = Localization.T("开始游戏", "Play");
        _playButton.Click += async (_, _) => await Play();
        _progress.Visibility = Visibility.Collapsed;
        _progressText.Visibility = Visibility.Collapsed;
        panel.Children.Add(_playButton);
        panel.Children.Add(_progress);
        panel.Children.Add(_progressText);

        // 数字键 1–9 切换版本
        Loaded += (_, _) =>
        {
            for (var i = 0; i < 9; i++)
            {
                var idx = i;
                var accelerator = new KeyboardAccelerator
                {
                    Key = (Windows.System.VirtualKey)((int)Windows.System.VirtualKey.Number1 + idx),
                    Modifiers = Windows.System.VirtualKeyModifiers.None,
                };
                accelerator.Invoked += (_, _) => DispatcherQueue.TryEnqueue(() =>
                {
                    if (idx < _hotbar.Items.Count) _hotbar.SelectedIndex = idx;
                });
                KeyboardAccelerators.Add(accelerator);
            }
        };

        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
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
                        SystemIntegration.Notify("Kiln", Localization.T($"{inst.Name} 崩溃了", $"{inst.Name} crashed"));
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




