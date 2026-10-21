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
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly string _title;
    private readonly Button[] _actions;
    private string Folder => Path.Combine(_instance.GameDir, _kind);
    private static string T(string zh, string en) => Localization.T(zh, en);
    public ContentFilesPage(McInstance instance, string kind)
    {
        _instance = instance; _kind = kind;
        _title = kind == "screenshots" ? T("截图", "Screenshots") : kind == "shaderpacks" ? T("光影", "Shaders") : T("资源包", "Resource packs");
        var actions = new List<Button>();
        if (kind != "screenshots")
        {
            actions.Add(Ui.Button(T("添加文件…", "Add files…"), async (_, _) => await Add()));
            actions.Add(Ui.Button(T("下载…", "Download…"), (_, _) => { LauncherSettings.Shared.SelectedInstance = _instance.Name; MainWindow.Current!.NavigateTo(kind == "shaderpacks" ? "shaders" : "resourcepacks"); }));
        }
        actions.Add(Ui.Button(T("打开文件夹", "Open folder"), async (_, _) => { Directory.CreateDirectory(Folder); await Windows.System.Launcher.LaunchFolderPathAsync(Folder); }));
        _actions = actions.ToArray(); Reload(); Loaded += (_, _) => Reload();
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
        // Reuse the existing card/header so action controls never change visual parents.
        var files = Directory.Exists(Folder) ? Directory.GetFileSystemEntries(Folder).Where(file => !Path.GetFileName(file).StartsWith('.') && (_kind == "screenshots" ? new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(file).ToLowerInvariant()) : !file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))).OrderByDescending(File.GetLastWriteTime).ToList() : new List<string>();
        var title = _title + " · " + files.Count;
        if (_cardTitle != null) _cardTitle.Text = title;
        if (Content == null)
        {
            var card = Ui.CardWithActions(title, _actions, _list);
            _cardTitle = FindText(card, title);
            Content = Ui.Scroll(card);
        }
        if (_kind == "shaderpacks" && !_instance.Loaders.Any(loader => new[] { "Fabric", "Quilt", "Forge", "NeoForge", "OptiFine" }.Contains(loader)))
        {
            var hint = Ui.Text(T("光影需要 Iris（Fabric / Quilt）、Oculus（Forge）或 OptiFine。", "Shaders need Iris (Fabric / Quilt), Oculus (Forge) or OptiFine."), 12); hint.Margin = new Thickness(0, 0, 0, 12); hint.Foreground = Ui.Brush("KilnTextSecondary"); _list.Children.Add(hint);
        }
        Grid? gallery = null;
        if (_kind == "screenshots") { gallery = new Grid { Name = "ScreenshotGallery", ColumnSpacing = 10, RowSpacing = 10 }; _list.Children.Add(gallery); gallery.SizeChanged += (_, _) => ArrangeGallery(gallery); }
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var directory = Directory.Exists(file);
            var info = new FileInfo(file);
            var open = Ui.Button(T("打开", "Open"), async (_, _) => { if (directory) await Windows.System.Launcher.LaunchFolderPathAsync(file); else await Windows.System.Launcher.LaunchFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(file)); });
            var copy = Ui.Button(T("另存为…", "Save as…"), async (_, _) =>
            {
                var picker = new KilnFileSavePicker();  picker.SuggestedFileName = Path.GetFileNameWithoutExtension(name); picker.FileTypeChoices.Add("File", new List<string> { Path.GetExtension(name) });
                var output = await picker.PickSaveFileAsync(); if (output != null) File.Copy(file, output.Path, true);
            });
            copy.IsEnabled = !directory;
            var remove = Ui.Button(T("移到回收站", "Move to Recycle Bin"), async (_, _) => { try { Recycle.Delete(file); Reload(); } catch (Exception e) { await Ui.ShowError(XamlRoot, e); } });
            var filename = Ui.Text(Path.GetFileNameWithoutExtension(name), 13, FontWeights.Medium); filename.MaxLines = 1; filename.TextTrimming = TextTrimming.CharacterEllipsis;
            var detail = Ui.Text((directory ? T("文件夹", "Folder") : Job.FormatSize(info.Length)) + $" · {info.LastWriteTime:yyyy-MM-dd HH:mm}", 11.5); detail.Foreground = Ui.Brush("KilnTextSecondary");
            var labels = Ui.Col(filename, detail); labels.Spacing = 4;
            if (_kind == "screenshots")
            {
                var image = new Image { Source = new BitmapImage(new Uri(file)), Height = 125, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Stretch };
                var tools = Ui.Row(open, copy, remove); tools.Spacing = 4;
                var caption = Ui.Col(labels, tools); caption.Padding = new Thickness(10); caption.Spacing = 8;
                var tile = Ui.Col(image, caption); tile.Spacing = 0;
                gallery!.Children.Add(new Border { BorderBrush = Ui.Brush("KilnStroke"), BorderThickness = new Thickness(1), Child = tile });
            }
            else
            {
                var icon = new Grid { Width = 36, Height = 36, Background = Ui.Brush("KilnPanelSecondary") };
                icon.Children.Add(new FontIcon { Glyph = _kind == "shaderpacks" ? "\uE706" : "\uE91B", FontSize = 18, Foreground = Ui.Brush("KilnTextSecondary") });
                if (!directory && Zip.Read(file, "pack.png") is { } bytes) { var image = new Image { Width = 36, Height = 36, Stretch = Stretch.UniformToFill }; icon.Children.Add(image); _ = LoadIcon(image, bytes); }
                var label = new Grid { ColumnSpacing = 12 }; label.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) }); label.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); label.Children.Add(icon); Grid.SetColumn(labels, 1); label.Children.Add(labels);
                _list.Children.Add(new Border { BorderBrush = Ui.Brush("KilnStroke"), BorderThickness = new Thickness(0, 0, 0, 1), Child = Ui.RecordRow(label, open, copy, remove) });
            }
        }
        if (gallery != null) ArrangeGallery(gallery);
        if (files.Count == 0) { var empty = Ui.Text(_kind == "screenshots" ? T("还没有截图，在游戏中按 F2 截图。", "No screenshots yet. Press F2 in game to take one.") : T($"还没有{_title}，可添加文件或拖放 ZIP 到这里。", $"No {_title.ToLowerInvariant()} yet. Add files or drop ZIP files here."), 12); empty.Margin = new Thickness(10, 16, 10, 16); empty.Foreground = Ui.Brush("KilnTextSecondary"); _list.Children.Add(empty); }
        if (_kind == "resourcepacks" && files.Count > 0) { var hint = Ui.Text(T("在游戏的“选项 → 资源包”中启用。", "Enable them in game under Options → Resource Packs."), 11.5); hint.Margin = new Thickness(10, 12, 10, 0); hint.Foreground = Ui.Brush("KilnTextSecondary"); _list.Children.Add(hint); }
    }
    private TextBlock? _cardTitle;
    private static TextBlock? FindText(DependencyObject parent, string text)
    {
        if (parent is TextBlock label && label.Text == text) return label;
        // The card is not loaded yet; its code-created children are already available.
        if (parent is Panel panel) foreach (var child in panel.Children) if (FindText(child, text) is { } found) return found;
        if (parent is Border border && border.Child != null) return FindText(border.Child, text);
        return null;
    }
    private static void ArrangeGallery(Grid gallery)
    {
        var columns = Math.Max(1, (int)(gallery.ActualWidth / 260));
        if (gallery.ColumnDefinitions.Count == columns && gallery.RowDefinitions.Count == (int)Math.Ceiling(gallery.Children.Count / (double)columns)) return;
        gallery.ColumnDefinitions.Clear(); gallery.RowDefinitions.Clear();
        for (var i = 0; i < columns; i++) gallery.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < gallery.Children.Count; i++) { if (i % columns == 0) gallery.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Grid.SetColumn((FrameworkElement)gallery.Children[i], i % columns); Grid.SetRow((FrameworkElement)gallery.Children[i], i / columns); }
    }
    private static async Task LoadIcon(Image image, byte[] bytes)
    {
        try { using var stream = new MemoryStream(bytes); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream.AsRandomAccessStream()); image.Source = bitmap; } catch { }
    }
    private void Copy(string path) { Directory.CreateDirectory(Folder); File.Copy(path, Path.Combine(Folder, Path.GetFileName(path)), true); }
    private async Task Add()
    {
        var picker = new KilnFileOpenPicker();  picker.FileTypeFilter.Add(".zip");
        try { foreach (var file in await picker.PickMultipleFilesAsync()) Copy(file.Path); Reload(); } catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
    }
}
