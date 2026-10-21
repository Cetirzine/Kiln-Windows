using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

internal static class InstanceCardUi
{
    internal static async Task Reveal(string path)
    {
        var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path)!);
        var options = new Windows.System.FolderLauncherOptions();
        options.ItemsToSelect.Add(Directory.Exists(path) ? await Windows.Storage.StorageFolder.GetFolderFromPathAsync(path) : await Windows.Storage.StorageFile.GetFileFromPathAsync(path));
        await Windows.System.Launcher.LaunchFolderAsync(folder, options);
    }

    internal static Button IconButton(string glyph, string title, RoutedEventHandler clicked)
    {
        var button = Ui.Button("", clicked);
        button.Content = new FontIcon { Glyph = glyph, FontSize = 13 };
        button.Width = 28; button.Height = 28; button.MinWidth = 0; button.Padding = new Thickness(0);
        ToolTipService.SetToolTip(button, title); AutomationProperties.SetName(button, title);
        return button;
    }

    internal static void Adapt(Grid grid, double minimumWidth, double spacing = 8)
    {
        grid.ColumnSpacing = spacing; grid.RowSpacing = spacing;
        void Arrange()
        {
            var columns = Math.Max(1, (int)((grid.ActualWidth + spacing) / (minimumWidth + spacing)));
            var rows = (int)Math.Ceiling(grid.Children.Count / (double)columns);
            if (grid.ColumnDefinitions.Count == columns && grid.RowDefinitions.Count == rows) return;
            grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < rows; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var i = 0; i < grid.Children.Count; i++) { Grid.SetColumn((FrameworkElement)grid.Children[i], i % columns); Grid.SetRow((FrameworkElement)grid.Children[i], i / columns); }
        }
        grid.SizeChanged += (_, _) => Arrange(); grid.Loaded += (_, _) => Arrange(); Arrange();
    }

    internal static Grid FileLabel(UIElement icon, UIElement labels, double size = 32)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(size) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(icon); Grid.SetColumn((FrameworkElement)labels, 1); row.Children.Add(labels);
        return row;
    }
}
