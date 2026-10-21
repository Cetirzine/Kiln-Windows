using Kiln.App.Pages;
using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Kiln.App;

internal static class InstanceLayoutUiChecks
{
    internal static async Task Run(MainWindow window, McInstance instance, Action<string, bool> check)
    {
        var oldSize = window.AppWindow.Size; var oldLanguage = Localizer.Shared.Language;
        try
        {
            foreach (var language in new[] { AppLanguage.ZhHans, AppLanguage.ZhHant, AppLanguage.En })
            foreach (var (width, height) in new[] { (920, 620), (1040, 680), (1200, 800) })
            {
                Localizer.Shared.Language = language;
                var scale = ((FrameworkElement)window.Content).XamlRoot.RasterizationScale;
                window.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(height * scale)));
                Ui.SelectTab("instance", 0); await Show(new InstanceDetailPage(instance));
                var folders = Descendants(window.Content).OfType<Grid>().Single(g => g.Name == "InstanceFolders");
                if (folders.Children.Count != 8 || folders.ColumnDefinitions.Count < 2) throw new InvalidOperationException("Missing adaptive folders");
                foreach (var button in folders.Children.OfType<Button>()) InsideHorizontally(button, folders);
                var management = Descendants(window.Content).OfType<Grid>().Single(g => g.Name == "InstanceManagement");
                foreach (var button in Descendants(management).OfType<Button>()) InsideHorizontally(button, management);
                var managementButtons = management.Children.OfType<Button>().ToArray();
                if (managementButtons.Length != 9 || management.ColumnDefinitions.Count != 3 || managementButtons.Max(b => b.ActualWidth) - managementButtons.Min(b => b.ActualWidth) > 1 || managementButtons.Any(b => Math.Abs(b.ActualHeight - 32) > .5)) throw new InvalidOperationException("Management buttons are not a uniform three-column grid");
                Ui.SelectTab("instance", 2); await Show(new InstanceDetailPage(instance));
                var worldRow = Descendants(window.Content).OfType<Grid>().Single(g => g.Name == "WorldRecord");
                foreach (var button in Descendants(worldRow).OfType<Button>()) InsideHorizontally(button, worldRow);
                foreach (var kind in new[] { "resourcepacks", "shaderpacks", "screenshots" })
                {
                    var page = new ContentFilesPage(instance, kind); await Show(page);
                    foreach (var button in Descendants(page).OfType<Button>().Where(b => b.ActualWidth > 0)) InsideHorizontally(button, page);
                }
                var mods = new ModManager(instance); await Show(mods);
                var elements = Descendants(mods).OfType<FrameworkElement>().ToList();
                var filters = elements.Single(e => e.Name == "ModFilters"); var actions = elements.Single(e => e.Name == "ModActions"); var list = elements.Single(e => e.Name == "ModList");
                SameEdges(filters, list, mods); SameEdges(actions, list, mods);
                var search = elements.Single(e => e.Name == "ModSearchField");
                var buttons = ((Grid)actions).Children.OfType<Button>().ToArray();
                var filter = ((Grid)filters).Children.OfType<ComboBox>().Single();
                if (Math.Abs(Left(search, mods) - Left(buttons[0], mods)) > .5 || Math.Abs(Right(filter, mods) - Right(buttons[^1], mods)) > .5 || buttons.Max(b => b.ActualWidth) - buttons.Min(b => b.ActualWidth) > 1) throw new InvalidOperationException("Mod search/action edges differ");
                foreach (var type in new[] { Modrinth.ProjectType.Mod, Modrinth.ProjectType.Modpack })
                {
                    var page = new ResourceSearchPage(type, loadRemote: false); await Show(page);
                    var toolbar = Descendants(page).OfType<Grid>().Single(g => g.Name == "ResourceSearchToolbar");
                    var results = Descendants(page).OfType<ScrollViewer>().Single(g => g.Name == "ResourceResultsViewport");
                    SameEdges(toolbar, results, page);
                    var first = toolbar.Children.OfType<FrameworkElement>().First(); var last = toolbar.Children.OfType<FrameworkElement>().Last();
                    if (Math.Abs(Left(first, page) - Left(results, page)) > .5 || Math.Abs(Right(last, page) - Right(results, page)) > .5) throw new InvalidOperationException("Download search/target edges differ");
                }
                check($"Instance cards and actual Mod controls align at {width}x{height} ({language})", true);
            }
        }
        finally { Localizer.Shared.Language = oldLanguage; window.AppWindow.Resize(oldSize); Ui.SelectTab("instance", 0); }

        async Task Show(UIElement content)
        {
            window.ShowDiagnostics(new Border { Padding = new Thickness(28, 48, 28, 24), Child = content });
            await Task.Delay(60); ((FrameworkElement)window.Content).UpdateLayout(); await Task.Delay(30); ((FrameworkElement)window.Content).UpdateLayout();
        }
    }
    private static double Left(FrameworkElement element, UIElement relative) => element.TransformToVisual(relative).TransformPoint(new Windows.Foundation.Point()).X;
    private static double Right(FrameworkElement element, UIElement relative) => Left(element, relative) + element.ActualWidth;
    private static void SameEdges(FrameworkElement a, FrameworkElement b, UIElement relative)
    {
        if (Math.Abs(Left(a, relative) - Left(b, relative)) > .5 || Math.Abs(Right(a, relative) - Right(b, relative)) > .5) throw new InvalidOperationException($"Different edges: {a.Name}/{b.Name}");
    }
    private static void InsideHorizontally(FrameworkElement child, FrameworkElement parent)
    {
        if (Left(child, parent) < -.5 || Right(child, parent) > parent.ActualWidth + .5) throw new InvalidOperationException($"Card action is clipped: {child.Name}/{child.GetType().Name}");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
}
