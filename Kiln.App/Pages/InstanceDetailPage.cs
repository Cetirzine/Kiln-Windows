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
            var play = Ui.Button(T("开始游戏", "Play"), async (_, _) => { try { await LaunchCoordinator.Launch(inst, new LaunchOptions()); } catch (OperationCanceledException) { } catch (Exception e) { await Ui.ShowError(XamlRoot, e); } });
            play.Content = Ui.Row(new FontIcon { Glyph = "\uE768", FontSize = 11 }, Ui.Text(T("开始游戏", "Play"), 12.5));
            play.Background = Ui.Brush("KilnAccent"); play.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            foreach (var element in ((FlowRow)play.Content).Children) { if (element is TextBlock text) text.Foreground = play.Foreground; if (element is FontIcon symbol) symbol.Foreground = play.Foreground; }
            Grid.SetColumn(play, 2); identity.Children.Add(play);
        var manageActions = new List<Button> {
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
            Ui.Button(Localization.T("导出启动脚本", "Export launch script"), async (_, _) => await ExportScript(inst)),
            Ui.Button(T("导出诊断包…", "Export diagnostics…"), async (_, _) => await ExportDiagnostics(inst)),
            Ui.Button(T("创建桌面快捷方式", "Create desktop shortcut"), (_, _) => SystemIntegration.CreateShortcut(inst)),
            Ui.Button(T("复制启动链接", "Copy launch link"), (_, _) => { var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText("kiln://launch/" + Uri.EscapeDataString(inst.Name)); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data); }) };
            var delete = Ui.Button(T("删除", "Delete"), async (_, _) => await DeleteInstance(inst));
            delete.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 194, 61, 61));
            manageActions.Add(delete);
            var management = new Grid { Name = "InstanceManagement", ColumnSpacing = 8, RowSpacing = 8 };
            for (var i = 0; i < 3; i++) { management.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); management.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
            for (var i = 0; i < manageActions.Count; i++)
            {
                var button = manageActions[i]; var title = button.Content?.ToString() ?? "";
                button.Content = new TextBlock { Text = title, FontSize = 12.5, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
                button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Center; button.MinWidth = 0; button.Height = 32;
                ToolTipService.SetToolTip(button, title); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, title);
                Grid.SetColumn(button, i % 3); Grid.SetRow(button, i / 3); management.Children.Add(button);
            }
            _detail.Children.Add(management);
            _detail.Spacing = 8;
            var folders = new Grid { Name = "InstanceFolders" };
            var locations = new[] { (T("版本文件夹", "Version folder"), inst.Dir), (T("游戏目录", "Game directory"), inst.GameDir), ("Mods", Path.Combine(inst.GameDir, "mods")), (T("存档", "Worlds"), Path.Combine(inst.GameDir, "saves")), (T("资源包", "Resource packs"), Path.Combine(inst.GameDir, "resourcepacks")), (T("光影", "Shaders"), Path.Combine(inst.GameDir, "shaderpacks")), (T("截图", "Screenshots"), Path.Combine(inst.GameDir, "screenshots")), (T("崩溃报告", "Crash reports"), Path.Combine(inst.GameDir, "crash-reports")) };
            for (var index = 0; index < locations.Length; index++)
            {
                var (title, path) = locations[index]; var folder = OpenFolderButton(path);
                var label = new TextBlock { Text = title, FontSize = 12.5, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
                folder.Content = InstanceCardUi.FileLabel(new FontIcon { Glyph = "\uE8B7", FontSize = 14, Foreground = Ui.Brush("KilnAccent") }, label, 16);
                folder.HorizontalContentAlignment = HorizontalAlignment.Stretch; folder.MinWidth = 0; folder.HorizontalAlignment = HorizontalAlignment.Stretch; folder.Height = 32; ToolTipService.SetToolTip(folder, path); folders.Children.Add(folder);
            }
            InstanceCardUi.Adapt(folders, 140);
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
        var worlds = InstanceManager.Worlds(inst);
        if (worlds.Count == 0) { var empty = Ui.Text(T("还没有存档", "No worlds yet"), 12); empty.Foreground = Ui.Brush("KilnTextSecondary"); empty.Margin = new Thickness(0, 8, 0, 8); _detail.Children.Add(empty); }
        foreach (var w in worlds)
        {
            var world = w;
            var playButton = Ui.Button(Localization.T("进入", "Play"), async (_, _) => await PlayWorld(inst, world.Name));
            playButton.IsEnabled = inst.SupportsQuickPlay;
            UIElement worldIcon = BlockIconControl.For("grass_block", 32);
            var imagePath = Path.Combine(world.Url, "icon.png");
            if (File.Exists(imagePath)) worldIcon = new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(imagePath)), Width = 32, Height = 32 };
            var time = Ui.Text(T("上次游玩：", "Last played: ") + world.LastPlayed.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), 11.5); time.Foreground = Ui.Brush("KilnTextSecondary");
            var worldName = Ui.Text(world.Name, 13, FontWeights.Medium); worldName.TextWrapping = TextWrapping.NoWrap; worldName.TextTrimming = TextTrimming.CharacterEllipsis;
            var worldLabels = Ui.Col(worldName, time); worldLabels.Spacing = 2;
            var row = Ui.RecordRow(
                InstanceCardUi.FileLabel(worldIcon, worldLabels),
                playButton,
                Ui.Button(Localization.T("导出…", "Export…"), async (_, _) => await ExportWorld(inst, world)),
                InstanceCardUi.IconButton("\uE8B7", T("打开文件夹", "Open folder"), async (_, _) => await Windows.System.Launcher.LaunchFolderPathAsync(world.Url)),
                InstanceCardUi.IconButton("\uE74D", T("删除", "Delete"), async (_, _) => await DeleteWorld(inst, world)));
            foreach (var button in ((StackPanel)row.Children[1]).Children.OfType<Button>()) if (button.Content is FontIcon) button.Width = 28;
            row.Name = "WorldRecord";
            _detail.Children.Add(row);
        }
        _detail.Spacing = 0;
        return Ui.Scroll(Ui.CardWithActions(T("存档", "Worlds") + " · " + worlds.Count, new[] {
            Ui.Button(T("导入…", "Import…"), async (_, _) => await ImportWorld(inst)),
            InstanceCardUi.IconButton("\uE8B7", T("打开文件夹", "Open folder"), async (_, _) => { var dir = Path.Combine(inst.GameDir, "saves"); Directory.CreateDirectory(dir); await Windows.System.Launcher.LaunchFolderPathAsync(dir); }) }, _detail));

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

    private Task ExportModpack(McInstance inst) => ModpackDialogs.Export(XamlRoot, inst);

    private async Task DeleteInstance(McInstance instance)
    {
        try
        {
            var dependents = InstanceManager.DependentsOf(instance);
            if (dependents.Count > 0) { await Ui.ShowError(XamlRoot, T("这些版本依赖它：", "These instances depend on it: ") + string.Join(", ", dependents)); return; }
            var dialog = new KilnDialog { XamlRoot = XamlRoot, Title = T("删除版本", "Delete instance"), Content = T($"确定删除 {instance.Name}？版本文件夹及其中的存档与 Mod 会移到回收站。", $"Delete {instance.Name}? Its folder, worlds and mods will be moved to the Recycle Bin."), PrimaryButtonText = T("移到回收站", "Move to Recycle Bin"), CloseButtonText = T("取消", "Cancel"), DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            Recycle.Delete(instance.Dir); MainWindow.Current!.NavigateTo("instances");
        }
        catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
    }

    private async Task ExportDiagnostics(McInstance instance)
    {
        var picker = new KilnFileSavePicker { SuggestedFileName = $"Kiln-diagnostics-{instance.Name}-{DateTime.Now:yyyyMMdd-HHmm}" };
        picker.FileTypeChoices.Add("ZIP", new List<string> { ".zip" });
        var file = await picker.PickSaveFileAsync(); if (file == null) return;
        try { await Diagnostics.Export(instance, null, file.Path); await App.ShowInfo(App.MainAppWindow!, T("诊断包已导出", "Diagnostics exported")); }
        catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
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

