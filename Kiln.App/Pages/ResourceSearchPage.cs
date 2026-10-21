using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Kiln.App.Pages;

public sealed class ResourceSearchPage : UserControl
{
    private readonly Modrinth.ProjectType _type;
    private readonly TextBox _query = new() { Width = 260, PlaceholderText = Localization.T("搜索（支持中文、拼音）", "Search (Chinese and pinyin supported)") };
    private readonly ComboBox _target = new() { MinWidth = 220 };
    private readonly ComboBox _source = new() { MinWidth = 150 };
    private readonly ComboBox _loader = new() { MinWidth = 140 };
    private readonly TextBox _game = new() { Width = 140, PlaceholderText = Localization.T("游戏版本", "Game version") };
    private readonly StackPanel _results = new() { Spacing = 0 };
    private readonly TextBlock _status = Ui.Text("", 11.5);
    private readonly List<Modrinth.Project> _projects = new();
    private readonly List<McInstance> _instances;
    private readonly Button _more;
    private readonly Button _configureCurseforge;
    private int _generation;
    private int _offset;
    private bool _alive;
    private bool _working;
    private static string T(string zh, string en) => Localization.T(zh, en);
    private McInstance? Target => _target.SelectedIndex > 0 && _target.SelectedIndex <= _instances.Count ? _instances[_target.SelectedIndex - 1] : null;
    private string? Loader => _type is Modrinth.ProjectType.Mod or Modrinth.ProjectType.Modpack && _loader.SelectedIndex > 0 ? _loader.SelectedItem?.ToString() : null;
    public ResourceSearchPage(Modrinth.ProjectType type)
    {
        _type = type;
        _instances = InstanceScanner.Scan(LauncherSettings.Shared.MinecraftFolder).Where(i => i.Kind != McInstance.Category.Error).ToList();
        _target.Items.Add(T("不选择版本（另存为文件）", "No instance (save file)")); foreach (var i in _instances) _target.Items.Add(i.Name);
        var selected = _instances.FindIndex(i => i.Name == LauncherSettings.Shared.SelectedInstance); _target.SelectedIndex = selected < 0 ? 0 : selected + 1;
        foreach (var name in new[] { T("全部", "All"), "Modrinth", "CurseForge" }) _source.Items.Add(name); _source.SelectedIndex = 0;
        foreach (var name in new[] { T("全部", "All"), "Fabric", "Forge", "NeoForge", "Quilt" }) _loader.Items.Add(name); _loader.SelectedIndex = 0;
        void SetTarget() { _game.Text = Target?.VanillaVersion ?? ""; var own = Target?.Loaders.FirstOrDefault(); _loader.SelectedIndex = own == null ? 0 : Math.Max(0, _loader.Items.IndexOf(own)); }
        SetTarget(); _target.SelectionChanged += (_, _) => SetTarget();
        var root = new Grid { RowSpacing = 12 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var searchRow = new Grid { ColumnSpacing = 10 };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        _query.PlaceholderText = T("搜索", "Search ") + _type.Title();
        searchRow.Children.Add(Ui.SearchField(_query));
        if (type == Modrinth.ProjectType.Modpack)
        {
            var install = Ui.Button(T("从文件安装…", "Install from file…"), async (_, _) => await ImportPage.ChooseModpack(XamlRoot)); install.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(install, 1); searchRow.Children.Add(install);
        }
        else
        {
            var target = new Grid { ColumnSpacing = 8 };
            target.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); target.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = Ui.Text(T("安装到", "Install to"), 12); label.VerticalAlignment = VerticalAlignment.Center; target.Children.Add(label);
            _target.MinWidth = 0; _target.HorizontalAlignment = HorizontalAlignment.Stretch; _target.Height = 28; Grid.SetColumn(_target, 1); target.Children.Add(_target);
            Grid.SetColumn(target, 1); searchRow.Children.Add(target);
        }
        root.Children.Add(searchRow);
        var filters = new Grid { ColumnSpacing = 10, RowSpacing = 8 };
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) }); filters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); filters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var sourceLabel = Ui.Text(T("来源", "Source"), 12); sourceLabel.Foreground = Ui.Brush("KilnTextSecondary"); sourceLabel.VerticalAlignment = VerticalAlignment.Center; filters.Children.Add(sourceLabel);
        var sources = Ui.Segmented(_source); Grid.SetColumn(sources, 1); filters.Children.Add(sources);
        var loaderHint = Ui.Text("", 11.5); loaderHint.Foreground = Ui.Brush("KilnTextSecondary");
        if (type is Modrinth.ProjectType.Mod or Modrinth.ProjectType.Modpack)
        {
            var label = Ui.Text(T("加载器", "Loader"), 12); label.Foreground = Ui.Brush("KilnTextSecondary"); label.VerticalAlignment = VerticalAlignment.Center; Grid.SetRow(label, 1); filters.Children.Add(label);
            var loaders = Ui.Segmented(_loader); Grid.SetRow(loaders, 1); Grid.SetColumn(loaders, 1); filters.Children.Add(loaders);
            Grid.SetRow(loaderHint, 1); Grid.SetColumn(loaderHint, 2); filters.Children.Add(loaderHint);
        }
        Grid.SetRow(filters, 1); root.Children.Add(filters);
        void UpdateHint() => loaderHint.Text = Loader == "Quilt" ? T("包含可在 Quilt 上运行的 Fabric Mod", "Includes Fabric mods that run on Quilt") : Target?.Loaders.FirstOrDefault() is { } own && Loader != null && own != Loader ? T($"与所选版本的加载器（{own}）不同", $"Differs from the instance loader ({own})") : "";
        _loader.SelectionChanged += async (_, _) => { UpdateHint(); if (_alive) await Search(false); };
        _source.SelectionChanged += async (_, _) => { if (_alive) await Search(false); };
        _target.SelectionChanged += async (_, _) => { UpdateHint(); if (_alive) await Search(false); };
        var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        debounce.Tick += async (_, _) => { debounce.Stop(); if (_alive) await Search(false); };
        _query.TextChanged += (_, _) => { debounce.Stop(); debounce.Start(); };
        Unloaded += (_, _) => debounce.Stop();
        UpdateHint();        _more = Ui.Button(T("加载更多", "Load more"), async (_, _) => await Search(true));
        var scroll = new ScrollViewer { Content = Ui.Col(_results, _more) }; Grid.SetRow(scroll, 2); root.Children.Add(scroll);
        var retry = Ui.Button(T("重试", "Retry"), async (_, _) => await Search(false));
        _configureCurseforge = Ui.Button(T("配置 CurseForge", "Configure CurseForge"), (_, _) => { Ui.SelectTab("setup", 3); MainWindow.Current!.NavigateTo("setup"); });
        _configureCurseforge.Visibility = Visibility.Collapsed;
        var statusRow = new Grid { ColumnSpacing = 12 };
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _status.TextWrapping = TextWrapping.Wrap; _status.VerticalAlignment = VerticalAlignment.Center;
        statusRow.Children.Add(_status); var statusActions = Ui.Row(_configureCurseforge, retry); statusActions.Spacing = 8; Grid.SetColumn(statusActions, 1); statusRow.Children.Add(statusActions);
        Grid.SetRow(statusRow, 3); root.Children.Add(statusRow);
        _query.KeyDown += async (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; await Search(false); } };
        Loaded += async (_, _) => { _alive = true; await Search(false); };
        Unloaded += (_, _) => { _alive = false; _generation++; _searchCancellation?.Cancel(); };
        Content = root;
    }
    private CancellationTokenSource? _searchCancellation;
    private async Task Search(bool more)
    {
        _searchCancellation?.Cancel(); _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource(); _searchCancellation.CancelAfter(TimeSpan.FromSeconds(25)); var ct = _searchCancellation.Token;
        var generation = ++_generation;
        if (!more) { _offset = 0; _projects.Clear(); _results.Children.Clear(); }
        _status.Text = T("正在搜索…", "Searching…"); _more.IsEnabled = false;
        _configureCurseforge.Visibility = Visibility.Collapsed;
        var errors = new List<string>(); var moreAvailable = false;
        var query = _query.Text.Trim(); var game = _game.Text.Trim(); var loader = Loader; var source = _source.SelectedIndex; var offset = _offset;
        void Append(IEnumerable<Modrinth.Project> batch)
        {
            if (!_alive || generation != _generation) return;
            foreach (var project in batch.DistinctBy(p => (p.SourceKind, p.Id)))
            {
                if (_projects.Any(p => p.SourceKind == project.SourceKind && p.Id == project.Id)) continue;
                _projects.Add(project); _results.Children.Add(ProjectRow(project));
            }
        }
        async Task<IReadOnlyList<Modrinth.Project>> Fetch(bool curse)
        {
            try { return curse ? await CurseForge.Search(query, _type, game.Length == 0 ? null : game, loader, offset, ct) : await Modrinth.Search(query, _type, game.Length == 0 ? null : game, loader, offset, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (_alive && generation == _generation) errors.Add(curse ? T("CurseForge 连接超时", "CurseForge timed out") : T("Modrinth 连接超时，请重试", "Modrinth timed out; retry"));
                return Array.Empty<Modrinth.Project>();
            }
            catch (Exception e)
            {
                Log.log($"[Search] {(curse ? "CurseForge" : "Modrinth")}: {e.Friendly()}");
                if (curse && _alive && generation == _generation) _configureCurseforge.Visibility = Visibility.Visible;
                errors.Add(curse ? e is CurseForgeException ? e.Message : T("CurseForge 连接失败，请检查网络或重试", "CurseForge connection failed; check the network or retry") : T("Modrinth 暂时不可用，请重试", "Modrinth is temporarily unavailable; retry")); return Array.Empty<Modrinth.Project>();
            }
        }
        var tasks = new List<Task<IReadOnlyList<Modrinth.Project>>>(); if (source != 2) tasks.Add(Fetch(false)); if (source != 1) tasks.Add(Fetch(true));
        if (!more && query.Length > 0 && source != 2 && _type == Modrinth.ProjectType.Mod)
        {
            var slugs = ModNames.Shared.Search(query);
            if (slugs.Count > 0) tasks.Add(FetchNamed());
            async Task<IReadOnlyList<Modrinth.Project>> FetchNamed()
            {
                try { return await Modrinth.Projects(slugs.Take(8).ToList(), game.Length == 0 ? null : game, loader, ct); }
                catch (OperationCanceledException) { return Array.Empty<Modrinth.Project>(); }
                catch (Exception e) { Log.log("[Search names] " + e.Friendly()); return Array.Empty<Modrinth.Project>(); }
            }
        }
        while (tasks.Count > 0)
        {
            var ready = await Task.WhenAny(tasks); tasks.Remove(ready); var batch = await ready;
            if (!_alive || generation != _generation) return;
            Append(batch); moreAvailable |= batch.Count >= 30;
            _status.Text = tasks.Count > 0 ? T($"已找到 {_projects.Count} 个结果，正在读取其他来源…", $"{_projects.Count} results; loading other sources…") : "";
        }
        if (!_alive || generation != _generation) return;
        _offset = offset + 30; _more.IsEnabled = moreAvailable;
        _status.Text = errors.Count > 0 ? string.Join(" · ", errors) + (_projects.Count > 0 ? T("；已显示其他来源的结果。", "; results from other sources are shown.") : T("。请重试或切换来源。", ". Retry or change source.")) : T($"{_projects.Count} 个结果 · 支持中文、拼音或拼音首字母搜索", $"{_projects.Count} results · Modrinth and CurseForge");
        if (_projects.Count == 0 && errors.Count == 0) _status.Text = T("没有匹配结果，请调整目标版本或加载器筛选。", "No matching results. Adjust the target instance or loader filter.");
    }    private Button ProjectRow(Modrinth.Project project)
    {
        var row = new Grid { Padding = new Thickness(10), ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new Image { Width = 44, Height = 44 }; if (project.IconUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri)) _ = RemoteImages.Set(icon, url); row.Children.Add(icon);
        var chinese = Localizer.Shared.Language == AppLanguage.En ? null : ModNames.Shared.Chinese(project.Slug);
        var title = Ui.Text(project.TitleText + (chinese == null ? "" : "  " + chinese), 13.5, FontWeights.SemiBold); title.TextTrimming = TextTrimming.CharacterEllipsis; title.TextWrapping = TextWrapping.NoWrap;
        var description = Ui.Text(project.Description, 11.5); description.MaxLines = 2; description.Foreground = Ui.Brush("KilnTextSecondary");
        var labels = Ui.Col(title, description, Ui.Text(project.SourceKind + " · " + string.Join(", ", project.Categories.Take(4)), 11)); labels.Spacing = 3;
        Grid.SetColumn(labels, 1); row.Children.Add(labels);
        var downloads = Ui.Text(project.Downloads >= 1000000 ? $"↓ {project.Downloads / 1000000.0:N1}M" : $"↓ {project.Downloads / 1000.0:N1}K", 11); Grid.SetColumn(downloads, 2); row.Children.Add(downloads);
        var button = new Button { Content = new Border { Child = row, BorderBrush = Ui.Brush("KilnStroke"), BorderThickness = new Thickness(0, 0, 0, 1) }, Style = (Style)Application.Current.Resources["KilnNavigationButton"], HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0) };
        button.Click += async (_, _) => await ShowVersions(project); return button;
    }
    private async Task ShowVersions(Modrinth.Project project)
    {
        if (_working) return; _working = true;
        try
        {
            var target = Target; var game = _game.Text.Trim(); var loader = Loader;
            _status.Text = T("正在读取版本…", "Loading versions…");
            var versions = project.SourceKind == Modrinth.Source.CurseForge ? await CurseForge.Files(project, game.Length == 0 ? null : game, loader) : await Modrinth.Versions(project.Id, game.Length == 0 ? null : game, loader);
            var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 360, MinWidth = 0, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            foreach (var v in versions) list.Items.Add(Ui.Col(Ui.Text(v.Name, 13, FontWeights.SemiBold), Ui.Text($"{v.Number} · {v.Published:yyyy-MM-dd} · {string.Join(", ", v.Loaders)}" + (v.Blocked ? T(" · 需手动下载", " · Manual download") : ""), 11.5)));
            list.SelectedIndex = versions.Count > 0 ? 0 : -1;
            var website = project.PageUrl ?? $"https://modrinth.com/{_type.Id()}/{project.Slug}";
            var dialog = new KilnDialog { XamlRoot = XamlRoot, Title = project.TitleText, Content = Ui.Col(Ui.Text(project.Description, 12), Ui.Text(target == null ? T("另存为文件", "Save as file") : T($"安装到 {target.Name}", $"Install into {target.Name}"), 12), list), PrimaryButtonText = T("安装", "Install"), SecondaryButtonText = T("项目网站", "Project website"), CloseButtonText = T("取消", "Cancel"), IsPrimaryButtonEnabled = versions.Count > 0 };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Secondary) { await Windows.System.Launcher.LaunchUriAsync(new Uri(website)); return; }
            if (result != ContentDialogResult.Primary || list.SelectedIndex < 0) return;
            var version = versions[list.SelectedIndex];
            if (version.Blocked) { await Windows.System.Launcher.LaunchUriAsync(new Uri(website)); return; }
            var dependencies = new List<Modrinth.Version>();
            if (_type == Modrinth.ProjectType.Mod && target != null)
            {
                var mods = Path.Combine(target.GameDir, "mods");
                var installed = Directory.Exists(mods) ? Directory.GetFiles(mods, "*.jar*") : Array.Empty<string>();
                var entries = await ModIndex.Identify(installed);
                var required = await ModIndex.RequiredDependencies(version, target.VanillaVersion, loader, ModIndex.InstalledProjects(entries));
                if (required.Count > 0)
                {
                    var confirmation = new KilnDialog { XamlRoot = XamlRoot, Title = T("需要前置 Mod", "Required dependencies"), Content = string.Join("\n", required.Select(d => d.Project?.TitleText ?? d.Version.FileName)), PrimaryButtonText = T("一起安装", "Install all"), SecondaryButtonText = T("只安装主 Mod", "Only the main mod"), CloseButtonText = T("取消", "Cancel") };
                    var choice = await confirmation.ShowAsync(); if (choice == ContentDialogResult.None) return;
                    if (choice == ContentDialogResult.Primary) dependencies.AddRange(required.Select(d => d.Version));
                }
            }
            var job = MainWindow.NewJob(T($"安装 {project.TitleText}", $"Install {project.TitleText}"));
            try
            {
                if (_type == Modrinth.ProjectType.Modpack)
                {
                    var archive = Path.Combine(Paths.Cache, "modpack-" + Guid.NewGuid().ToString("N") + Path.GetExtension(version.FileName));
                    await Download(version, archive, job); await ImportPage.InstallModpack(XamlRoot, archive);
                }
                else if (target == null)
                {
                    var picker = new KilnFileSavePicker();  picker.SuggestedFileName = Path.GetFileNameWithoutExtension(version.FileName); picker.FileTypeChoices.Add("File", new List<string> { Path.GetExtension(version.FileName) });
                    var file = await picker.PickSaveFileAsync(); if (file == null) { job.State = Job.JobState.Cancelled; return; } await Download(version, file.Path, job);
                }
                else
                {
                    foreach (var v in new[] { version }.Concat(dependencies)) await Download(v, Path.Combine(target.GameDir, _type.Folder(), v.FileName), job);
                }
                job.State = Job.JobState.Success; _status.Text = T("安装完成", "Installed");
            }
            catch (OperationCanceledException) { job.State = Job.JobState.Cancelled; }
            catch (Exception e) { job.State = Job.JobState.Failed; job.Error = e.Friendly(); throw; }
        }
        catch (Exception e) { _status.Text = e.Friendly(); if (_alive) await Ui.ShowError(XamlRoot, e); }
        finally { _working = false; }
    }
    internal static async Task Download(Modrinth.Version version, string destination, Job job)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var item = new DownloadItem(version.FileUrl, destination, version.Sha1, version.Size);
        if (version.SourceKind == Modrinth.Source.CurseForge) item.Urls = CurseForge.DownloadUrls(version);
        await Downloader.Run(new[] { item }, job);
    }
}


