using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

/// 下载页：安装游戏版本与加载器、安装整合包、从其他启动器导入（含 PCL2 接管）
public sealed class DownloadPage : Page
{
    private readonly ComboBox _kindBox = new();
    private readonly ListView _list = new() { MinHeight = 360 };
    private readonly TextBox _searchBox = new() { PlaceholderText = "搜索 / Search", MinWidth = 260 };
    private readonly ProgressBar _progress = Ui.Progress();
    private readonly TextBlock _status = Ui.Text("", 13);
    private readonly ComboBox _targetBox = new() { MinWidth = 260 };
    private List<McInstance> _instances = new();

    public DownloadPage()
    {
        BuildUi();
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        _instances = InstanceScanner.Scan(LauncherSettings.Shared.MinecraftFolder).ToList();
        _targetBox.Items.Clear();
        foreach (var i in _instances) _targetBox.Items.Add(i.Name);
        if (_targetBox.SelectedIndex < 0 && _instances.Count > 0) _targetBox.SelectedIndex = 0;
    }

    private void BuildUi()
    {
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(0, 16, 0, 0) };
        panel.Children.Add(Ui.Text(Localization.T("下载", "Download"), 26, FontWeights.SemiBold));

        _kindBox.Items.Add(Localization.T("原版版本", "Vanilla versions"));
        _kindBox.Items.Add("Fabric / Quilt");
        _kindBox.Items.Add("Forge / NeoForge");
        _kindBox.Items.Add(Localization.T("Mod / 资源包 / 光影", "Mods / Resource packs / Shaders"));
        _kindBox.Items.Add(Localization.T("整合包（文件）", "Modpack (file)"));
        _kindBox.Items.Add(Localization.T("从其他启动器导入", "Import from other launchers"));
        _kindBox.SelectedIndex = 0;
        _kindBox.SelectionChanged += async (_, _) => await LoadList();

        var controls = Ui.Row(_kindBox, _searchBox, Ui.Button(Localization.T("搜索", "Search"), async (_, _) => await LoadList()));
        panel.Children.Add(controls);
        panel.Children.Add(Ui.Row(Ui.Text(Localization.T("安装到新版本：", "Install as: "), 13), _targetBox));
        panel.Children.Add(_list);
        panel.Children.Add(_progress);
        panel.Children.Add(_status);

        var hint = Ui.Text(Localization.T(
            "原版列表点击即装；加载器先在原版列表中选择对应游戏版本（或直接在版本页安装）。Mod 安装到上面选中的版本。",
            "Click a vanilla version to install. Pick the game version first for loaders. Mods install into the selected instance."), 12);
        panel.Children.Add(hint);

        Content = new ScrollViewer { Content = panel };
        Loaded += async (_, _) => await LoadList();
    }

    private async Task LoadList()
    {
        _list.Items.Clear();
        _status.Text = Localization.T("加载中…", "Loading…");
        try
        {
            switch (_kindBox.SelectedIndex)
            {
                case 0:
                    var manifest = await VersionManifest.Fetch();
                    var query = _searchBox.Text.Trim();
                    foreach (var v in manifest.Versions.Where(v => query.Length == 0 || v.Id.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(200))
                        _list.Items.Add(MakeVersionItem(v));
                    break;
                case 1:
                    var game = await PromptGameVersion();
                    if (game == null) { _status.Text = ""; return; }
                    foreach (var kind in new[] { ModLoaderKind.Fabric, ModLoaderKind.Quilt })
                        foreach (var v in await kind.Versions(game))
                            _list.Items.Add(MakeLoaderItem(kind, game, v));
                    break;
                case 2:
                    var game2 = await PromptGameVersion();
                    if (game2 == null) { _status.Text = ""; return; }
                    foreach (var kind in new[] { ForgeKind.Forge, ForgeKind.NeoForge })
                        foreach (var v in await ForgeInstaller.Versions(kind, game2))
                            _list.Items.Add(MakeForgeItem(kind, game2, v));
                    break;
                case 3:
                    await SearchMods();
                    break;
                case 4:
                    _status.Text = Localization.T("选择 .mrpack 或 CurseForge 整合包压缩包安装", "Pick a .mrpack or CurseForge modpack archive");
                    var pick = Ui.Button(Localization.T("选择整合包…", "Choose modpack…"), async (_, _) => await PickModpack());
                    _list.Items.Add(pick);
                    break;
                case 5:
                    await ListImports();
                    break;
            }
            _status.Text = "";
        }
        catch (Exception e)
        {
            _status.Text = e.Friendly();
        }
    }

    private async Task<string?> PromptGameVersion()
    {
        var instances = InstanceScanner.Scan(LauncherSettings.Shared.MinecraftFolder)
            .Where(i => !i.IsModded).Select(i => i.VanillaVersion).Distinct().ToList();
        var manifest = await VersionManifest.Fetch();
        var all = instances.Concat(manifest.Versions.Where(v => v.Kind == McVersionKind.Release).Take(30).Select(v => v.Id))
            .Distinct().ToList();
        var (ok, value) = await Ui.Prompt(XamlRoot, Localization.T("游戏版本", "Game version"), "1.21.1", _instances.Count > 0 ? _instances[0].VanillaVersion : "1.21.1");
        return ok ? value : null;
    }

    private StackPanel MakeVersionItem(RemoteVersion v)
    {
        var line = Ui.Col(Ui.Text(v.Id, 15, FontWeights.SemiBold),
            Ui.Text($"{v.Kind.Title()} · {v.ReleaseTime:yyyy-MM-dd}", 12));
        var btn = Ui.Button(Localization.T("安装", "Install"), async (_, _) => await InstallVanilla(v));
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(line, 0); Grid.SetColumn(btn, 1);
        row.Children.Add(line); row.Children.Add(btn);
        return new StackPanel { Children = { row }, Margin = new Thickness(0, 6, 0, 6) };
    }

    private StackPanel MakeLoaderItem(ModLoaderKind kind, string game, ModLoaderKindExtensions.LoaderVersion v)
    {
        var line = Ui.Col(Ui.Text($"{kind.Id()} {v.Version}{(v.Stable ? "" : " (beta)")}", 14),
            Ui.Text(Localization.T($"Minecraft {game}", $"Minecraft {game}"), 12));
        var btn = Ui.Button(Localization.T("安装", "Install"), async (_, _) =>
        {
            var remote = await VersionManifest.Find(game);
            await RunJob(Localization.T($"安装 {kind.Id()} {v.Version}", $"Install {kind.Id()} {v.Version}"),
                async job => await Installer.Install(remote, $"{game}-{kind.Id()} {v.Version}", (kind, v.Version),
                    LauncherSettings.Shared.MinecraftFolder, job));
        });
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(line, 0); Grid.SetColumn(btn, 1);
        row.Children.Add(line); row.Children.Add(btn);
        return new StackPanel { Children = { row }, Margin = new Thickness(0, 4, 0, 4) };
    }

    private StackPanel MakeForgeItem(ForgeKind kind, string game, ForgeInstaller.Version v)
    {
        var line = Ui.Col(Ui.Text($"{kind.Id()} {v.Display}{(v.IsBeta ? " (beta)" : "")}", 14),
            Ui.Text($"Minecraft {game}", 12));
        var btn = Ui.Button(Localization.T("安装", "Install"), async (_, _) =>
        {
            var remote = await VersionManifest.Find(game);
            await RunJob(Localization.T($"安装 {kind.Id()} {v.Display}", $"Install {kind.Id()} {v.Display}"),
                async job => await ForgeInstaller.Install(kind, v.VersionString, remote,
                    $"{game}-{kind.Id()}-{v.Display}", LauncherSettings.Shared.MinecraftFolder, job));
        });
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(line, 0); Grid.SetColumn(btn, 1);
        row.Children.Add(line); row.Children.Add(btn);
        return new StackPanel { Children = { row }, Margin = new Thickness(0, 4, 0, 4) };
    }

    private async Task InstallVanilla(RemoteVersion v)
    {
        var name = v.Id;
        var (ok, custom) = await Ui.Prompt(XamlRoot, Localization.T("版本名称", "Instance name"), v.Id, v.Id);
        if (ok && custom.Length > 0) name = custom;
        await RunJob(Localization.T($"安装 {name}", $"Install {name}"),
            async job => await Installer.Install(v, name, null, LauncherSettings.Shared.MinecraftFolder, job));
    }

    private async Task SearchMods()
    {
        var query = _searchBox.Text.Trim();
        if (query.Length == 0)
        {
            _status.Text = Localization.T("输入关键字搜索 Modrinth 与 CurseForge", "Type a keyword to search Modrinth and CurseForge");
            return;
        }
        var inst = _targetBox.SelectedIndex >= 0 ? _instances[_targetBox.SelectedIndex] : null;
        if (inst == null)
        {
            _status.Text = Localization.T("没有可安装 Mod 的版本", "No instance to install mods into");
            return;
        }
        // 中文名：中文/拼音搜索（如 机械动力 / jxdl）
        var slugHits = ModNames.Shared.Search(query);
        var results = new List<Modrinth.Project>();
        if (slugHits.Count > 0)
            results.AddRange(await Modrinth.Projects(slugHits, inst.VanillaVersion, inst.Loaders.FirstOrDefault()));
        results.AddRange(await Modrinth.Search(query, Modrinth.ProjectType.Mod, inst.VanillaVersion, inst.Loaders.FirstOrDefault()));
        try { results.AddRange(await CurseForge.Search(query, Modrinth.ProjectType.Mod, inst.VanillaVersion, inst.Loaders.FirstOrDefault())); }
        catch { }
        foreach (var p in results.GroupBy(p => p.Slug).Select(g => g.First()).Take(60))
        {
            var zh = ModNames.Shared.Chinese(p.Slug);
            var titleText = zh != null ? $"{p.TitleText}（{zh}）" : p.TitleText;
            var line = Ui.Col(Ui.Text(titleText, 14, FontWeights.SemiBold),
                Ui.Text($"{p.SourceKind} · {p.Downloads:N0} · {p.Description}", 12));
            var proj = p;
            var btn = Ui.Button(Localization.T("安装", "Install"), async (_, _) =>
                await RunJob(Localization.T($"安装 {proj.TitleText}", $"Install {proj.TitleText}"),
                    async job => await InstallMod(inst, proj, job)));
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(line, 0); Grid.SetColumn(btn, 1);
            row.Children.Add(line); row.Children.Add(btn);
            _list.Items.Add(new StackPanel { Children = { row }, Margin = new Thickness(0, 4, 0, 4) });
        }
    }

    private static async Task InstallMod(McInstance inst, Modrinth.Project p, Job job)
    {
        var versions = p.SourceKind == Modrinth.Source.CurseForge
            ? await CurseForge.Files(p, inst.VanillaVersion, inst.Loaders.FirstOrDefault())
            : await Modrinth.Versions(p.Id, inst.VanillaVersion, inst.Loaders.FirstOrDefault());
        var v = versions.FirstOrDefault(x => !x.Blocked)
                ?? throw new LauncherException(Localization.T("这个 Mod 不支持当前版本", "This mod doesn't support the game version"));
        var type = Modrinth.ProjectType.Mod;
        var dest = Path.Combine(inst.GameDir, type.Folder(), v.FileName);
        var item = new DownloadItem(v.FileUrl, dest, v.Sha1, v.Size);
        if (p.SourceKind == Modrinth.Source.CurseForge) item.Urls = CurseForge.DownloadUrls(v);
        await Downloader.Run(new[] { item }, job);
        // 自动补全前置
        try
        {
            var installed = Directory.EnumerateFiles(Path.Combine(inst.GameDir, "mods"), "*.jar*")
                .Select(f => new FileInfo(f)).ToList();
            var entries = await ModIndex.Identify(installed.Select(f => f.FullName).ToList());
            var deps = await ModIndex.RequiredDependencies(v, inst.VanillaVersion, inst.Loaders.FirstOrDefault(),
                ModIndex.InstalledProjects(entries));
            foreach (var (project, dep) in deps)
            {
                var depDest = Path.Combine(inst.GameDir, "mods", dep.FileName);
                var depItem = new DownloadItem(dep.FileUrl, depDest, dep.Sha1, dep.Size);
                if (dep.SourceKind == Modrinth.Source.CurseForge) depItem.Urls = CurseForge.DownloadUrls(dep);
                await Downloader.Run(new[] { depItem }, job);
                Log.log($"[Mod] 已补全前置 {project?.TitleText ?? dep.ProjectId} {dep.FileName}");
            }
        }
        catch (Exception e)
        {
            Log.log($"[Mod] 补全前置失败：{e.Friendly()}");
        }
    }

    private async Task PickModpack()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker,
            WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow!));
        picker.FileTypeFilter.Add(".mrpack");
        picker.FileTypeFilter.Add(".zip");
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;
        try
        {
            var info = Modpack.Inspect(file.Path);
            var (ok, name) = await Ui.Prompt(XamlRoot,
                Localization.T($"安装整合包 {info.Name}", $"Install modpack {info.Name}"),
                Localization.T("版本名称", "Instance name"), LauncherImport.AvailableName(info.Name, LauncherSettings.Shared.MinecraftFolder));
            if (!ok) return;
            var manual = await RunJob(Localization.T($"安装 {info.Name}", $"Install {info.Name}"),
                async job => await Modpack.Install(file.Path, name, LauncherSettings.Shared.MinecraftFolder, job));
            var left = Modpack.CollectManualDownloads(manual);
            await App.ShowInfo(App.MainAppWindow!, left.Count == 0
                ? Localization.T("整合包安装完成。", "Modpack installed.")
                : Localization.T(
                    $"{left.Count} 个文件需要手动下载（作者禁止第三方下载）：\n{string.Join("\n", left.Select(m => $"{m.Title} — {m.PageUrl}"))}\n下载后放入“下载”文件夹，再点一次安装即可自动收集。",
                    $"{left.Count} file(s) need manual download: {string.Join("\n", left.Select(m => m.Title))}"));
        }
        catch (Exception e)
        {
            await Ui.ShowError(XamlRoot, e);
        }
    }

    private async Task ListImports()
    {
        _status.Text = Localization.T("正在查找其他启动器…", "Looking for other launchers…");
        // PCL2 接管（手动触发）
        var pcl = await Task.Run(() => Pcl2.Detect());
        if (pcl != null && !LauncherSettings.Shared.Pcl2TakenOver)
        {
            var line = Ui.Col(Ui.Text("PCL2", 14, FontWeights.SemiBold),
                Ui.Text($"{pcl.GameFolder}（{pcl.VersionNames.Count} 个版本）", 12));
            var btn = Ui.Button(Localization.T("接管配置", "Take over settings"), async (_, _) =>
            {
                var r = Pcl2.TakeOver(pcl);
                await App.ShowInfo(App.MainAppWindow!, string.Join("\n", r.Applied));
                Reload();
            });
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(line, 0); Grid.SetColumn(btn, 1);
            row.Children.Add(line); row.Children.Add(btn);
            _list.Items.Add(new StackPanel { Children = { row }, Margin = new Thickness(0, 6, 0, 6) });
        }
        foreach (var c in await Task.Run(() => LauncherImport.Discover()))
        {
            var cand = c;
            var line = Ui.Col(Ui.Text($"{cand.Name} · {cand.Source.DisplayName()}", 14, FontWeights.SemiBold),
                Ui.Text(cand.Unsupported ?? $"{cand.Summary} · Mod {cand.ModCount} · 存档 {cand.WorldCount}" + (cand.Warning != null ? $"\n⚠ {cand.Warning}" : ""), 12));
            var btn = Ui.Button(Localization.T("导入", "Import"), async (_, _) =>
            {
                try
                {
                    if (cand.Unsupported != null) throw new LauncherException(cand.Unsupported);
                    var (ok, name) = await Ui.Prompt(XamlRoot, Localization.T("版本名称", "Instance name"),
                        cand.Name, LauncherImport.AvailableName(cand.Name, LauncherSettings.Shared.MinecraftFolder));
                    if (!ok) return;
                    await RunJob(Localization.T($"导入 {cand.Name}", $"Import {cand.Name}"),
                        async job => await LauncherImport.Run(cand, name, LauncherSettings.Shared.MinecraftFolder, job));
                    await App.ShowInfo(App.MainAppWindow!, Localization.T("导入完成。", "Imported."));
                }
                catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
            });
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(line, 0); Grid.SetColumn(btn, 1);
            row.Children.Add(line); row.Children.Add(btn);
            _list.Items.Add(new StackPanel { Children = { row }, Margin = new Thickness(0, 6, 0, 6) });
        }
        _status.Text = "";
    }

    private async Task<T> RunJob<T>(string title, Func<Job, Task<T>> work)
    {
        var job = MainWindow.NewJob(title);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            _progress.Value = job.Fraction;
            _status.Text = $"{job.Stage}  {job.FileText}  {Job.FormatSpeed(job.Speed)}";
        };
        timer.Start();
        try
        {
            var result = await work(job);
            job.State = Job.JobState.Success;
            _status.Text = Localization.T("完成 ✓", "Done ✓");
            return result;
        }
        catch (Exception e)
        {
            job.State = Job.JobState.Failed;
            job.Error = e.Friendly();
            _status.Text = "";
            await Ui.ShowError(XamlRoot, e);
            throw;
        }
        finally
        {
            timer.Stop();
        }
    }

    private async Task RunJob(string title, Func<Job, Task> work) =>
        await RunJob<object?>(title, async job => { await work(job); return null; });
}


