using Kiln.Core;
using Kiln.App.DotScene;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

public sealed class InstanceDetailPage : Page
{
    private readonly McInstance _instance;
    private StackPanel _detail = new();
    private static string T(string zh, string en) => Localization.T(zh, en);
    public InstanceDetailPage(McInstance instance)
    {
        _instance = instance;
        Content = Ui.Tabs("instance", instance.Name,
            (T("概览", "Overview"), () => BuildSection("overview")),
            ("Mod", () => new ModManager(instance)),
            (T("存档", "Worlds"), () => BuildSection("worlds")),
            (T("资源包", "Resource packs"), () => new ContentFilesPage(instance, "resourcepacks")),
            (T("光影", "Shaders"), () => new ContentFilesPage(instance, "shaderpacks")),
            (T("截图", "Screenshots"), () => new ContentFilesPage(instance, "screenshots")),
            (T("设置", "Settings"), () => BuildSection("settings")));
    }
    private void UpdateDetail() => MainWindow.Current!.ShowInstance(_instance);
    private UIElement BuildSection(string section)
    {
        var inst = _instance;
        var setup = inst.Setup;
        var s = LauncherSettings.Shared;
        _detail = new StackPanel { Spacing = 12 };
        if (section == "overview")
        {
            var identity = new Grid { ColumnSpacing = 14 };
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) }); identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); identity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = Ui.Button("", async (_, _) => { var picked = await IconPicker.Pick(XamlRoot, inst.Icon); if (picked == null) return; var current = inst.Setup; current.Icon = string.IsNullOrEmpty(picked) ? null : picked; s.SaveInstanceSetup(current, inst.Dir); await BlockTextures.Shared.Ensure(new[] { picked }); UpdateDetail(); }); icon.Content = BlockIconControl.For(inst.Icon, 48); icon.Padding = new Thickness(0); icon.BorderThickness = new Thickness(0); identity.Children.Add(icon);
            var description = Ui.Text(inst.Description, 12); description.Foreground = Ui.Brush("KilnTextSecondary");
            var nameLabel = Ui.Text(inst.Name, 17, FontWeights.Bold); nameLabel.MaxLines = 1; nameLabel.TextTrimming = TextTrimming.CharacterEllipsis;
            var labels = Ui.Col(nameLabel, description); labels.Spacing = 4;
            if (McInstance.FormatPlayTime(setup.PlayTime) is { } played) { var time = Ui.Text(T($"已玩 {played}", $"Played for {played}"), 12); time.Foreground = Ui.Brush("KilnTextSecondary"); labels.Children.Add(time); }
            var badges = Ui.Row(); badges.Spacing = 6;
            void Badge(string text) => badges.Children.Add(new Border { Padding = new Thickness(6, 3, 6, 3), Background = Ui.Brush("KilnPanelSecondary"), BorderBrush = Ui.Brush("KilnStroke"), BorderThickness = new Thickness(1), Child = Ui.Text(text, 11) });
            Badge($"Java {inst.JavaMajor}+"); if (inst.Isolated) Badge(T("独立游戏目录", "Separate game directory")); labels.Children.Add(badges); Grid.SetColumn(labels, 1); identity.Children.Add(labels);
            var play = Ui.Button(T("开始游戏", "Play"), async (_, _) => { try { await LaunchCoordinator.Launch(inst, new LaunchOptions()); } catch (OperationCanceledException) { } catch (Exception e) { await Ui.ShowError(XamlRoot, e); } }); play.Background = Ui.Brush("KilnAccent"); play.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White); Grid.SetColumn(play, 2); identity.Children.Add(play);
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
                    job.State = Job.JobState.Success;
                    await App.ShowInfo(App.MainAppWindow!, Localization.T("文件已补全。", "Files repaired."));
                }
                catch (OperationCanceledException) { job.State = Job.JobState.Cancelled; }
                catch (Exception e) { job.State = Job.JobState.Failed; job.Error = e.Friendly(); await Ui.ShowError(XamlRoot, e); }
            }),
            Ui.Button(Localization.T("导出整合包", "Export modpack"), async (_, _) => await ExportModpack(inst)),
            Ui.Button(Localization.T("导出启动脚本", "Export launch script"), async (_, _) => await ExportScript(inst))));
            _detail.Children.Add(Ui.Row(
                Ui.Button(T("创建桌面快捷方式", "Create desktop shortcut"), (_, _) => SystemIntegration.CreateShortcut(inst)),
                Ui.Button(T("复制启动链接", "Copy launch link"), (_, _) => { var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText("kiln://launch/" + Uri.EscapeDataString(inst.Name)); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data); })));
            var folders = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
            for (var column = 0; column < 4; column++) folders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var locations = new[] { (T("版本文件夹", "Version folder"), inst.Dir), (T("游戏目录", "Game directory"), inst.GameDir), ("Mods", Path.Combine(inst.GameDir, "mods")), (T("存档", "Worlds"), Path.Combine(inst.GameDir, "saves")), (T("资源包", "Resource packs"), Path.Combine(inst.GameDir, "resourcepacks")), (T("光影", "Shaders"), Path.Combine(inst.GameDir, "shaderpacks")), (T("截图", "Screenshots"), Path.Combine(inst.GameDir, "screenshots")), (T("崩溃报告", "Crash reports"), Path.Combine(inst.GameDir, "crash-reports")) };
            for (var index = 0; index < locations.Length; index++)
            {
                if (index % 4 == 0) folders.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var (title, path) = locations[index]; var folder = OpenFolderButton(path); folder.Content = new TextBlock { Text = title, FontSize = 12, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis }; folder.MinWidth = 0; folder.HorizontalAlignment = HorizontalAlignment.Stretch; folder.Height = 34; ToolTipService.SetToolTip(folder, path); Grid.SetRow(folder, index / 4); Grid.SetColumn(folder, index % 4); folders.Children.Add(folder);
            }
            return Ui.Scroll(Ui.Card("", identity), Ui.Card(T("文件夹", "Folders"), folders), Ui.Card(T("管理", "Manage"), _detail));
        }
        if (section == "settings")
        {        // 版本设置
        var iconButton = Ui.Button(Localization.T("更换图标…", "Change icon…"), async (_, _) =>
        {
            var picked = await IconPicker.Pick(XamlRoot, inst.Icon);
            if (picked == null) return;
            var st = inst.Setup; st.Icon = string.IsNullOrEmpty(picked) ? null : picked; s.SaveInstanceSetup(st, inst.Dir);
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
        _detail.Children.Add(Ui.Setting(T("Java", "Java"), JavaPicker(inst)));
        _detail.Children.Add(Ui.Setting(T("版本类型文字", "Version type text"),
            Ui.Box(setup.CustomInfo ?? "", T("跟随全局设置", "Use global setting"), value =>
            {
                var st = inst.Setup; st.CustomInfo = string.IsNullOrWhiteSpace(value) ? null : value; s.SaveInstanceSetup(st, inst.Dir);
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

        }
        if (section == "worlds")
        {        // 存档（直接进入 / 导出 / 删除 / 导入）
        if (InstanceManager.Worlds(inst).Count == 0) _detail.Children.Add(Ui.Text(T("还没有存档，可导入已有的存档或在游戏中创建。", "No worlds yet. Import a world or create one in game."), 12));
        foreach (var w in InstanceManager.Worlds(inst))
        {
            var world = w;
            var playButton = Ui.Button(Localization.T("进入", "Play"), async (_, _) => await PlayWorld(inst, world.Name));
            playButton.IsEnabled = inst.SupportsQuickPlay;
            _detail.Children.Add(Ui.RecordRow(
                Ui.Col(Ui.Text(world.Name, 13), Ui.Text(world.LastPlayed.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), 11)),
                playButton,
                Ui.Button(Localization.T("导出…", "Export…"), async (_, _) => await ExportWorld(inst, world)),
                Ui.Button(Localization.T("删除", "Delete"), async (_, _) => await DeleteWorld(inst, world))));
        }
        _detail.Children.Add(Ui.Row(
            Ui.Button(Localization.T("导入存档…", "Import world…"), async (_, _) => await ImportWorld(inst)),
            OpenFolderButton(Path.Combine(inst.GameDir, "saves"))));

        }
        return new ScrollViewer { Content = Ui.Card(section == "settings" ? T("版本设置", "Instance settings") : section == "worlds" ? T("存档", "Worlds") : T("概览", "Overview"), _detail) };
    }    private static Button OpenFolderButton(string dir) => Ui.Button(Localization.T("打开文件夹", "Open folder"), (_, _) =>
    {
        try { Directory.CreateDirectory(dir); _ = Windows.System.Launcher.LaunchUriAsync(new Uri(dir)); }
        catch { }
    });

    private async Task PlayWorld(McInstance inst, string world)
    {
        var account = AccountStore.Shared.AccountFor(inst.Setup);
        if (account == null)
        {
            await Ui.ShowError(XamlRoot, Localization.T("请先在“账号”页添加账号。", "Add an account first."));
            return;
        }
        try
        {
            await LaunchCoordinator.Launch(inst, new LaunchOptions { World = world });
            MainWindow.Current!.NavigateTo("home");
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
    }

    private ComboBox JavaPicker(McInstance instance)
    {
        var picker = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var updating = false;
        void Populate(IReadOnlyList<JavaInfo> runtimes, string? selected)
        {
            updating = true;
            try
            {
                picker.Items.Clear(); picker.Items.Add(new ComboBoxItem { Content = T("跟随全局设置", "Use global setting"), Tag = "" });
                foreach (var java in runtimes) picker.Items.Add(new ComboBoxItem { Content = java.DisplayName, Tag = java.Path });
                var index = picker.Items.Cast<ComboBoxItem>().ToList().FindIndex(item => string.Equals((string)item.Tag, selected ?? "", StringComparison.OrdinalIgnoreCase));
                if (index < 0 && !string.IsNullOrEmpty(selected)) { picker.Items.Add(new ComboBoxItem { Content = selected, Tag = selected }); index = picker.Items.Count - 1; }
                picker.SelectedIndex = Math.Max(0, index);
            }
            finally { updating = false; }
        }
        Populate(Array.Empty<JavaInfo>(), instance.Setup.JavaPath);
        picker.SelectionChanged += (_, _) =>
        {
            if (updating || picker.SelectedItem is not ComboBoxItem item) return;
            var setup = instance.Setup; setup.JavaPath = item.Tag is string { Length: > 0 } path ? path : null;
            LauncherSettings.Shared.SaveInstanceSetup(setup, instance.Dir);
        };
        async Task Load()
        {
            try { var runtimes = await JavaManager.Search(); Populate(runtimes, (picker.SelectedItem as ComboBoxItem)?.Tag as string); }
            catch (Exception e) { Log.log("[JavaPicker] " + e.Friendly()); }
        }
        _ = Load(); return picker;
    }

    private async Task ExportWorld(McInstance inst, InstanceManager.World world)
    {
        var picker = new KilnFileSavePicker();
        
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
        var dialog = new KilnDialog
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
        var picker = new KilnFileOpenPicker();
        
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
        var picker = new KilnFileSavePicker();
        
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
        var account = AccountStore.Shared.AccountFor(inst.Setup);
        if (account == null) { await Ui.ShowError(XamlRoot, Localization.T("请先在“账号”页添加账号。", "Add an account first.")); return; }
        var picker = new KilnFileSavePicker();
        
        picker.SuggestedFileName = inst.Name;
        picker.FileTypeChoices.Add("Batch (.bat)", new List<string> { ".bat" });
        var file = await picker.PickSaveFileAsync();
        if (file == null) return;
        try
        {
            await LaunchCoordinator.Launch(inst, new LaunchOptions { ExportScript = file.Path });
            await App.ShowInfo(App.MainAppWindow!, Localization.T($"已导出 {file.Path}", $"Exported {file.Path}"));
        }
        catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
    }
}

