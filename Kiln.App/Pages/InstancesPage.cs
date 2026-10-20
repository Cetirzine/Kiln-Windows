using Kiln.App.DotScene;
using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

/// 版本页：实例列表与每个版本的管理（设置 / Mod / 资源包与光影 / 存档 / 截图 / 补全文件 / 导出）
/// 对应原版 InstancePages + InstanceContent
public sealed class InstancesPage : Page
{
    private readonly ComboBox _instanceBox = new() { MinWidth = 300 };
    private readonly StackPanel _detail = new() { Spacing = 12 };

    private List<McInstance> _instances = new();

    public InstancesPage()
    {
        BuildUi();
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        _instances = InstanceScanner.Scan(LauncherSettings.Shared.MinecraftFolder).ToList();
        _instanceBox.Items.Clear();
        foreach (var i in _instances) _instanceBox.Items.Add(i.Name);
        var selected = _instances.FirstOrDefault(i => i.Name == LauncherSettings.Shared.SelectedInstance)
                       ?? _instances.FirstOrDefault();
        _instanceBox.SelectedIndex = _instances.IndexOf(selected!);
        if (!_wired)
        {
            _wired = true;
            _instanceBox.SelectionChanged += (_, _) => UpdateDetail();
        }
        UpdateDetail();
    }

    private bool _wired;

    private McInstance? Current =>
        _instanceBox.SelectedIndex >= 0 && _instanceBox.SelectedIndex < _instances.Count
            ? _instances[_instanceBox.SelectedIndex]
            : null;

    private void BuildUi()
    {
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(0, 16, 0, 0) };
        panel.Children.Add(Ui.Text(Localization.T("版本", "Instances"), 26, FontWeights.SemiBold));
        panel.Children.Add(_instanceBox);
        panel.Children.Add(_detail);
        Content = new ScrollViewer { Content = panel };
    }

    private void UpdateDetail()
    {
        _detail.Children.Clear();
        var inst = Current;
        if (inst == null)
        {
            _detail.Children.Add(Ui.Text(Localization.T("没有版本。", "No instances."), 14));
            return;
        }
        _detail.Children.Add(Ui.Text(inst.Description, 15, FontWeights.SemiBold));
        var setup = inst.Setup;
        var s = LauncherSettings.Shared;

        // 基本操作
        _detail.Children.Add(Ui.Row(
            Ui.Button(Localization.T("重命名", "Rename"), async (_, _) =>
            {
                var (ok, name) = await Ui.Prompt(XamlRoot, Localization.T("重命名", "Rename"), inst.Name, inst.Name);
                if (!ok) return;
                try { InstanceManager.Rename(inst, name); MainWindow.Current!.ReloadCurrentPage(); }
                catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
            }),
            Ui.Button(Localization.T("复制", "Duplicate"), async (_, _) =>
            {
                var (ok, name) = await Ui.Prompt(XamlRoot, Localization.T("复制为新版本", "Duplicate as"), inst.Name, inst.Name + " copy");
                if (!ok) return;
                try { InstanceManager.Duplicate(inst, name); MainWindow.Current!.ReloadCurrentPage(); }
                catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
            }),
            Ui.Button(Localization.T("补全文件", "Repair files"), async (_, _) =>
            {
                var job = MainWindow.NewJob(Localization.T($"补全 {inst.Name}", $"Repair {inst.Name}"));
                try
                {
                    await Installer.CompleteFiles(inst, job, quick: false);
                    await App.ShowInfo(App.MainAppWindow!, Localization.T("文件已补全。", "Files repaired."));
                }
                catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
            }),
            Ui.Button(Localization.T("导出整合包", "Export modpack"), async (_, _) => await ExportModpack(inst)),
            Ui.Button(Localization.T("导出启动脚本", "Export launch script"), async (_, _) => await ExportScript(inst))));

        // 版本设置
        _detail.Children.Add(Ui.Text(Localization.T("版本设置", "Instance settings"), 17, FontWeights.SemiBold));
        var iconButton = Ui.Button(Localization.T("更换图标…", "Change icon…"), async (_, _) =>
        {
            var picked = await IconPicker.Pick(XamlRoot, inst.Icon);
            if (picked == null) return;
            var st = inst.Setup; st.Icon = picked; s.SaveInstanceSetup(st, inst.Dir);
            await BlockTextures.Shared.Ensure(new[] { picked });
            UpdateDetail();
        });
        _detail.Children.Add(Ui.Setting(Localization.T("图标", "Icon"),
            Ui.Row(iconButton, BlockIconControl.For(inst.Icon, 40))));
        _detail.Children.Add(Ui.Setting(Localization.T("版本隔离", "Separate game directory"),
            Ui.Toggle(inst.Isolated, Localization.T("隔离", "Separate"), Localization.T("共用", "Shared"), v =>
            {
                var st = inst.Setup; st.Isolation = v; s.SaveInstanceSetup(st, inst.Dir);
            })));
        _detail.Children.Add(Ui.Setting(Localization.T("内存（GB，0 = 自动）", "Memory (GB, 0 = auto)"),
            Ui.Num(setup.MemoryGB ?? 0, 0, 64, 0.5, v =>
            {
                var st = inst.Setup; st.MemoryGB = v > 0 ? v : null; s.SaveInstanceSetup(st, inst.Dir);
            })));
        _detail.Children.Add(Ui.Setting(Localization.T("固定账号", "Pinned account"),
            Ui.Combo(new[] { Localization.T("跟随当前账号", "Follow current") }.Concat(AccountStore.Shared.Accounts.Select(a => a.Name)).ToArray(),
                setup.Account == null ? 0 : AccountStore.Shared.Accounts.FindIndex(a => a.Id == setup.Account) + 1,
                i =>
                {
                    var st = inst.Setup;
                    st.Account = i <= 0 ? null : AccountStore.Shared.Accounts[i - 1].Id;
                    s.SaveInstanceSetup(st, inst.Dir);
                })));
        _detail.Children.Add(Ui.Setting(Localization.T("JVM 参数", "JVM arguments"),
            Ui.Box(setup.JvmArgs ?? "", "-Xmx 等由 Kiln 自动调优", v =>
            {
                var st = inst.Setup; st.JvmArgs = v.Length > 0 ? v : null; s.SaveInstanceSetup(st, inst.Dir);
            })));
        _detail.Children.Add(Ui.Setting(Localization.T("游戏参数", "Game arguments"),
            Ui.Box(setup.GameArgs ?? "", "--width 1280 …", v =>
            {
                var st = inst.Setup; st.GameArgs = v.Length > 0 ? v : null; s.SaveInstanceSetup(st, inst.Dir);
            })));
        _detail.Children.Add(Ui.Setting(Localization.T("进入服务器（地址[:端口]）", "Join server (host[:port])"),
            Ui.Box(setup.ServerEnter ?? "", "mc.example.com", v =>
            {
                var st = inst.Setup; st.ServerEnter = v.Length > 0 ? v : null; s.SaveInstanceSetup(st, inst.Dir);
            })));

        // Mod 管理（识别 / 启用禁用 / 删除 / 更新）
        _detail.Children.Add(Ui.Text(Localization.T("Mod", "Mods"), 17, FontWeights.SemiBold));
        _detail.Children.Add(Ui.Row(
            Ui.Button(Localization.T("刷新与检查更新", "Refresh & check updates"), async (_, _) => await ModsSection(inst, checkUpdates: true)),
            Ui.Button(Localization.T("一键更新", "Update all"), async (_, _) => await UpdateAllMods(inst)),
            OpenFolderButton(Path.Combine(inst.GameDir, "mods"))));
        _ = ModsSection(inst, checkUpdates: false);

        // 资源包 / 光影
        foreach (var (title, folder) in new[]
                 {
                     (Localization.T("资源包", "Resource packs"), "resourcepacks"),
                     (Localization.T("光影", "Shaders"), "shaderpacks"),
                 })
        {
            var dir = Path.Combine(inst.GameDir, folder);
            _detail.Children.Add(Ui.Text($"{title}" + (Directory.Exists(dir) ? $" · {Directory.GetFiles(dir).Length}" : ""), 17, FontWeights.SemiBold));
            _detail.Children.Add(Ui.Row(
                Ui.Button(Localization.T("添加文件…", "Add files…"), async (_, _) =>
                {
                    var picker = new Windows.Storage.Pickers.FileOpenPicker();
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow!));
                    picker.FileTypeFilter.Add(".zip");
                    var file = await picker.PickSingleFileAsync();
                    if (file == null) return;
                    Directory.CreateDirectory(dir);
                    File.Copy(file.Path, Path.Combine(dir, file.Name), overwrite: true);
                    UpdateDetail();
                }),
                OpenFolderButton(dir)));
        }

        // 存档（直接进入 / 导出 / 删除 / 导入）
        _detail.Children.Add(Ui.Text(Localization.T("存档", "Worlds"), 17, FontWeights.SemiBold));
        foreach (var w in InstanceManager.Worlds(inst))
        {
            var world = w;
            var playButton = Ui.Button(Localization.T("进入", "Play"), async (_, _) => await PlayWorld(inst, world.Name));
            playButton.IsEnabled = inst.SupportsQuickPlay;
            _detail.Children.Add(Ui.Row(
                Ui.Text(world.Name, 13),
                Ui.Text(world.LastPlayed.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), 11),
                playButton,
                Ui.Button(Localization.T("导出…", "Export…"), async (_, _) => await ExportWorld(inst, world)),
                Ui.Button(Localization.T("删除", "Delete"), async (_, _) => await DeleteWorld(inst, world))));
        }
        _detail.Children.Add(Ui.Row(
            Ui.Button(Localization.T("导入存档…", "Import world…"), async (_, _) => await ImportWorld(inst)),
            OpenFolderButton(Path.Combine(inst.GameDir, "saves"))));

        // 截图
        var shots = Path.Combine(inst.GameDir, "screenshots");
        _detail.Children.Add(Ui.Text(Localization.T("截图", "Screenshots"), 17, FontWeights.SemiBold));
        try
        {
            var files = Directory.GetFiles(shots, "*.png").OrderByDescending(f => File.GetLastWriteTime(f)).Take(8).ToList();
            if (files.Count == 0) _detail.Children.Add(Ui.Text(Localization.T("还没有截图", "No screenshots"), 12));
            else _detail.Children.Add(Ui.Row(OpenFolderButton(shots)));
        }
        catch { }
    }

    private static Button OpenFolderButton(string dir) => Ui.Button(Localization.T("打开文件夹", "Open folder"), (_, _) =>
    {
        try { Directory.CreateDirectory(dir); _ = Windows.System.Launcher.LaunchUriAsync(new Uri(dir)); }
        catch { }
    });

    /// Mod 列表：识别出名称（含中文名）与版本，可启停与删除
    private async Task ModsSection(McInstance inst, bool checkUpdates)
    {
        var dir = Path.Combine(inst.GameDir, "mods");
        if (!Directory.Exists(dir)) return;
        var files = Directory.GetFiles(dir, "*.jar*").OrderBy(f => f).ToList();
        if (files.Count == 0) return;
        var entries = await Task.Run(() => ModIndex.Identify(files));
        var panel = new StackPanel { Spacing = 4 };
        var anyUpdate = false;
        foreach (var file in files)
        {
            var entry = entries.GetValueOrDefault(file);
            var project = entry?.Project;
            var zh = project != null ? ModNames.Shared.Chinese(project.Slug) : null;
            var name = project != null
                ? (zh != null ? $"{project.TitleText}（{zh}）" : project.TitleText)
                : Path.GetFileNameWithoutExtension(file);
            var versionText = entry?.Version != null
                ? $"{entry.Version.SourceKind} {entry.Version.Number}"
                : Localization.T("未识别", "Unrecognized");
            var disabled = file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            var f = file;
            var row = Ui.Row(
                Ui.Text((disabled ? "⛔ " : "") + name, 13),
                Ui.Text(versionText, 11),
                Ui.Button(disabled ? Localization.T("启用", "Enable") : Localization.T("禁用", "Disable"), (_, _) =>
                {
                    var target = disabled ? f[..^".disabled".Length] : f + ".disabled";
                    try { File.Move(f, target); UpdateDetail(); } catch { }
                }),
                Ui.Button(Localization.T("删除", "Delete"), async (_, _) =>
                {
                    try { Recycle.Delete(f); UpdateDetail(); }
                    catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
                }));
            panel.Children.Add(row);
        }
        if (checkUpdates)
        {
            var updates = await ModIndex.Updates(entries.Values.ToList(), inst.VanillaVersion, inst.Loaders.FirstOrDefault());
            anyUpdate = updates.Count > 0;
            if (updates.Count == 0)
                await App.ShowInfo(App.MainAppWindow!, Localization.T("所有 Mod 都是最新。", "All mods are up to date."));
            else
                await App.ShowInfo(App.MainAppWindow!, Localization.T(
                    $"{updates.Count} 个 Mod 可更新：\n{string.Join("\n", updates.Select(u => (u.Project?.TitleText ?? Path.GetFileName(u.File)) + " → " + u.Latest.Number))}",
                    $"{updates.Count} mod(s) can be updated."));
        }
        // 插到 Mod 标题之后（跳过标题与按钮行）
        var index = 0;
        foreach (var child in _detail.Children)
        {
            index++;
            if (child is TextBlock t && t.Text == Localization.T("Mod", "Mods")) break;
        }
        _detail.Children.Insert(Math.Min(index + 1, _detail.Children.Count), panel);
    }

    private async Task UpdateAllMods(McInstance inst)
    {
        var dir = Path.Combine(inst.GameDir, "mods");
        if (!Directory.Exists(dir)) return;
        var files = Directory.GetFiles(dir, "*.jar*").ToList();
        var entries = await Task.Run(() => ModIndex.Identify(files));
        var updates = await ModIndex.Updates(entries.Values.ToList(), inst.VanillaVersion, inst.Loaders.FirstOrDefault());
        var job = MainWindow.NewJob(Localization.T("更新 Mod", "Update mods"));
        var count = 0;
        foreach (var u in updates)
        {
            try { await ModIndex.Apply(u, job); count++; }
            catch (Exception e) { Log.log($"[Mod] 更新失败 {u.File}：{e.Friendly()}"); }
        }
        await App.ShowInfo(App.MainAppWindow!, Localization.T($"已更新 {count} 个 Mod。", $"Updated {count} mod(s)."));
        UpdateDetail();
    }

    private async Task PlayWorld(McInstance inst, string world)
    {
        var account = AccountStore.Shared.Selected ?? AccountStore.Shared.AccountFor(inst.Setup);
        if (account == null)
        {
            await Ui.ShowError(XamlRoot, Localization.T("请先在“账号”页添加账号。", "Add an account first."));
            return;
        }
        try
        {
            var job = MainWindow.NewJob(Localization.T($"启动 {inst.Name}", $"Play {inst.Name}"));
            await Launcher.Launch(inst, account, new LaunchOptions { World = world }, job, new LaunchCallbacks());
            MainWindow.Current!.NavigateTo("home");
        }
        catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
    }

    private async Task ExportWorld(McInstance inst, InstanceManager.World world)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow!));
        picker.SuggestedFileName = world.Name;
        picker.FileTypeChoices.Add("zip", new List<string> { ".zip" });
        var file = await picker.PickSaveFileAsync();
        if (file == null) return;
        try
        {
            InstanceManager.ExportWorld(world, file.Path);
            await App.ShowInfo(App.MainAppWindow!, Localization.T($"已导出 {file.Path}", $"Exported {file.Path}"));
        }
        catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
    }

    private async Task DeleteWorld(McInstance inst, InstanceManager.World world)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Localization.T("删除存档", "Delete world"),
            Content = Localization.T($"确定要删除存档 {world.Name} 吗？存档会被移到回收站。", $"Delete the world {world.Name}? It will be moved to the Recycle Bin."),
            PrimaryButtonText = Localization.T("删除", "Delete"),
            CloseButtonText = Localization.T("取消", "Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try { Recycle.Delete(world.Url); UpdateDetail(); }
        catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
    }

    private async Task ImportWorld(McInstance inst)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow!));
        picker.FileTypeFilter.Add(".zip");
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;
        try
        {
            var name = InstanceManager.ImportWorld(file.Path, inst);
            await App.ShowInfo(App.MainAppWindow!, Localization.T($"已导入存档 {name}。", $"Imported world {name}."));
            UpdateDetail();
        }
        catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
    }

    private async Task ExportModpack(McInstance inst)
    {
        var (ok, name) = await Ui.Prompt(XamlRoot, Localization.T("整合包名称", "Modpack name"), inst.Name, inst.Name);
        if (!ok) return;
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow!));
        picker.SuggestedFileName = name;
        picker.FileTypeChoices.Add("Modrinth modpack (.mrpack)", new List<string> { ".mrpack" });
        var file = await picker.PickSaveFileAsync();
        if (file == null) return;
        var include = Modpack.ExportItems(inst).Where(i => i.Suggested).Select(i => i.Path).ToHashSet();
        var job = MainWindow.NewJob(Localization.T("导出整合包", "Export modpack"));
        var r = await Modpack.Export(inst, name, "1.0", "", include, file.Path, job);
        await App.ShowInfo(App.MainAppWindow!, Localization.T(
            $"导出完成：{r.Linked} 个文件以下载地址引用，{r.Embedded} 个直接打包。",
            $"Exported: {r.Linked} linked, {r.Embedded} embedded."));
    }

    private async Task ExportScript(McInstance inst)
    {
        var account = AccountStore.Shared.Selected;
        if (account == null) { await Ui.ShowError(XamlRoot, Localization.T("请先在“账号”页添加账号。", "Add an account first.")); return; }
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow!));
        picker.SuggestedFileName = inst.Name;
        picker.FileTypeChoices.Add("Batch (.bat)", new List<string> { ".bat" });
        var file = await picker.PickSaveFileAsync();
        if (file == null) return;
        try
        {
            await Launcher.Launch(inst, account, new LaunchOptions { ExportScript = file.Path },
                MainWindow.NewJob("export"), new LaunchCallbacks());
            await App.ShowInfo(App.MainAppWindow!, Localization.T($"已导出 {file.Path}", $"Exported {file.Path}"));
        }
        catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
    }
}
