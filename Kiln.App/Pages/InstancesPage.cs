using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

/// 版本页：实例列表与每个版本的管理（设置 / Mod / 存档 / 日志 / 补全文件）
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
        _instanceBox.SelectionChanged += (_, _) => UpdateDetail();
        UpdateDetail();
    }

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
        var s = LauncherSettings.Shared;
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

        // Mod 管理
        _detail.Children.Add(Ui.Text(Localization.T("Mod", "Mods"), 17, FontWeights.SemiBold));
        _detail.Children.Add(Ui.Row(
            Ui.Button(Localization.T("识别与检查更新", "Identify & check updates"), async (_, _) => await CheckModUpdates(inst)),
            Ui.Button(Localization.T("一键更新", "Update all"), async (_, _) => await UpdateAllMods(inst))));

        // 存档
        _detail.Children.Add(Ui.Text(Localization.T("存档", "Worlds"), 17, FontWeights.SemiBold));
        foreach (var w in InstanceManager.Worlds(inst).Take(12))
            _detail.Children.Add(Ui.Row(Ui.Text(w.Name, 13), Ui.Text(w.LastPlayed.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), 12)));
        _detail.Children.Add(Ui.Row(
            Ui.Button(Localization.T("导入存档…", "Import world…"), async (_, _) => await ImportWorld(inst))));
    }

    private async Task CheckModUpdates(McInstance inst)
    {
        var mods = new DirectoryInfo(Path.Combine(inst.GameDir, "mods"));
        if (!mods.Exists) { await App.ShowInfo(App.MainAppWindow!, Localization.T("没有 mods 文件夹。", "No mods folder.")); return; }
        var files = mods.EnumerateFiles("*.jar*").Select(f => f.FullName).ToList();
        var entries = await Task.Run(() => ModIndex.Identify(files));
        var updates = await ModIndex.Updates(entries.Values.ToList(), inst.VanillaVersion, inst.Loaders.FirstOrDefault());
        if (updates.Count == 0)
        {
            await App.ShowInfo(App.MainAppWindow!, Localization.T("所有 Mod 都是最新。", "All mods are up to date."));
            return;
        }
        foreach (var u in updates)
        {
            var zh = u.Project != null ? ModNames.Shared.Chinese(u.Project.Slug) : null;
            Log.log($"[Mod] 可更新：{u.Project?.TitleText ?? Path.GetFileName(u.File)}{(zh != null ? $"（{zh}）" : "")} → {u.Latest.FileName}");
        }
        await App.ShowInfo(App.MainAppWindow!, Localization.T(
            $"{updates.Count} 个 Mod 可更新（已写入日志）：\n{string.Join("\n", updates.Select(u => (u.Project?.TitleText ?? Path.GetFileName(u.File)) + " → " + u.Latest.Number))}",
            $"{updates.Count} mod(s) can be updated."));
    }

    private async Task UpdateAllMods(McInstance inst)
    {
        var mods = new DirectoryInfo(Path.Combine(inst.GameDir, "mods"));
        if (!mods.Exists) return;
        var files = mods.EnumerateFiles("*.jar*").Select(f => f.FullName).ToList();
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
    }

    private async Task ImportWorld(McInstance inst)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker,
            WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow!));
        picker.FileTypeFilter.Add(".zip");
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;
        try
        {
            var name = InstanceManager.ImportWorld(file.Path, inst);
            await App.ShowInfo(App.MainAppWindow!, Localization.T($"已导入存档 {name}。", $"Imported world {name}."));
        }
        catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
    }

    private async Task ExportModpack(McInstance inst)
    {
        var (ok, name) = await Ui.Prompt(XamlRoot, Localization.T("整合包名称", "Modpack name"), inst.Name, inst.Name);
        if (!ok) return;
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker,
            WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow!));
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
        WinRT.Interop.InitializeWithWindow.Initialize(picker,
            WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow!));
        picker.SuggestedFileName = inst.Name;
        picker.FileTypeChoices.Add("Batch (.bat)", new List<string> { ".bat" });
        var file = await picker.PickSaveFileAsync();
        if (file == null) return;
        try
        {
            var result = await Launcher.Launch(inst, account, new LaunchOptions { ExportScript = file.Path },
                MainWindow.NewJob("export"), new LaunchCallbacks());
            await App.ShowInfo(App.MainAppWindow!, Localization.T($"已导出 {file.Path}", $"Exported {file.Path}"));
        }
        catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
    }
}

