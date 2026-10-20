using Kiln.Core;
using Kiln.App.DotScene;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

public sealed class GameDownloadPage : UserControl
{
    private readonly ListView _versions = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly ComboBox _kind = new() { MinWidth = 160 };
    private readonly TextBox _search = new() { Width = 180, PlaceholderText = Localization.T("搜索", "Search") };
    private readonly StackPanel _install = new() { Spacing = 12 };
    private readonly TextBlock _status = Ui.Text("", 12);
    private IReadOnlyList<RemoteVersion> _all = Array.Empty<RemoteVersion>();
    private List<RemoteVersion> _visible = new();
    private int _generation;
    private bool _alive;
    private bool _installing;
    private string? _selectedVersion;
    private static string T(string zh, string en) => Localization.T(zh, en);
    public GameDownloadPage()
    {
        var root = new Grid { ColumnSpacing = 16 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 }); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        var left = new Grid { RowSpacing = 10 };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var kind in Enum.GetValues<McVersionKind>()) _kind.Items.Add(kind.Title()); _kind.SelectedIndex = 0;
        var filters = new Grid { ColumnSpacing = 10 }; filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        filters.Children.Add(Ui.Segmented(_kind, null)); var searchField = Ui.SearchField(_search); Grid.SetColumn(searchField, 1); filters.Children.Add(searchField);
        left.Children.Add(filters); _versions.Padding = new Thickness(8); _versions.Background = Ui.Brush("KilnPanel"); _versions.BorderBrush = Ui.Brush("KilnStroke"); _versions.BorderThickness = new Thickness(1);
        Grid.SetRow(_versions, 1); left.Children.Add(_versions);
        var retry = Ui.Button(T("重新加载", "Reload"), async (_, _) => await Load());
        var footer = new Grid { ColumnSpacing = 10 }; footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _status.VerticalAlignment = VerticalAlignment.Center; _status.Foreground = Ui.Brush("KilnTextSecondary"); footer.Children.Add(_status); Grid.SetColumn(retry, 1); footer.Children.Add(retry);
        Grid.SetRow(footer, 2); left.Children.Add(footer); root.Children.Add(left);
        var right = new ScrollViewer { Content = _install }; Grid.SetColumn(right, 1); root.Children.Add(right);
        _kind.SelectionChanged += (_, _) => Filter(); _search.TextChanged += (_, _) => Filter();
        _versions.SelectionChanged += async (_, _) => { if (_versions.SelectedIndex >= 0 && _versions.SelectedIndex < _visible.Count) { var version = _visible[_versions.SelectedIndex]; if (_selectedVersion != version.Id) { _selectedVersion = version.Id; await ShowInstall(version); } } };
        Loaded += async (_, _) => { _alive = true; await Load(); };
        Unloaded += (_, _) => { _alive = false; _generation++; };
        var empty = Ui.Col(BlockIconControl.For("Grass", 48), Ui.Text(T("在左侧选择一个版本", "Choose a version on the left")));
        empty.Spacing = 10; empty.Padding = new Thickness(0, 30, 0, 30); empty.HorizontalAlignment = HorizontalAlignment.Center;
        _install.Children.Add(Ui.Card("", empty));
        Content = root;
    }
    private async Task Load()
    {
        _status.Text = T("正在获取版本列表…", "Loading versions…");
        try { var manifest = await VersionManifest.Fetch(); if (!_alive) return; _all = manifest.Versions; _status.Text = ""; Filter(); }
        catch (Exception e) { if (_alive) _status.Text = e.Friendly(); }
    }
    private void Filter()
    {
        var kinds = Enum.GetValues<McVersionKind>();
        var kind = kinds[Math.Clamp(_kind.SelectedIndex, 0, kinds.Length - 1)];
        _visible = _all.Where(v => v.Kind == kind && v.Id.Contains(_search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        _versions.Items.Clear();
        var installed = InstanceScanner.Scan(LauncherSettings.Shared.MinecraftFolder).Select(i => i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var version in _visible)
        {
            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(BlockIconControl.For(version.Kind.Icon(), 26));
            var info = Ui.Text(version.ReleaseTime.ToLocalTime().ToString("yyyy-MM-dd") + (installed.Contains(version.Id) ? T(" · 已安装", " · Installed") : ""), 11.5); info.Foreground = Ui.Brush("KilnTextSecondary");
            var labels = Ui.Col(Ui.Text(version.Id, 13, FontWeights.Medium), info); labels.Spacing = 2;
            Grid.SetColumn(labels, 1); row.Children.Add(labels); _versions.Items.Add(row);
        }
        _versions.SelectedIndex = _visible.FindIndex(v => v.Id == _selectedVersion);
        if (_all.Count > 0) _status.Text = _visible.Count == 0 ? T("没有匹配的版本", "No matching versions") : T($"{_visible.Count} 个版本", $"{_visible.Count} versions");
    }
    private async Task ShowInstall(RemoteVersion version)
    {
        var generation = ++_generation;
        _install.Children.Clear();
        var body = new StackPanel { Spacing = 12 };
        var bodyHost = new UserControl { Content = body };
        var preview = new Border { Child = BlockIconControl.For(version.Kind.Icon(), 44), Width = 44, Height = 44 };
        var kindLabel = Ui.Text(version.Kind.Title(), 12); kindLabel.Foreground = Ui.Brush("KilnTextSecondary");
        body.Children.Add(Ui.Row(preview, Ui.Col(Ui.Text("Minecraft " + version.Id, 15, FontWeights.Bold), kindLabel)));
        var loader = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var loaderName in new[] { T("原版", "Vanilla"), "Fabric", "Quilt", "Forge", "NeoForge" }) loader.Items.Add(loaderName); loader.SelectedIndex = 0;
        var loaderVersion = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
        var options = new StackPanel { Spacing = 6 };
        var fabricApi = new CheckBox { Content = T("同时安装 Fabric API", "Also install Fabric API"), IsChecked = true, Visibility = Visibility.Collapsed };
        var optiToggle = new CheckBox { Content = T("同时安装 OptiFine", "Also install OptiFine"), Visibility = Visibility.Collapsed };
        var optiVersion = new ComboBox { Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Stretch };
        var liteToggle = new CheckBox { Content = T("同时安装 LiteLoader", "Also install LiteLoader"), Visibility = Visibility.Collapsed };
        var name = new TextBox { Text = version.Id }; var error = Ui.Text("", 11.5);
        IReadOnlyList<OptiFine.Version> optifine = Array.Empty<OptiFine.Version>(); IReadOnlyList<LiteLoader.Version> lite = Array.Empty<LiteLoader.Version>();
        var loaderRevision = 0;
        var installButton = Ui.Button(T("安装", "Install"), async (_, _) =>
        {
            if (_installing) return;
            var instanceName = name.Text.Trim(); var validation = InstanceManager.ValidateName(instanceName, LauncherSettings.Shared.MinecraftFolder);
            if (validation != null) { error.Text = validation; return; }
            if (loader.SelectedIndex > 0 && loaderVersion.SelectedItem is not string) { error.Text = T("请选择加载器版本", "Choose a loader version"); return; }
            var job = MainWindow.NewJob(T($"安装 {instanceName}", $"Install {instanceName}"));
            _installing = true; _versions.IsEnabled = false; bodyHost.IsEnabled = false;
            try
            {
                var selectedLoader = loader.SelectedIndex; var selectedVersion = loaderVersion.SelectedItem?.ToString();
                if (selectedLoader is 3 or 4) await ForgeInstaller.Install(selectedLoader == 3 ? ForgeKind.Forge : ForgeKind.NeoForge, selectedVersion!, version, instanceName, LauncherSettings.Shared.MinecraftFolder, job);
                else await Installer.Install(version, instanceName, selectedLoader == 0 ? null : (selectedLoader == 1 ? ModLoaderKind.Fabric : ModLoaderKind.Quilt, selectedVersion!), LauncherSettings.Shared.MinecraftFolder, job);
                var instance = new McInstance(LauncherSettings.Shared.MinecraftFolder, instanceName);
                if (selectedLoader == 1 && fabricApi.IsChecked == true)
                {
                    var api = (await Modrinth.Versions("fabric-api", version.Id, "Fabric")).FirstOrDefault() ?? throw new LauncherException(T("没有兼容的 Fabric API", "No compatible Fabric API"));
                    await ResourceSearchPage.Download(api, Path.Combine(instance.GameDir, "mods", api.FileName), job);
                }
                if (selectedLoader == 3)
                {
                    if (optiToggle.IsChecked == true && optiVersion.SelectedIndex >= 0) await OptiFine.Install(optifine[optiVersion.SelectedIndex], Path.Combine(instance.GameDir, "mods"), job);
                    if (liteToggle.IsChecked == true && lite.Count > 0) await LiteLoader.Install(lite[0], Path.Combine(instance.GameDir, "mods"), job);
                }
                LauncherSettings.Shared.SelectedInstance = instanceName; job.State = Job.JobState.Success; error.Text = T("安装完成", "Installed");
            }
            catch (OperationCanceledException) { job.State = Job.JobState.Cancelled; error.Text = T("已取消", "Cancelled"); }
            catch (Exception e) { job.State = Job.JobState.Failed; job.Error = e.Friendly(); error.Text = e.Friendly(); }
            finally { _installing = false; _versions.IsEnabled = true; bodyHost.IsEnabled = true; Filter(); }
        }); installButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        installButton.Background = Ui.Brush("KilnAccent"); installButton.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White); installButton.Height = 32;
        loader.SelectionChanged += async (_, _) =>
        {
            var revision = ++loaderRevision;
            var index = loader.SelectedIndex; loaderVersion.Items.Clear(); error.Text = "";
            loaderVersion.Visibility = index == 0 ? Visibility.Collapsed : Visibility.Visible;
            fabricApi.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            optiToggle.Visibility = optiVersion.Visibility = liteToggle.Visibility = Visibility.Collapsed;
            name.Text = index == 0 ? version.Id : version.Id + "-" + loader.SelectedItem;
            preview.Child = BlockIconControl.For(index switch { 1 => "Fabric", 2 => "Quilt", 3 => "Anvil", 4 => "NeoForge", _ => version.Kind.Icon() }, 44);
            if (index == 0) { installButton.IsEnabled = true; return; }
            installButton.IsEnabled = false; error.Text = T("正在读取加载器版本…", "Loading loader versions…");
            try
            {
                var versions = index is 1 or 2 ? (await (index == 1 ? ModLoaderKind.Fabric : ModLoaderKind.Quilt).Versions(version.Id)).Select(v => v.Version).ToList() : (await ForgeInstaller.Versions(index == 3 ? ForgeKind.Forge : ForgeKind.NeoForge, version.Id)).Select(v => v.VersionString).ToList();
                if (!_alive || generation != _generation || revision != loaderRevision) return;
                foreach (var v in versions) loaderVersion.Items.Add(v); loaderVersion.SelectedIndex = versions.Count > 0 ? 0 : -1;
                error.Text = versions.Count == 0 ? T("没有适用于该游戏版本的加载器", "No loader for this game version") : "";
                if (index == 3)
                {
                    try { optifine = await OptiFine.Versions(version.Id); } catch { optifine = Array.Empty<OptiFine.Version>(); }
                    try { lite = await LiteLoader.Versions(version.Id); } catch { lite = Array.Empty<LiteLoader.Version>(); }
                    if (!_alive || generation != _generation || revision != loaderRevision) return;
                    optiVersion.Items.Clear(); foreach (var v in optifine) optiVersion.Items.Add(v.Display);
                    if (optifine.Count > 0) { optiVersion.SelectedIndex = 0; optiToggle.Visibility = optiVersion.Visibility = Visibility.Visible; }
                    if (lite.Count > 0) liteToggle.Visibility = Visibility.Visible;
                }
            }
            catch (Exception e) { if (generation == _generation && revision == loaderRevision) error.Text = e.Friendly(); }
            finally { if (generation == _generation && revision == loaderRevision) installButton.IsEnabled = index == 0 || loaderVersion.SelectedIndex >= 0; }
        };
        body.Children.Add(Ui.Text(T("Mod 加载器", "Mod loader"), 12, FontWeights.SemiBold)); body.Children.Add(Ui.Segmented(loader, null)); body.Children.Add(loaderVersion);
        options.Children.Add(fabricApi); options.Children.Add(optiToggle); options.Children.Add(optiVersion); options.Children.Add(liteToggle); body.Children.Add(options);
        body.Children.Add(Ui.Text(T("名称", "Name"), 12, FontWeights.SemiBold)); body.Children.Add(name); body.Children.Add(error); body.Children.Add(installButton);
        _install.Children.Add(Ui.Card(T("安装", "Install"), bodyHost)); await Task.CompletedTask;
    }
}
