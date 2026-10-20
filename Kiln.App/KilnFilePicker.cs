using Kiln.App.Pages;
using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace Kiln.App;

internal static class KilnFileBrowser
{
    private static string _lastFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static async Task<string[]> Pick(bool folders, bool save, bool multiple, IReadOnlyList<string> filters, string name = "")
    {
        var root = App.MainAppWindow!.Content.XamlRoot;
        var path = new TextBox { Text = Directory.Exists(_lastFolder) ? _lastFolder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var fileName = new TextBox { Text = name, PlaceholderText = Localization.T("文件名", "File name"), MinWidth = 0 };
        var status = Ui.Text("", 11.5);
        var list = new ListView { Height = 280, IsItemClickEnabled = false, SelectionMode = multiple ? ListViewSelectionMode.Multiple : ListViewSelectionMode.Single };
        var body = new Grid { RowSpacing = 10, MinWidth = 620 };
        for (var i = 0; i < 4; i++) body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string current = path.Text;
        void Navigate(string folder)
        {
            try
            {
                folder = Path.GetFullPath(Environment.ExpandEnvironmentVariables(folder));
                if (!Directory.Exists(folder)) throw new IOException(Localization.T("文件夹不存在", "Folder does not exist"));
                var entries = Directory.EnumerateFileSystemEntries(folder).OrderBy(f => Directory.Exists(f) ? 0 : 1).ThenBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToList();
                list.Items.Clear(); current = folder; path.Text = folder; _lastFolder = folder; status.Text = "";
                foreach (var entry in entries)
                {
                    var directory = Directory.Exists(entry);
                    if (!directory && (folders || filters.Count > 0 && !filters.Contains("*") && !filters.Any(f => Path.GetExtension(entry).Equals(f.TrimStart('*'), StringComparison.OrdinalIgnoreCase)))) continue;
                    var label = new Grid { ColumnSpacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
                    label.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); label.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                    label.Children.Add(Ui.Text((directory ? "▸  " : "    ") + Path.GetFileName(entry), 12.5));
                    var size = Ui.Text(directory ? Localization.T("文件夹", "Folder") : $"{new FileInfo(entry).Length / 1024.0:N1} KB", 11.5); Grid.SetColumn(size, 1); label.Children.Add(size);
                    list.Items.Add(new ListViewItem { Content = label, Tag = entry, HorizontalContentAlignment = HorizontalAlignment.Stretch });
                }
            }
            catch (Exception e) { status.Text = e.Friendly(); }
        }
        var location = new Grid { ColumnSpacing = 8 }; location.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); location.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        location.Children.Add(Ui.Button("↑", (_, _) => Navigate(Directory.GetParent(current)?.FullName ?? current))); Grid.SetColumn(path, 1); location.Children.Add(path); body.Children.Add(location);
        var shortcuts = Ui.Row();
        foreach (var destination in new[] { (Localization.T("用户目录", "Home"), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), (Localization.T("桌面", "Desktop"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)), (Localization.T("下载", "Downloads"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")) })
        {
            var folder = destination.Item2; shortcuts.Children.Add(Ui.Button(destination.Item1, (_, _) => Navigate(folder)));
        }
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady)) { var folder = drive.RootDirectory.FullName; shortcuts.Children.Add(Ui.Button(folder, (_, _) => Navigate(folder))); }
        Grid.SetRow(shortcuts, 1); body.Children.Add(shortcuts); Grid.SetRow(list, 2); body.Children.Add(list);
        if (save) { Grid.SetRow(fileName, 3); body.Children.Add(fileName); } Grid.SetRow(status, 4); body.Children.Add(status);
        path.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; Navigate(path.Text); } };
        list.DoubleTapped += (_, _) => { if (list.SelectedItem is ListViewItem item && item.Tag is string entry && Directory.Exists(entry)) Navigate(entry); };
        list.SelectionChanged += (_, _) => { if (save && list.SelectedItem is ListViewItem item && item.Tag is string entry && File.Exists(entry)) fileName.Text = Path.GetFileName(entry); };
        Navigate(current);
        while (true)
        {
            var dialog = new KilnDialog { XamlRoot = root, PreferredWidth = 680, Title = save ? Localization.T("另存为", "Save as") : folders ? Localization.T("选择文件夹", "Choose a folder") : Localization.T("选择文件", "Choose files"), Content = body, PrimaryButtonText = save ? Localization.T("保存", "Save") : Localization.T("选择", "Choose"), CloseButtonText = Localization.T("取消", "Cancel") };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return Array.Empty<string>();
            var selected = list.SelectedItems.OfType<ListViewItem>().Select(i => i.Tag as string).Where(p => p != null).Cast<string>().ToArray();
            if (save)
            {
                var leaf = fileName.Text.Trim();
                if (leaf.Length == 0 || leaf.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || leaf.EndsWith('.')) { status.Text = Localization.T("请输入有效的文件名", "Enter a valid file name"); continue; }
                if (Path.GetExtension(leaf).Length == 0 && filters.FirstOrDefault(f => f != "*") is { } extension) leaf += extension.TrimStart('*');
                var destination = Path.Combine(current, leaf);
                if (File.Exists(destination))
                {
                    var confirm = new KilnDialog { XamlRoot = root, Title = Localization.T("替换已有文件？", "Replace existing file?"), Content = destination, PrimaryButtonText = Localization.T("替换", "Replace"), CloseButtonText = Localization.T("取消", "Cancel") };
                    if (await confirm.ShowAsync() != ContentDialogResult.Primary) continue;
                }
                return new[] { destination };
            }
            if (folders) return new[] { selected.FirstOrDefault(Directory.Exists) ?? current };
            var files = selected.Where(File.Exists).ToArray(); if (files.Length > 0) return files;
            if (selected.FirstOrDefault() is { } folder && Directory.Exists(folder)) Navigate(folder);
            else status.Text = Localization.T("请选择一个文件", "Choose a file");
        }
    }
}

public sealed class KilnFileOpenPicker
{
    public List<string> FileTypeFilter { get; } = new();
    public async Task<StorageFile?> PickSingleFileAsync()
    {
        var files = await KilnFileBrowser.Pick(false, false, false, FileTypeFilter); return files.Length == 0 ? null : await StorageFile.GetFileFromPathAsync(files[0]);
    }
    public async Task<IReadOnlyList<StorageFile>> PickMultipleFilesAsync()
    {
        var files = await KilnFileBrowser.Pick(false, false, true, FileTypeFilter); var result = new List<StorageFile>(); foreach (var file in files) result.Add(await StorageFile.GetFileFromPathAsync(file)); return result;
    }
}
public sealed class KilnFolderPicker
{
    public List<string> FileTypeFilter { get; } = new();
    public async Task<StorageFolder?> PickSingleFolderAsync()
    {
        var folders = await KilnFileBrowser.Pick(true, false, false, FileTypeFilter); return folders.Length == 0 ? null : await StorageFolder.GetFolderFromPathAsync(folders[0]);
    }
}
public sealed class KilnFileSavePicker
{
    public string SuggestedFileName { get; set; } = "";
    public Dictionary<string, IList<string>> FileTypeChoices { get; } = new();
    public async Task<StorageFile?> PickSaveFileAsync()
    {
        var paths = await KilnFileBrowser.Pick(false, true, false, FileTypeChoices.Values.SelectMany(v => v).ToList(), SuggestedFileName); if (paths.Length == 0) return null;
        if (!File.Exists(paths[0])) await File.WriteAllBytesAsync(paths[0], Array.Empty<byte>());
        return await StorageFile.GetFileFromPathAsync(paths[0]);
    }
}
