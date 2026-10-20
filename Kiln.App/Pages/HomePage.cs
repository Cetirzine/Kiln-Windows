using Kiln.App.DotScene;
using Kiln.Core.DotScene;
using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace Kiln.App.Pages;

// Layout dimensions and HUD hierarchy follow SwiftUI HomePage.swift.
public sealed class HomePage : Page
{
    private readonly DotSceneControl _scene = new() { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly StackPanel _hotbar = new() { Orientation = Orientation.Horizontal, Spacing = 0 };
    private readonly TextBlock _title = Ui.Text("", 32, FontWeights.ExtraBold);
    private readonly TextBlock _desc = Ui.Text("", 12.5);
    private readonly TextBlock _slotName = Ui.Text(" ", 13, FontWeights.SemiBold);
    private readonly TextBlock _playLabel = Ui.Text("", 19, FontWeights.ExtraBold);
    private readonly TextBlock _playStatus = Ui.Text("", 11);
    private readonly Border _playFill = new() { Background = Solid(0x3D8B2F), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _playButton = new() { Width = 230, Height = 52, Padding = new Thickness(0) };
    private readonly Button _accountButton = Ui.AccountButton();
    private readonly Grid _notes = new() { ColumnSpacing = 20 };
    private readonly TextBlock _notesStatus = Ui.Text("", 12);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _nameTimer = new() { Interval = TimeSpan.FromMilliseconds(1600) };
    private readonly StackPanel _running = new() { Spacing = 6 };
    private List<McInstance> _instances = new();
    private McInstance? _selected;
    private bool _loaded;
    private Job? _launchJob { get => MainWindow.LaunchJob; set => MainWindow.LaunchJob = value; }
    private static PlayerLook? _author;
    private int _castRevision;

    public HomePage()
    {
        BuildUi();
        _timer.Tick += (_, _) => UpdateProgress();
        _nameTimer.Tick += (_, _) => { _slotName.Text = " "; _nameTimer.Stop(); };
        Loaded += async (_, _) =>
        {
            _loaded = true;
            MainWindow.Current!.AccountsUpdated += UpdateInfo;
            Reload();
            await BlockTextures.Shared.Load(InstanceScanner.Scan(LauncherSettings.Shared.MinecraftFolder));
            _timer.Start();
            await LoadNotes();
        };
        Unloaded += (_, _) => { MainWindow.Current!.AccountsUpdated -= UpdateInfo; _loaded = false; _timer.Stop(); _nameTimer.Stop(); _castRevision++; };
    }

    private static SolidColorBrush Solid(uint rgb, byte alpha = 255) =>
        new(Color.FromArgb(alpha, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    private void Reload()
    {
        var all = InstanceScanner.Scan(LauncherSettings.Shared.MinecraftFolder).Where(i => i.Kind != McInstance.Category.Error).ToList();
        _selected = all.FirstOrDefault(i => i.Name == LauncherSettings.Shared.SelectedInstance) ?? all.FirstOrDefault();
        _instances = all.OrderByDescending(i => i.Setup.LastPlayed ?? DateTime.MinValue).Take(9).ToList();
        if (_selected != null && !_instances.Any(i => i.Name == _selected.Name))
        {
            if (_instances.Count == 9) _instances.RemoveAt(8);
            _instances.Insert(0, _selected);
        }
        UpdateInfo();
    }

    private void BuildUi()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 330 });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = 238 });
        var hero = new Grid { Background = Solid(0x2B2219) };
        hero.Children.Add(_scene);
        var shade = new Border
        {
            IsHitTestVisible = false,
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1),
                GradientStops = { new GradientStop { Color = Color.FromArgb(0, 0, 0, 0), Offset = .35 }, new GradientStop { Color = Color.FromArgb(158, 0, 0, 0), Offset = 1 } },
            },
        };
        hero.Children.Add(shade);
        _title.Foreground = _desc.Foreground = _slotName.Foreground = Solid(0xFFFFFF);
        _desc.Opacity = .78;
        _title.TextWrapping = TextWrapping.NoWrap;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        var hotbarFrame = new Border { Padding = new Thickness(2), Background = Solid(0, 128), BorderBrush = Solid(0xFFFFFF, 56), BorderThickness = new Thickness(1), Child = _hotbar, HorizontalAlignment = HorizontalAlignment.Left };
        var info = Ui.Col(_title, _desc, _slotName, hotbarFrame);
        info.Spacing = 6;
        var bottom = new Grid { Margin = new Thickness(28, 0, 28, 24), VerticalAlignment = VerticalAlignment.Bottom, ColumnSpacing = 24 };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.Children.Add(info);
        var settings = Ui.Button("\uE713", (_, _) => { if (_selected != null) MainWindow.Current!.ShowInstance(_selected); });
        settings.FontFamily = new FontFamily("Segoe Fluent Icons");
        settings.Width = 52; settings.Height = 52; settings.Foreground = Solid(0xFFFFFF); settings.Background = Solid(0, 115);
        ToolTipService.SetToolTip(settings, Localization.T("版本设置", "Instance settings"));
        var playContent = new Grid { Width = 230, Height = 52, Background = Solid(0x2B6620) };
        playContent.Children.Add(_playFill);
        _playLabel.Foreground = _playStatus.Foreground = Solid(0xFFFFFF);
        _playLabel.HorizontalAlignment = _playStatus.HorizontalAlignment = HorizontalAlignment.Center;
        var playText = Ui.Col(_playLabel, _playStatus); playText.Spacing = 2; playText.VerticalAlignment = VerticalAlignment.Center;
        playContent.Children.Add(playText);
        playContent.Children.Add(new Border { Height = 3, Background = Solid(0, 71), VerticalAlignment = VerticalAlignment.Bottom });
        _playButton.Content = playContent;
        _playButton.Style = (Style)Application.Current.Resources["KilnNavigationButton"];
        _playButton.Click += async (_, _) =>
        {
            if (_launchJob != null) { _launchJob.Cancel(); return; }
            if (_selected == null) { MainWindow.Current!.NavigateTo("download"); return; }
            await Play(new LaunchOptions());
        };
        var menu = new MenuFlyout();
        var demo = new MenuFlyoutItem { Text = Localization.T("以试玩模式启动", "Play the demo") };
        demo.Click += async (_, _) => await Play(new LaunchOptions { Demo = true });
        var export = new MenuFlyoutItem { Text = Localization.T("导出启动脚本…", "Export launch script…") };
        export.Click += async (_, _) => await ExportScript();
        menu.Items.Add(export); menu.Items.Add(demo); _playButton.ContextFlyout = menu;
        var actions = Ui.Row(settings, _playButton); actions.Spacing = 8; actions.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(actions, 1); bottom.Children.Add(actions); hero.Children.Add(bottom);
        _accountButton.Margin = new Thickness(0, 48, 28, 0);
        _accountButton.HorizontalAlignment = HorizontalAlignment.Right;
        _accountButton.VerticalAlignment = VerticalAlignment.Top;
        hero.Children.Add(_accountButton);
        root.Children.Add(hero);
        _running.Margin = new Thickness(28, 8, 28, 0); Grid.SetRow(_running, 1); root.Children.Add(_running);
        for (var i = 0; i < 4; i++) _notes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var noteHeader = Ui.Row(Ui.Text(Localization.T("Minecraft 更新", "Minecraft updates"), 13, FontWeights.SemiBold), _notesStatus);
        var notePanel = Ui.Col(noteHeader, _notes); notePanel.Spacing = 12; notePanel.Margin = new Thickness(28, 18, 28, 24);
        Grid.SetRow(notePanel, 2); root.Children.Add(notePanel);
        for (var i = 0; i < 9; i++)
        {
            var index = i;
            var key = new KeyboardAccelerator { Key = (Windows.System.VirtualKey)((int)Windows.System.VirtualKey.Number1 + i) };
            key.Invoked += (_, args) => { if (index < _instances.Count) Pick(_instances[index]); args.Handled = true; };
            KeyboardAccelerators.Add(key);
        }
        var enter = new KeyboardAccelerator { Key = Windows.System.VirtualKey.Enter };
        enter.Invoked += async (_, args) => { args.Handled = true; if (_launchJob != null) _launchJob.Cancel(); else if (_selected == null) MainWindow.Current!.NavigateTo("download"); else await Play(new LaunchOptions()); };
        KeyboardAccelerators.Add(enter);
        var scroll = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.SizeChanged += (_, args) => root.MinHeight = args.NewSize.Height;
        Content = scroll;
    }

    private void Pick(McInstance instance)
    {
        _selected = instance;
        LauncherSettings.Shared.SelectedInstance = instance.Name;
        UpdateInfo();
        _slotName.Text = instance.Name;
        _nameTimer.Stop(); _nameTimer.Start();
    }

    private void UpdateInfo()
    {
        var inst = _selected;
        _title.Text = inst?.Name ?? Localization.T("还没有安装游戏", "No game installed yet");
        _desc.Text = inst?.Description ?? Localization.T("先下载一个版本，安装完成后会出现在这里。", "Download a version first; it will show up here once installed.");
        _scene.Scene = inst == null ? null : DotScenes.SceneFor(inst.VanillaVersion);
        _hotbar.Children.Clear();
        for (var n = 0; n < 9; n++)
        {
            var instance = n < _instances.Count ? _instances[n] : null;
            var selected = instance != null && instance.Name == inst?.Name;
            var content = new Grid { Width = 42, Height = 42, Background = Solid(0xFFFFFF, 10) };
            if (instance != null) content.Children.Add(BlockIconControl.For(instance.Icon, 26));
            var num = Ui.Text((n + 1).ToString(), 8.5, FontWeights.Bold); num.Foreground = Solid(0xFFFFFF, 115); num.Margin = new Thickness(3); num.VerticalAlignment = VerticalAlignment.Top;
            content.Children.Add(num);
            var slot = new Button
            {
                Width = 42, Height = 42, Padding = new Thickness(0), Content = content,
                Style = (Style)Application.Current.Resources["KilnNavigationButton"],
                BorderBrush = Solid(0xFFFFFF, selected ? (byte)255 : (byte)31), BorderThickness = new Thickness(selected ? 2 : 1),
            };
            slot.Click += (_, _) => { if (instance != null) Pick(instance); };
            ToolTipService.SetToolTip(slot, instance?.Name ?? ""); _hotbar.Children.Add(slot);
        }
        var account = inst == null ? AccountStore.Shared.Selected : AccountStore.Shared.AccountFor(inst.Setup);
        _accountButton.Content = account == null ? Localization.T("添加账号", "Add account") : $"{account.Name}  ▾";
        _ = LoadCast(account);
        UpdateProgress();
    }

    private void ShowAccounts() => MainWindow.Current!.ShowAccounts(_accountButton);

    private async Task LoadCast(Account? account)
    {
        var revision = ++_castRevision;
        var look = PlayerLook.Steve;
        try
        {
            if (account?.Skin is { IsEmpty: false } skin)
            {
                var path = skin.File; var slim = skin.Slim;
                if (skin.Premium is { } premium)
                {
                    var remote = await Skins.Premium(premium); path = remote.Url; slim = remote.Slim;
                }
                if (path != null) look = DecodeSkin(Path.IsPathRooted(path) ? path : Skins.SkinPath(path), slim) ?? look;
            }
            else if (account?.Kind == Account.AccountKind.Microsoft)
            {
                var remote = await Skins.Premium(account.Name);
                look = DecodeSkin(remote.Url, remote.Slim) ?? look;
            }
            if (_author == null)
            {
                var remote = await Skins.Premium("NelsonPence");
                _author = DecodeSkin(remote.Url, remote.Slim);
            }
        }
        catch (Exception e) { Log.log($"[Scene] {e.Friendly()}"); }
        if (_loaded && revision == _castRevision) _scene.Cast = new SceneCast { Player = look, Author = _author ?? PlayerLook.Alex };
    }

    internal static PlayerLook? DecodeSkin(string path, bool slim)
    {
        try { using var file = File.OpenRead(path); return DecodeSkin(file, slim); } catch { return null; }
    }

    internal static PlayerLook? DecodeSkin(Stream stream, bool slim)
    {
        try
        {
            var decoder = Task.Run(() => Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream.AsRandomAccessStream()).AsTask()).Result;
            var pixels = Task.Run(async () => (await decoder.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                Windows.Graphics.Imaging.BitmapAlphaMode.Straight, new Windows.Graphics.Imaging.BitmapTransform(),
                Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation, 0)).DetachPixelData()).Result;
            return PlayerLook.FromSkin(pixels, (int)decoder.PixelWidth, (int)decoder.PixelHeight, slim);
        }
        catch { return null; }
    }

    private async Task LoadNotes()
    {
        try
        {
            var data = await Net.json("https://launchercontent.mojang.com/v2/javaPatchNotes.json");
            if (!_loaded) return;
            _notes.Children.Clear();
            foreach (var entry in data["entries"].Array.Take(4))
            {
                var version = entry["version"].String; if (version == null) continue;
                var image = new Image { Height = 92, Stretch = Stretch.UniformToFill };
                if (entry["image"]["url"].String is { } url)
                    image.Source = new BitmapImage(new Uri(new Uri("https://launchercontent.mojang.com"), url));
                var heading = Ui.Text(entry["title"].String ?? version, 12.5, FontWeights.Medium); heading.MaxLines = 2;
                var type = entry["type"].String == "release" ? Localization.T("正式版", "Release") : Localization.T("快照", "Snapshot");
                var subtitle = Ui.Text($"{type} {version}", 11.5);
                subtitle.Foreground = Ui.Brush("KilnTextSecondary");
                var captions = Ui.Col(heading, subtitle); captions.Spacing = 6;
                captions.Padding = new Thickness(12, 10, 12, 14);
                var content = Ui.Col(image, captions); content.Spacing = 0;
                var button = new Button { Content = content, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top, Style = (Style)Application.Current.Resources["KilnNavigationButton"] };
                button.Click += (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri($"https://minecraft.wiki/w/Java_Edition_{Uri.EscapeDataString(version)}"));
                Grid.SetColumn(button, _notes.Children.Count); _notes.Children.Add(button);
            }
        }
        catch { if (_loaded) _notesStatus.Text = Localization.T("无法加载更新日志，请检查网络。", "Couldn't load the update notes. Check your connection."); }
    }

    private void UpdateProgress()
    {
        var job = _launchJob;
        _playFill.Width = 230 * (job?.Fraction ?? 1);
        _playLabel.Text = job == null ? (_selected == null ? Localization.T("下载游戏", "Get the game") : Localization.T("开始游戏", "Play")) : job.Stage;
        _playLabel.FontSize = job == null ? 19 : 14;
        _playStatus.Text = job == null ? "" : job.Speed > 0 ? Job.FormatSpeed(job.Speed) : $"{job.Fraction:P0}";
        _playStatus.Visibility = job == null ? Visibility.Collapsed : Visibility.Visible;
        var games = MainWindow.Games.Take(3).ToList();
        var signature = string.Join("|", games.Select(g => $"{g.Id}:{g.State}:{g.ExitCode}"));
        if (_running.Tag is string old && old == signature) return;
        _running.Tag = signature; _running.Children.Clear();
        foreach (var game in games)
        {
            var log = Ui.Button(Localization.T("查看日志", "View log"), (_, _) => MainWindow.Current!.ShowGameLog(game));
            var stop = Ui.Button(Localization.T("结束", "Stop"), (_, _) => game.Kill());
            var status = game.State switch { GameProcess.GameState.Starting => Localization.T("正在等待游戏窗口", "Waiting for game window"), GameProcess.GameState.Running => Localization.T("运行中", "Running"), GameProcess.GameState.Exited => Localization.T("已退出", "Exited"), _ => Localization.T($"崩溃了，退出码 {game.ExitCode ?? -1}", $"Crashed, exit code {game.ExitCode ?? -1}") };
            var label = Ui.Row(Ui.Text(game.InstanceName, 12.5, FontWeights.Medium), Ui.Text(status, 12));
            var running = game.State is GameProcess.GameState.Starting or GameProcess.GameState.Running;
            _running.Children.Add(Ui.RecordRow(label, running ? new[] { log, stop } : new[] { log }));
        }
    }

    private async Task ExportScript()
    {
        if (_selected == null || _launchJob != null) return;
        var picker = new KilnFileSavePicker();
        
        picker.SuggestedFileName = _selected.Name; picker.FileTypeChoices.Add("Batch (.bat)", new List<string> { ".bat" });
        var file = await picker.PickSaveFileAsync();
        if (file != null) await Play(new LaunchOptions { ExportScript = file.Path });
    }

    private async Task Play(LaunchOptions options)
    {
        if (_selected == null || _launchJob != null) return;
        var inst = _selected;
        var account = AccountStore.Shared.AccountFor(inst.Setup);
        if (account == null) { MainWindow.Current!.NavigateTo("accounts"); return; }
        try
        {
            await LaunchCoordinator.Launch(inst, options);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (_loaded) await Ui.ShowError(XamlRoot, e);
        }
        finally { UpdateProgress(); }
    }
}

