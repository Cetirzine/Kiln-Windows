using Kiln.Core;
using Kiln.App.DotScene;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

public sealed class InstancesPage : Page
{
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly TextBox _search = new() { PlaceholderText = Localization.T("按名称筛选", "Filter by name") };
    private readonly TextBlock _folder = Ui.Text("", 11.5);
    private FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private static string T(string zh, string en) => Localization.T(zh, en);
    public InstancesPage()
    {
        var root = new Grid { RowSpacing = 14 };
        for (var i = 0; i < 3; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(Ui.Header(T("版本", "Instances"),
            Ui.Button(T("切换文件夹…", "Game folders…"), (_, _) => FolderMenu()),
            Ui.Button(T("导入…", "Import…"), (_, _) => MainWindow.Current!.NavigateTo("import")),
            Ui.Button(T("安装新版本", "Install a version"), (_, _) => MainWindow.Current!.NavigateTo("download"))));
        _folder.Text = LauncherSettings.Shared.MinecraftFolder; _folder.TextTrimming = TextTrimming.CharacterEllipsis; _folder.MaxLines = 1;
        Grid.SetRow(_folder, 1); root.Children.Add(_folder); Grid.SetRow(_search, 2); root.Children.Add(_search);
        var scroll = new ScrollViewer { Content = _list }; Grid.SetRow(scroll, 3); root.Children.Add(scroll);
        _search.TextChanged += (_, _) => Reload();
        _debounce.Tick += (_, _) => { _debounce.Stop(); Reload(); };
        Loaded += (_, _) => { Reload(); Watch(); };
        Unloaded += (_, _) => { _watcher?.Dispose(); _watcher = null; _debounce.Stop(); };
        Content = root;
    }
    private void Watch()
    {
        _watcher?.Dispose();
        var versions = Path.Combine(LauncherSettings.Shared.MinecraftFolder, "versions");
        if (!Directory.Exists(versions)) return;
        _watcher = new FileSystemWatcher(versions) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite, EnableRaisingEvents = true };
        void Changed(object sender, FileSystemEventArgs e) => DispatcherQueue.TryEnqueue(() => { _debounce.Stop(); _debounce.Start(); });
        _watcher.Created += Changed; _watcher.Deleted += Changed; _watcher.Renamed += Changed;
    }
    private void Reload()
    {
        _list.Children.Clear();
        var instances = InstanceScanner.Scan(LauncherSettings.Shared.MinecraftFolder).Where(i => i.Name.Contains(_search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        var header = MakeColumns(Ui.Text(T("名称", "Name"), 11.5), Ui.Text(T("运行方式", "Runs as"), 11.5), Ui.Text(T("上次游玩", "Last played"), 11.5), Ui.Text(T("游戏时长", "Play time"), 11.5), new Border());
        _list.Children.Add(header);
        foreach (var group in instances.GroupBy(i => i.Kind).OrderBy(g => g.Key))
        {
            var groupName = group.Key switch { McInstance.Category.Modded => T("装有 Mod 加载器", "With a mod loader"), McInstance.Category.Release => T("原版", "Vanilla"), McInstance.Category.Snapshot => T("快照", "Snapshots"), McInstance.Category.Old => T("远古版本", "Old alpha and beta"), McInstance.Category.AprilFool => T("愚人节版本", "April Fools"), _ => T("无法识别", "Unrecognized") };
            var heading = Ui.Text($"{groupName}  {group.Count()}", 12, FontWeights.SemiBold); heading.Margin = new Thickness(10, 16, 0, 6); _list.Children.Add(heading);
            foreach (var instance in group)
            {
                var title = Ui.Text(instance.Name, 13, FontWeights.Medium); title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis;
                var label = new Grid { ColumnSpacing = 12 };
                label.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) }); label.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                label.Children.Add(BlockIconControl.For(instance.Icon, 26)); Grid.SetColumn(title, 1); label.Children.Add(title);
                var menu = InstanceMenu(instance); var more = new Button { Content = "···", Flyout = menu };
                var row = MakeColumns(label, Ui.Text(instance.Description, 11.5), Ui.Text(instance.Setup.LastPlayed?.ToLocalTime().ToString("yyyy-MM-dd") ?? "—", 11.5), Ui.Text(McInstance.FormatPlayTime(instance.Setup.PlayTime) ?? "—", 11.5), more);
                var button = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0), Style = (Style)Application.Current.Resources["KilnNavigationButton"], ContextFlyout = InstanceMenu(instance) };
                button.Click += (_, _) => MainWindow.Current!.ShowInstance(instance);
                _list.Children.Add(button);
            }
        }
        if (instances.Count == 0) _list.Children.Add(Ui.Card(T("没有版本", "No instances"), Ui.Text(T("安装一个版本，或者切换到已有的 Minecraft 文件夹。", "Install a version or switch to an existing Minecraft folder.")), Ui.Button(T("安装新版本", "Install a version"), (_, _) => MainWindow.Current!.NavigateTo("download"))));
    }
    private static Grid MakeColumns(params UIElement[] content)
    {
        var row = new Grid { Padding = new Thickness(10, 7, 10, 7), ColumnSpacing = 12 };
        foreach (var width in new[] { -1d, 150, 110, 80, 36 }) row.ColumnDefinitions.Add(new ColumnDefinition { Width = width < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
        for (var i = 0; i < content.Length; i++) { Grid.SetColumn((FrameworkElement)content[i], i); row.Children.Add(content[i]); }
        return row;
    }
    private MenuFlyout InstanceMenu(McInstance instance)
    {
        var menu = new MenuFlyout();
        void Add(string title, Action action) { var item = new MenuFlyoutItem { Text = title }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add(T("版本设置", "Instance settings"), () => MainWindow.Current!.ShowInstance(instance));
        Add(T("打开文件夹", "Open folder"), () => _ = Windows.System.Launcher.LaunchFolderPathAsync(instance.GameDir));
        Add(T("复制启动链接", "Copy launch link"), () => SystemIntegration.CopyLaunchLink(instance));
        Add(T("创建桌面快捷方式", "Create desktop shortcut"), () => SystemIntegration.CreateShortcut(instance));
        Add(T("删除…", "Delete…"), async () =>
        {
            try
            {
                var deps = InstanceManager.DependentsOf(instance);
                if (deps.Count > 0) { await Ui.ShowError(XamlRoot, T("这些版本依赖它：", "These instances depend on it: ") + string.Join(", ", deps)); return; }
                var dialog = new KilnDialog { XamlRoot = XamlRoot, Title = T("删除版本", "Delete instance"), Content = instance.Name, PrimaryButtonText = T("移到回收站", "Move to Recycle Bin"), CloseButtonText = T("取消", "Cancel") };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary) { Recycle.Delete(instance.Dir); Reload(); }
            }
            catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
        });
        return menu;
    }
    private void FolderMenu()
    {
        var menu = new MenuFlyout();
        foreach (var folder in new[] { Paths.DefaultMinecraft, LauncherSettings.Shared.MinecraftFolder }.Concat(LauncherSettings.Shared.ExtraFolders).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var item = new MenuFlyoutItem { Text = folder };
            item.Click += (_, _) => { LauncherSettings.Shared.MinecraftFolder = folder; _folder.Text = folder; Reload(); Watch(); }; menu.Items.Add(item);
        }
        var add = new MenuFlyoutItem { Text = T("添加游戏文件夹…", "Add game folder…") };
        add.Click += async (_, _) =>
        {
            var picker = new KilnFolderPicker();  picker.FileTypeFilter.Add("*");
            var result = await picker.PickSingleFolderAsync(); if (result == null) return;
            LauncherSettings.Shared.ExtraFolders = LauncherSettings.Shared.ExtraFolders.Append(result.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            LauncherSettings.Shared.MinecraftFolder = result.Path; _folder.Text = result.Path; Reload(); Watch();
        };
        menu.Items.Add(add); menu.ShowAt(_folder);
    }
}
