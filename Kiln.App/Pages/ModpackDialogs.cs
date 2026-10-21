using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

// Layout and fields follow ModpackUI.swift. Shared by local and downloaded packs.
internal sealed class ModpackExportForm : UserControl
{
    private readonly TextBox _name;
    private readonly TextBox _version = new() { Text = "1.0.0" };
    private readonly TextBox _summary = new();
    private readonly string _fallbackName;
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    public event Action? SelectionChanged;
    public IReadOnlySet<string> Selected => _selected;
    public string PackName => string.IsNullOrWhiteSpace(_name.Text) ? _fallbackName : _name.Text.Trim();
    public string Version => string.IsNullOrWhiteSpace(_version.Text) ? "1.0.0" : _version.Text.Trim();
    public string Summary => _summary.Text.Trim();
    public Control InitialFocus => _name;
    private static string T(string zh, string en) => Localization.T(zh, en);

    public ModpackExportForm(string name, IReadOnlyList<Modpack.ExportItem> items)
    {
        _fallbackName = name; _name = new TextBox { Text = name, Tag = "PackName" }; _version.Tag = "PackVersion"; _summary.Tag = "PackSummary";
        var fields = new Grid { ColumnSpacing = 10, RowSpacing = 8 };
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labels = new[] { T("名称", "Name"), T("版本", "Version"), T("简介", "Summary") };
        var inputs = new[] { _name, _version, _summary };
        for (var i = 0; i < inputs.Length; i++)
        {
            fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = Ui.Text(labels[i], 12); label.Foreground = Ui.Brush("KilnTextSecondary"); label.VerticalAlignment = VerticalAlignment.Center; Grid.SetRow(label, i); fields.Children.Add(label);
            var input = inputs[i]; input.MinWidth = 0; input.Height = 28; input.VerticalContentAlignment = VerticalAlignment.Center; Grid.SetRow(input, i); Grid.SetColumn(input, 1); fields.Children.Add(input);
        }
        _summary.PlaceholderText = T("可选", "Optional");
        var list = new StackPanel { Spacing = 0 };
        foreach (var item in items)
        {
            if (item.Suggested) _selected.Add(item.Path);
            var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(6, 5, 6, 5) };
            foreach (var width in new[] { 20d, 18, -1, 65, 68 }) row.ColumnDefinitions.Add(new ColumnDefinition { Width = width < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
            var toggle = new CheckBox { IsChecked = item.Suggested, Tag = item.Path, MinHeight = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, item.Path);
            toggle.Checked += (_, _) => { _selected.Add(item.Path); SelectionChanged?.Invoke(); }; toggle.Unchecked += (_, _) => { _selected.Remove(item.Path); SelectionChanged?.Invoke(); }; row.Children.Add(toggle);
            var icon = new FontIcon { Glyph = item.IsDirectory ? "\uE8B7" : "\uE7C3", FontSize = 14, Foreground = Ui.Brush("KilnTextSecondary") }; Grid.SetColumn(icon, 1); row.Children.Add(icon);
            var path = Ui.Text(item.Path, 12.5); path.TextWrapping = TextWrapping.NoWrap; path.TextTrimming = TextTrimming.CharacterEllipsis; path.VerticalAlignment = VerticalAlignment.Center; ToolTipService.SetToolTip(path, item.Path); Grid.SetColumn(path, 2); row.Children.Add(path);
            if (!item.Suggested) { var personal = Ui.Text(T("个人数据", "Personal"), 10.5); personal.Foreground = Ui.Brush("KilnTextSecondary"); personal.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(personal, 3); row.Children.Add(personal); }
            var size = Ui.Text(Job.FormatSize(item.Size), 11.5); size.Foreground = Ui.Brush("KilnTextSecondary"); size.VerticalAlignment = VerticalAlignment.Center; size.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(size, 4); row.Children.Add(size); list.Children.Add(row);
        }
        if (items.Count == 0) list.Children.Add(Ui.Text(T("没有可导出的内容。", "There is no content to export."), 12));
        var files = new Border { BorderBrush = Ui.Brush("KilnStroke"), BorderThickness = new Thickness(1), Background = Ui.Brush("KilnPanelSecondary"), Child = new ScrollViewer { Content = list, Height = 220, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        var hint = Ui.Text(T("在 Modrinth 上能找到的 Mod、资源包与光影只记录下载地址，不打包文件本身；其余文件直接打包。", "Mods, resource packs and shaders found on Modrinth are referenced by download link; everything else is packed."), 11.5); hint.Foreground = Ui.Brush("KilnTextSecondary");
        var body = Ui.Col(fields, Ui.Text(T("包含的内容", "Include"), 12, FontWeights.SemiBold), files, hint); body.Spacing = 12; Content = body;
    }
}

internal sealed class ModpackInstallForm : UserControl
{
    private readonly TextBox _name;
    private readonly TextBlock _warning = Ui.Text("", 11.5);
    private readonly string _folder;
    public string InstanceName => _name.Text.Trim();
    public bool IsValid => InstanceManager.ValidateName(InstanceName, _folder) == null;
    public Control InitialFocus => _name;
    public event Action? ValidationChanged;
    private static string T(string zh, string en) => Localization.T(zh, en);
    public ModpackInstallForm(Modpack.Info info, string folder)
    {
        _folder = folder;
        _name = new TextBox { Text = LauncherImport.AvailableName(info.Name, folder), MinWidth = 0, Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
        var labels = Ui.Col(Ui.Text(info.Name + " " + info.Version, 14, FontWeights.SemiBold)); labels.Spacing = 4;
        if (!string.IsNullOrWhiteSpace(info.Summary)) { var summary = Ui.Text(info.Summary, 12); summary.MaxLines = 2; summary.Foreground = Ui.Brush("KilnTextSecondary"); labels.Children.Add(summary); }
        var badges = Ui.Row(); badges.Spacing = 6;
        foreach (var text in new[] { "Minecraft " + info.GameVersion, info.Loader is { } loader ? loader.Kind + " " + loader.Version : T("原版", "Vanilla"), T($"{info.FileCount} 个文件", $"{info.FileCount} files"), info.FormatKind.ToString() })
            badges.Children.Add(new Border { Padding = new Thickness(5, 3, 5, 3), Background = Ui.Brush("KilnPanelSecondary"), BorderBrush = Ui.Brush("KilnStroke"), BorderThickness = new Thickness(1), Child = Ui.Text(text, 10.5) });
        labels.Children.Add(badges);
        var heading = new Grid { ColumnSpacing = 12 }; heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) }); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.Children.Add(new FontIcon { Glyph = "\uE7B8", FontSize = 26, Foreground = Ui.Brush("KilnTextSecondary") }); Grid.SetColumn(labels, 1); heading.Children.Add(labels);
        var hint = Ui.Text(T("整合包会安装为使用独立游戏目录的新版本。", "The modpack is installed as a new instance with its own game directory."), 11.5); hint.Foreground = Ui.Brush("KilnTextSecondary");
        Content = Ui.Col(heading, Ui.Text(T("版本名称", "Instance name"), 12, FontWeights.SemiBold), _name, _warning, hint);
        _name.TextChanged += (_, _) => Validate(); Validate();
    }
    private void Validate() { _warning.Text = InstanceManager.ValidateName(InstanceName, _folder) ?? ""; _warning.Visibility = _warning.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible; ValidationChanged?.Invoke(); }
}

internal static class ModpackDialogs
{
    private static string T(string zh, string en) => Localization.T(zh, en);
    public static async Task Export(XamlRoot root, McInstance instance)
    {
        try
        {
            var items = await Task.Run(() => Modpack.ExportItems(instance));
            var form = new ModpackExportForm(instance.Name, items);
            var dialog = new KilnDialog { XamlRoot = root, Title = T("导出整合包", "Export modpack"), Content = form, PreferredWidth = 520, SheetLayout = true, InitialFocus = form.InitialFocus, PrimaryButtonText = T("导出…", "Export…"), CloseButtonText = T("取消", "Cancel"), IsPrimaryButtonEnabled = form.Selected.Count > 0 };
            form.SelectionChanged += () => dialog.IsPrimaryButtonEnabled = form.Selected.Count > 0;
            while (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var suggested = string.Concat((form.PackName + "-" + form.Version).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                var paths = await KilnFileBrowser.Pick(false, true, false, new[] { ".mrpack" }, suggested); if (paths.FirstOrDefault() is not { } output) continue;
                var name = form.PackName; var version = form.Version; var summary = form.Summary; var include = form.Selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var job = MainWindow.NewJob(T($"导出整合包 {name}", $"Export modpack {name}"));
                try
                {
                    var result = await Task.Run(() => Modpack.Export(instance, name, version, summary, include, output, job));
                    job.State = Job.JobState.Success;
                    await App.ShowInfo(App.MainAppWindow!, T($"已导出：{result.Linked} 个文件引用 Modrinth，{result.Embedded} 个文件已打包。", $"Exported: {result.Linked} files linked from Modrinth, {result.Embedded} packed."));
                }
                catch (OperationCanceledException) { job.State = Job.JobState.Cancelled; }
                catch (Exception e) { job.State = Job.JobState.Failed; job.Error = e.Friendly(); await Ui.ShowError(root, e); }
                return;
            }
        }
        catch (Exception e) { await Ui.ShowError(root, e); }
    }
}
