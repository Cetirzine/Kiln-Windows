using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

internal sealed class LauncherImportForm : UserControl
{
    private readonly List<LauncherImport.Candidate> _candidates = new();
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly TextBlock _status = Ui.Text("", 12);
    private readonly HashSet<string> _imported;
    private readonly Func<Task<IReadOnlyList<LauncherImport.Candidate>>> _discover;
    private bool _started, _stopped;
    public Button ChooseButton { get; }
    public IReadOnlyList<LauncherImport.Candidate> Selected => _candidates.Where(c => _selected.Contains(c.Id) && c.Unsupported == null).ToArray();
    public event Action? Changed;
    public event Action<string>? ModpackRequested;
    private static string T(string zh, string en) => Localization.T(zh, en);

    public LauncherImportForm(IReadOnlyList<LauncherImport.Candidate>? preview = null, IEnumerable<string>? imported = null, Func<Task<IReadOnlyList<LauncherImport.Candidate>>>? discover = null)
    {
        _imported = new HashSet<string>(imported ?? InstanceScanner.Scan(LauncherSettings.Shared.MinecraftFolder).Select(i => i.Setup.ImportedFrom).OfType<string>(), StringComparer.OrdinalIgnoreCase);
        _discover = discover ?? (() => Task.Run(() => LauncherImport.Discover()));
        var explanation = Ui.Text(T("支持 Prism Launcher、MultiMC、PolyMC、CurseForge App，以及官方启动器中使用独立游戏目录的配置。Kiln 会重新安装同样的游戏与加载器，再复制 Mod、存档、资源包与设置；原来的文件不会改动。", "Supports Prism Launcher, MultiMC, PolyMC, CurseForge and official launcher profiles with their own game directory. Kiln installs the same game and loader, then copies mods, worlds, resource packs and settings. The original files aren't touched."), 12); explanation.Foreground = Ui.Brush("KilnTextSecondary");
        _status.Foreground = Ui.Brush("KilnTextSecondary");
        var scroller = new ScrollViewer { Content = _list, MinHeight = 120, MaxHeight = 320, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var frame = new Border { BorderBrush = Ui.Brush("KilnStroke"), BorderThickness = new Thickness(1), Background = Ui.Brush("KilnPanelSecondary"), Child = scroller };
        var body = Ui.Col(explanation, _status, frame); body.Spacing = 14; Content = body;
        ChooseButton = Ui.Button(T("选择文件夹或压缩包…", "Choose folder or archive…"), async (_, _) =>
        {
            var paths = await KilnFileBrowser.Pick(false, false, false, new[] { ".zip", ".mrpack" }, allowFolders: true);
            if (paths.FirstOrDefault() is { } path) await AddPath(path);
        });
        AllowDrop = true;
        DragOver += (_, e) => { if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy; };
        Drop += async (_, e) =>
        {
            try { foreach (var item in await e.DataView.GetStorageItemsAsync()) { await AddPath(item.Path); if (_stopped) break; } }
            catch (Exception error) { _status.Text = error.Friendly(); _status.Visibility = Visibility.Visible; }
        };
        if (preview != null) { _candidates.AddRange(preview); _started = true; if (preview.FirstOrDefault(c => c.Unsupported == null) is { } first) _selected.Add(first.Id); Render(); }
        Loaded += async (_, _) => await Discover();
    }
    private async Task Discover()
    {
        if (_started || _stopped) return; _started = true;
        _status.Text = T("正在查找……", "Looking…");
        try
        {
            var found = await _discover(); if (_stopped) return;
            foreach (var candidate in found) if (!_candidates.Any(c => string.Equals(c.Id, candidate.Id, StringComparison.OrdinalIgnoreCase))) _candidates.Add(candidate);
            _status.Text = ""; Render();
        }
        catch (Exception e) { if (!_stopped) { _status.Text = e.Friendly(); _status.Visibility = Visibility.Visible; } }
    }
    private async Task AddPath(string path)
    {
        try
        {
            LauncherImport.Candidate candidate;
            if (Directory.Exists(path)) candidate = await Task.Run(() => LauncherImport.Inspect(path)) ?? throw new LauncherException(T("这个文件夹不是支持的启动器实例。", "This folder isn't a supported launcher instance."));
            else if (await Task.Run(() => LauncherImport.IsInstanceArchive(path))) candidate = await Task.Run(() => LauncherImport.InspectArchive(path));
            else { _ = await Task.Run(() => Modpack.Inspect(path)); ModpackRequested?.Invoke(path); return; }
            if (_stopped) { await Task.Run(() => LauncherImport.ReleaseTemporary(candidate)); return; }
            if (!_candidates.Any(c => string.Equals(c.Id, candidate.Id, StringComparison.OrdinalIgnoreCase))) _candidates.Insert(0, candidate);
            if (candidate.Unsupported == null) _selected.Add(candidate.Id);
            _status.Text = candidate.Unsupported ?? ""; Render();
        }
        catch (Exception e) { if (!_stopped) { _status.Text = e.Friendly(); _status.Visibility = Visibility.Visible; } }
    }
    private void Render()
    {
        _list.Children.Clear(); _status.Visibility = _status.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var candidate in _candidates)
        {
            var row = new Grid { ColumnSpacing = 10, Height = 50, Padding = new Thickness(10, 0, 10, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            var check = new CheckBox { IsChecked = _selected.Contains(candidate.Id), IsEnabled = candidate.Unsupported == null, Tag = candidate.Id };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(check, candidate.Name);
            check.Checked += (_, _) => { _selected.Add(candidate.Id); Changed?.Invoke(); }; check.Unchecked += (_, _) => { _selected.Remove(candidate.Id); Changed?.Invoke(); }; row.Children.Add(check);
            var name = Ui.Text(candidate.Name, 13, FontWeights.Medium); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
            var heading = Ui.Row(name); heading.Spacing = 6;
            if (_imported.Contains(candidate.ContentDir)) heading.Children.Add(Ui.Text(T("已导入", "Imported"), 10.5));
            var parts = new List<string> { candidate.Summary }; if (candidate.ModCount > 0) parts.Add(T($"{candidate.ModCount} 个 Mod", $"{candidate.ModCount} mods")); if (candidate.WorldCount > 0) parts.Add(T($"{candidate.WorldCount} 个存档", $"{candidate.WorldCount} worlds")); if (candidate.Warning != null) parts.Add(candidate.Warning);
            var detail = Ui.Text(candidate.Unsupported ?? string.Join(" · ", parts), 11.5); detail.TextWrapping = TextWrapping.NoWrap; detail.TextTrimming = TextTrimming.CharacterEllipsis; detail.Foreground = Ui.Brush("KilnTextSecondary"); ToolTipService.SetToolTip(detail, detail.Text);
            var labels = Ui.Col(heading, detail); labels.Spacing = 2; labels.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(labels, 1); row.Children.Add(labels);
            var source = Ui.Text(candidate.Source.DisplayName(), 10.5); source.VerticalAlignment = VerticalAlignment.Center; source.Foreground = Ui.Brush("KilnTextSecondary"); Grid.SetColumn(source, 2); row.Children.Add(source);
            var folder = Ui.Button("", async (_, _) => { if (Directory.Exists(candidate.ContentDir)) await Windows.System.Launcher.LaunchFolderPathAsync(candidate.ContentDir); }); folder.Content = new FontIcon { Glyph = "\uE8B7", FontSize = 13 }; folder.Padding = new Thickness(4); ToolTipService.SetToolTip(folder, T("打开文件夹", "Open folder")); Grid.SetColumn(folder, 3); row.Children.Add(folder);
            _list.Children.Add(new Border { BorderBrush = Ui.Brush("KilnStroke"), BorderThickness = new Thickness(0, 0, 0, 1), Child = row });
        }
        if (_candidates.Count == 0) { var empty = Ui.Text(T("没有在本机找到其他启动器的版本。可以点“选择文件夹或压缩包…”手动选择。", "No instances from other launchers were found on this PC. Use Choose folder or archive to pick one."), 12); empty.Margin = new Thickness(10, 16, 10, 16); empty.Foreground = Ui.Brush("KilnTextSecondary"); _list.Children.Add(empty); }
        Changed?.Invoke();
    }
    public async Task Stop(IReadOnlyList<LauncherImport.Candidate>? keep = null)
    {
        _stopped = true;
        var retained = (keep ?? Array.Empty<LauncherImport.Candidate>()).Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        await Task.Run(() => { foreach (var candidate in _candidates.Where(c => !retained.Contains(c.Id))) LauncherImport.ReleaseTemporary(candidate); });
    }
}
