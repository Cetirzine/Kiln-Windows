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
            actions.Add(Ui.Button(kind == "shaderpacks" ? T("下载光影", "Download shaders") : T("下载资源包", "Download resource packs"), (_, _) => { LauncherSettings.Shared.SelectedInstance = _instance.Name; MainWindow.Current!.NavigateTo(kind == "shaderpacks" ? "shaders" : "resourcepacks"); }));
        }
        actions.Add(InstanceCardUi.IconButton("\uE8B7", T("打开文件夹", "Open folder"), async (_, _) => { Directory.CreateDirectory(Folder); await Windows.System.Launcher.LaunchFolderPathAsync(Folder); }));
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
        var files = Directory.Exists(Folder) ? Directory.GetFileSystemEntries(Folder).Where(file => !Path.GetFileName(file).StartsWith('.') && (_kind == "screenshots" ? new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(file).ToLowerInvariant()) : !file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))).ToList() : new List<string>();
        files = _kind == "screenshots" ? files.OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList() : files.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
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
        if (_kind == "screenshots") { gallery = new Grid { Name = "ScreenshotGallery" }; _list.Children.Add(gallery); }
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var directory = Directory.Exists(file);
            var info = new FileInfo(file);
            async Task OpenFile() { if (directory) await Windows.System.Launcher.LaunchFolderPathAsync(file); else await Windows.System.Launcher.LaunchFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(file)); }
            async Task SaveFile()
            {
                if (directory) return;
                var picker = new KilnFileSavePicker();  picker.SuggestedFileName = Path.GetFileNameWithoutExtension(name); picker.FileTypeChoices.Add("File", new List<string> { Path.GetExtension(name) });
                var output = await picker.PickSaveFileAsync(); if (output != null) File.Copy(file, output.Path, true);
            }
            Task RemoveFile() { Recycle.Delete(file); Reload(); return Task.CompletedTask; }
            var menu = new MenuFlyout();
            void MenuAction(string title, Func<Task> clicked, bool enabled = true) { var item = new MenuFlyoutItem { Text = title, IsEnabled = enabled }; item.Click += async (_, _) => { try { await clicked(); } catch (Exception error) { await Ui.ShowError(XamlRoot, error); } }; menu.Items.Add(item); }
            MenuAction(T("打开", "Open"), OpenFile);
            MenuAction(T("另存为…", "Save as…"), SaveFile, !directory);
            MenuAction(T("在文件资源管理器中显示", "Show in File Explorer"), () => InstanceCardUi.Reveal(file));
            menu.Items.Add(new MenuFlyoutSeparator());
            MenuAction(T("移到回收站", "Move to Recycle Bin"), RemoveFile);
            var filename = Ui.Text(Path.GetFileNameWithoutExtension(name), 13, FontWeights.Medium); filename.MaxLines = 1; filename.TextTrimming = TextTrimming.CharacterEllipsis;
            var detail = Ui.Text((directory ? T("文件夹", "Folder") : Job.FormatSize(info.Length)) + $" · {info.LastWriteTime:yyyy-MM-dd HH:mm}", 11.5); detail.Foreground = Ui.Brush("KilnTextSecondary");
            var labels = Ui.Col(filename, detail); labels.Spacing = 4;
            if (_kind == "screenshots")
            {
                var image = new Image { Source = new BitmapImage(new Uri(file)) { DecodePixelWidth = 480 }, Height = 110, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Stretch };
                var tile = new Button { Content = image, Style = (Style)Application.Current.Resources["KilnNavigationButton"], Padding = new Thickness(0), BorderBrush = Ui.Brush("KilnStroke"), BorderThickness = new Thickness(1), HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch, ContextFlyout = menu };
                tile.SizeChanged += (_, _) => image.Height = Math.Max(1, (tile.ActualWidth - 2) * 9 / 16);
                tile.DoubleTapped += async (_, _) => await Windows.System.Launcher.LaunchFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(file));
                ToolTipService.SetToolTip(tile, name); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile, name);
                gallery!.Children.Add(tile);
            }
            else
            {
                var icon = new Grid { Width = 32, Height = 32, Background = Ui.Brush("KilnPanelSecondary") };
                icon.Children.Add(new FontIcon { Glyph = _kind == "shaderpacks" ? "\uE706" : "\uE91B", FontSize = 18, Foreground = Ui.Brush("KilnTextSecondary") });
                var bytes = directory ? (File.Exists(Path.Combine(file, "pack.png")) ? File.ReadAllBytes(Path.Combine(file, "pack.png")) : null) : Zip.Read(file, "pack.png");
                if (bytes != null) { var image = new Image { Width = 32, Height = 32, Stretch = Stretch.UniformToFill }; icon.Children.Add(image); _ = LoadIcon(image, bytes); }
                var reveal = InstanceCardUi.IconButton("\uE721", T("在文件资源管理器中显示", "Show in File Explorer"), async (_, _) => { try { await InstanceCardUi.Reveal(file); } catch (Exception e) { await Ui.ShowError(XamlRoot, e); } });
                var trash = InstanceCardUi.IconButton("\uE74D", T("移到回收站", "Move to Recycle Bin"), async (_, _) => { try { Recycle.Delete(file); Reload(); } catch (Exception e) { await Ui.ShowError(XamlRoot, e); } });
                var row = Ui.RecordRow(InstanceCardUi.FileLabel(icon, labels), reveal, trash);
                foreach (var button in ((StackPanel)row.Children[1]).Children.OfType<Button>()) button.Width = 28;
                row.ContextFlyout = menu;
                _list.Children.Add(row);
            }
        }
        if (gallery != null) InstanceCardUi.Adapt(gallery, 180, 10);
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
