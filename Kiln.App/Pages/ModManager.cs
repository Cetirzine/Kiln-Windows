using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Kiln.App.Pages;

public sealed class ModManager : UserControl
{
    private readonly McInstance _instance;
    private readonly TextBox _search = new() { PlaceholderText = Localization.T("搜索名称、中文名或文件名", "Search name, Chinese name or filename"), Width = 260 };
    private readonly ComboBox _filter = new() { Width = 140 };
    private readonly TextBlock _status = Ui.Text("", 11.5);
    private readonly StackPanel _rows = new() { Spacing = 0 };
    private readonly Dictionary<string, ModIndex.Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModIndex.Update> _updates = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private FileSystemWatcher? _watcher;
    private List<string> _files = new();
    private int _revision;
    private bool _active;
    private bool _updating;
    private string DirectoryPath => Path.Combine(_instance.GameDir, "mods");
    private static string T(string zh, string en) => Localization.T(zh, en);

    public ModManager(McInstance instance)
    {
        _instance = instance;
        foreach (var item in new[] { T("全部", "All"), T("已启用", "Enabled"), T("已禁用", "Disabled"), T("可更新", "Updates") }) _filter.Items.Add(item);
        _filter.SelectedIndex = 0;
        var root = new Grid { RowSpacing = 12 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(Ui.Row(_search, _filter,
            Ui.Button(T("添加…", "Add…"), async (_, _) => await AddFiles()),
            Ui.Button(T("下载 Mod", "Download mods"), (_, _) => MainWindow.Current!.NavigateTo("mods")),
            Ui.Button(T("检查更新", "Check updates"), async (_, _) => await Reload(true)),
            Ui.Button(T("全部更新", "Update all"), async (_, _) => await ApplyUpdates(_updates.Values.ToList())),
            Ui.Button(T("打开文件夹", "Open folder"), async (_, _) => { Directory.CreateDirectory(DirectoryPath); await Windows.System.Launcher.LaunchFolderPathAsync(DirectoryPath); })));
        Grid.SetRow(_status, 1); root.Children.Add(_status);
        var scroll = new ScrollViewer { Content = _rows }; Grid.SetRow(scroll, 2); root.Children.Add(scroll);
        _search.TextChanged += (_, _) => Render(); _filter.SelectionChanged += (_, _) => Render();
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await Reload(false); };
        Loaded += async (_, _) =>
        {
            _active = true;
            Directory.CreateDirectory(DirectoryPath);
            _watcher = new FileSystemWatcher(DirectoryPath) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite, EnableRaisingEvents = true };
            _watcher.Created += Changed; _watcher.Deleted += Changed; _watcher.Renamed += Changed; _watcher.Changed += Changed;
            await Reload(false);
        };
        Unloaded += (_, _) => { _active = false; _revision++; _debounce.Stop(); _watcher?.Dispose(); _watcher = null; };
        Content = root;
    }
    private void Changed(object sender, FileSystemEventArgs args) => DispatcherQueue.TryEnqueue(() => { if (!_active) return; _debounce.Stop(); _debounce.Start(); });

    private async Task Reload(bool check)
    {
        var revision = ++_revision;
        try
        {
            _files = Directory.Exists(DirectoryPath) ? Directory.GetFiles(DirectoryPath).Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList() : new();
            Render(); _status.Text = T("正在识别 Mod…", "Identifying mods…");
            var entries = await ModIndex.Identify(_files);
            if (!_active || revision != _revision) return;
            _entries.Clear(); foreach (var entry in entries) _entries[entry.Key] = entry.Value;
            if (check)
            {
                _status.Text = T("正在检查更新…", "Checking for updates…");
                var updates = await ModIndex.Updates(entries.Values.ToList(), _instance.VanillaVersion, _instance.Loaders.FirstOrDefault());
                if (!_active || revision != _revision) return;
                _updates.Clear(); foreach (var update in updates) _updates[update.File] = update;
            }
            _status.Text = T($"{_files.Count} 个 Mod · {_entries.Values.Count(e => e.Project != null)} 个已识别 · {_updates.Count} 个可更新", $"{_files.Count} mods · {_entries.Values.Count(e => e.Project != null)} identified · {_updates.Count} updates");
            Render();
        }
        catch (Exception e) { if (_active && revision == _revision) _status.Text = e.Friendly(); }
    }

    private void Render()
    {
        _rows.Children.Clear();
        var query = _search.Text.Trim();
        foreach (var file in _files)
        {
            var disabled = file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            if (_filter.SelectedIndex == 1 && disabled || _filter.SelectedIndex == 2 && !disabled || _filter.SelectedIndex == 3 && !_updates.ContainsKey(file)) continue;
            var entry = _entries.GetValueOrDefault(file); var project = entry?.Project;
            var local = LocalInfo(file);
            var name = project?.TitleText ?? local.Name;
            var chinese = project == null ? null : ModNames.Shared.Chinese(project.Slug);
            if (chinese != null) name += "（" + chinese + "）";
            if (query.Length > 0 && !(name + " " + Path.GetFileName(file)).Contains(query, StringComparison.OrdinalIgnoreCase) && !(project != null && ModNames.Shared.Search(query).Contains(project.Slug))) continue;
            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 9, 10, 9) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new Image { Width = 30, Height = 30 };
            if (project?.IconUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri)) _ = RemoteImages.Set(icon, url);
            else if (Zip.Read(file, "icon.png") is { } bytes) _ = SetIcon(icon, bytes);
            row.Children.Add(icon);
            var title = Ui.Text(name, 13, FontWeights.SemiBold); title.TextTrimming = TextTrimming.CharacterEllipsis; title.TextWrapping = TextWrapping.NoWrap;
            var info = Ui.Text((entry?.Version?.Number ?? local.Version) + " · " + (project?.SourceKind.ToString() ?? T("本地文件", "Local file")) + (disabled ? T(" · 已禁用", " · Disabled") : ""), 11.5); info.Foreground = Ui.Brush("KilnTextSecondary");
            var labels = Ui.Col(title, info); labels.Spacing = 2; ToolTipService.SetToolTip(labels, Path.GetFileName(file)); Grid.SetColumn(labels, 1); row.Children.Add(labels);
            var actions = Ui.Row(); actions.Spacing = 6;
            if (_updates.TryGetValue(file, out var update)) actions.Children.Add(Ui.Button("↑ " + update.Latest.Number, async (_, _) => await ApplyUpdates(new[] { update })));
            actions.Children.Add(Ui.Button(disabled ? T("启用", "Enable") : T("禁用", "Disable"), async (_, _) =>
            {
                try { File.Move(file, disabled ? file[..^9] : file + ".disabled"); await Reload(false); }
                catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
            }));
            var more = new Button { Content = "···" }; var menu = new MenuFlyout();
            var copy = new MenuFlyoutItem { Text = T("复制文件名", "Copy filename") };
            copy.Click += (_, _) => { var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(Path.GetFileName(file)); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data); }; menu.Items.Add(copy);
            if (project?.PageUrl is { } page)
            {
                var web = new MenuFlyoutItem { Text = T("查看项目页面", "Project page") }; web.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri(page)); menu.Items.Add(web);
            }
            var delete = new MenuFlyoutItem { Text = T("移到回收站", "Move to Recycle Bin") };
            delete.Click += async (_, _) => { try { Recycle.Delete(file); await Reload(false); } catch (Exception e) { await Ui.ShowError(XamlRoot, e); } }; menu.Items.Add(delete); more.Flyout = menu; actions.Children.Add(more);
            Grid.SetColumn(actions, 2); row.Children.Add(actions);
            _rows.Children.Add(new Border { BorderBrush = Ui.Brush("KilnStroke"), BorderThickness = new Thickness(0, 0, 0, 1), Child = row, Opacity = disabled ? .55 : 1 });
        }
        if (_rows.Children.Count == 0) _rows.Children.Add(Ui.Text(_files.Count == 0 ? T("还没有 Mod。下载 Mod 或拖入 .jar 文件。", "No mods yet. Download mods or add .jar files.") : T("没有匹配的 Mod。", "No matching mods."), 13));
    }
    private static async Task SetIcon(Image image, byte[] bytes)
    {
        try { using var stream = new MemoryStream(bytes); var source = new BitmapImage(); await source.SetSourceAsync(stream.AsRandomAccessStream()); image.Source = source; } catch { }
    }
    private static (string Name, string Version) LocalInfo(string file)
    {
        try
        {
            if (Zip.Read(file, "fabric.mod.json") is { } fabric) { var j = Json.Parse(fabric); return (j["name"].String ?? j["id"].String ?? Path.GetFileName(file), j["version"].String ?? ""); }
            if (Zip.Read(file, "quilt.mod.json") is { } quilt) { var j = Json.Parse(quilt)["quilt_loader"]; return (j["metadata"]["name"].String ?? j["id"].String ?? Path.GetFileName(file), j["version"].String ?? ""); }
        }
        catch { }
        return (Path.GetFileName(file), "");
    }
    private async Task AddFiles()
    {
        var picker = new KilnFileOpenPicker();
         picker.FileTypeFilter.Add(".jar");
        var files = await picker.PickMultipleFilesAsync();
        try { Directory.CreateDirectory(DirectoryPath); foreach (var file in files) File.Copy(file.Path, Path.Combine(DirectoryPath, file.Name), true); await Reload(false); }
        catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
    }
    private async Task ApplyUpdates(IEnumerable<ModIndex.Update> updates)
    {
        if (_updating) return; _updating = true;
        var job = MainWindow.NewJob(T("更新 Mod", "Update mods"));
        try { foreach (var update in updates.ToArray()) { job.Token.ThrowIfCancellationRequested(); await ModIndex.Apply(update, job); } job.State = Job.JobState.Success; await Reload(true); }
        catch (OperationCanceledException) { job.State = Job.JobState.Cancelled; }
        catch (Exception e) { job.State = Job.JobState.Failed; job.Error = e.Friendly(); _status.Text = e.Friendly(); }
        finally { _updating = false; }
    }
}
