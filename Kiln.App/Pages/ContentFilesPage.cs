using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Kiln.App.Pages;

public sealed class ContentFilesPage : UserControl
{
    private readonly McInstance _instance;
    private readonly string _kind;
    private readonly StackPanel _list = new() { Spacing = 4 };
    private string Folder => Path.Combine(_instance.GameDir, _kind);
    private static string T(string zh, string en) => Localization.T(zh, en);
    public ContentFilesPage(McInstance instance, string kind)
    {
        _instance = instance; _kind = kind;
        var title = kind == "screenshots" ? T("截图", "Screenshots") : kind == "shaderpacks" ? T("光影", "Shaders") : T("资源包", "Resource packs");
        var actions = Ui.Row(Ui.Button(T("打开文件夹", "Open folder"), async (_, _) => { Directory.CreateDirectory(Folder); await Windows.System.Launcher.LaunchFolderPathAsync(Folder); }));
        if (kind != "screenshots")
        {
            actions.Children.Add(Ui.Button(T("添加文件…", "Add files…"), async (_, _) => await Add()));
            actions.Children.Add(Ui.Button(T("下载…", "Download…"), (_, _) => MainWindow.Current!.NavigateTo(kind == "shaderpacks" ? "shaders" : "resourcepacks")));
        }
        Content = Ui.Scroll(Ui.Card(title, actions, _list)); Loaded += (_, _) => Reload();
        AllowDrop = kind != "screenshots";
        DragOver += (_, e) => e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        Drop += async (_, e) =>
        {
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) return;
            try { var items = await e.DataView.GetStorageItemsAsync(); foreach (var file in items.OfType<Windows.Storage.StorageFile>()) if (file.FileType.Equals(".zip", StringComparison.OrdinalIgnoreCase)) Copy(file.Path); Reload(); }
            catch (Exception error) { await Ui.ShowError(XamlRoot, error); }
        };
    }
    private void Reload()
    {
        _list.Children.Clear();
        if (!Directory.Exists(Folder)) { _list.Children.Add(Ui.Text(T("还没有文件", "No files yet"))); return; }
        foreach (var file in Directory.GetFiles(Folder).OrderByDescending(File.GetLastWriteTime))
        {
            if (_kind == "screenshots" && !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
            var name = Path.GetFileName(file);
            var info = new FileInfo(file);
            var open = Ui.Button(T("打开", "Open"), async (_, _) => await Windows.System.Launcher.LaunchFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(file)));
            var copy = Ui.Button(T("另存为…", "Save as…"), async (_, _) =>
            {
                var picker = new KilnFileSavePicker();  picker.SuggestedFileName = Path.GetFileNameWithoutExtension(name); picker.FileTypeChoices.Add("File", new List<string> { Path.GetExtension(name) });
                var output = await picker.PickSaveFileAsync(); if (output != null) File.Copy(file, output.Path, true);
            });
            var remove = Ui.Button(T("移到回收站", "Move to Recycle Bin"), async (_, _) => { try { Recycle.Delete(file); Reload(); } catch (Exception e) { await Ui.ShowError(XamlRoot, e); } });
            var label = Ui.Col(Ui.Text(name, 13, FontWeights.Medium), Ui.Text($"{info.Length / 1024.0:N1} KB · {info.LastWriteTime:yyyy-MM-dd HH:mm}", 11.5));
            if (_kind == "screenshots")
            {
                var image = new Image { Source = new BitmapImage(new Uri(file)), Height = 160, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
                _list.Children.Add(Ui.Card(name, image, Ui.Row(open, copy, remove)));
            }
            else _list.Children.Add(Ui.RecordRow(label, open, copy, remove));
        }
        if (_list.Children.Count == 0) _list.Children.Add(Ui.Text(T("还没有文件", "No files yet")));
    }
    private void Copy(string path) { Directory.CreateDirectory(Folder); File.Copy(path, Path.Combine(Folder, Path.GetFileName(path)), true); }
    private async Task Add()
    {
        var picker = new KilnFileOpenPicker();  picker.FileTypeFilter.Add(".zip");
        try { foreach (var file in await picker.PickMultipleFilesAsync()) Copy(file.Path); Reload(); } catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
    }
}
