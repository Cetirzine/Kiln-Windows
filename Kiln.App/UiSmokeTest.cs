using Kiln.App.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System.Text.Json;

namespace Kiln.App;

// App-owned native UI regression checks. No credentials, accounts or remote requests.
internal static class UiSmokeTest
{
    internal static async Task Run(MainWindow window, string reportPath)
    {
        var results = new List<object>();
        void Check(string name, bool passed, object? detail = null)
        {
            results.Add(new { name, passed, detail });
            File.WriteAllText(reportPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            if (!passed) throw new InvalidOperationException(name);
        }
        try
        {
            var input = new TextBox { PlaceholderText = "搜索… / Search…" };
            var field = Ui.SearchField(input);
            var fixture = new Grid { Padding = new Thickness(28), Background = Ui.Brush("KilnPanel") };
            field.VerticalAlignment = VerticalAlignment.Top; fixture.Children.Add(field);
            window.ShowDiagnostics(fixture);
            await Task.Delay(200); fixture.UpdateLayout();
            var root = fixture.XamlRoot;
            window.SetCaptionBackdrop(.02);
            Check("Caption on dark content uses white glyphs and transparent background", window.AppWindow.TitleBar.ButtonForegroundColor == Microsoft.UI.Colors.White && window.AppWindow.TitleBar.ButtonBackgroundColor?.A == 0 && window.AppWindow.TitleBar.ButtonInactiveBackgroundColor?.A == 0);
            window.SetCaptionBackdrop(.8);
            Check("Caption on light content uses black glyphs", window.AppWindow.TitleBar.ButtonForegroundColor == Microsoft.UI.Colors.Black && window.AppWindow.TitleBar.ButtonInactiveForegroundColor == Microsoft.UI.Colors.Black);
            window.SetCaptionBackdrop();
            var placeholder = Descendants(field).OfType<TextBlock>().Single(t => t.Text == "搜索… / Search…");
            double Center(FrameworkElement element) => element.TransformToVisual(field).TransformPoint(new Windows.Foundation.Point(0, element.ActualHeight / 2)).Y;
            Check("Search placeholder vertically centered", Math.Abs(Center(placeholder) - field.ActualHeight / 2) < 1, new { center = Center(placeholder), height = field.ActualHeight });
            input.Text = "Fabric API"; await Task.Delay(30); fixture.UpdateLayout();
            var editor = Descendants(input).OfType<ScrollViewer>().First(e => e.Name == "ContentElement");
            Check("Search editor vertically centered", Math.Abs(Center(editor) - field.ActualHeight / 2) < 1, new { center = Center(editor), height = editor.ActualHeight });
            Check("Search placeholder hidden after typing", placeholder.Visibility == Visibility.Collapsed);
            input.Text = ""; await Task.Delay(30); Check("Search placeholder restored after clearing", placeholder.Visibility == Visibility.Visible);

            var body = Ui.Col(Ui.Text("Synthetic modal lifecycle test", 18), Ui.Row(Ui.Button("Action A", (_, _) => { }), Ui.Button("Action B", (_, _) => { })));
            for (var i = 0; i < 100; i++)
            {
                var dialog = new KilnDialog { XamlRoot = root, Title = "UI regression", Content = body, CloseButtonText = "Cancel test", PrimaryButtonText = "Continue test" };
                var shown = dialog.ShowAsync();
                if (shown.IsFaulted) await shown;
                ((FrameworkElement)root.Content).UpdateLayout();
                dialog.IsPrimaryButtonEnabled = false;
                var overlay = Descendants(root.Content).OfType<Grid>().Single(g => g.Name == "KilnModalOverlay");
                overlay.UpdateLayout();
                var buttons = Descendants(overlay).OfType<Button>().ToList();
                if (i == 0) Check("Live dialog primary state updates", !buttons.Single(b => b.Content is string s && s == "Continue test").IsEnabled);
                dialog.IsPrimaryButtonEnabled = true;
                var action = buttons.Single(b => b.Content is string s && s == (i % 2 == 0 ? "Cancel test" : "Continue test"));
                ((IInvokeProvider)new ButtonAutomationPeer(action).GetPattern(PatternInterface.Invoke)).Invoke();
                var result = await shown;
                if (result != (i % 2 == 0 ? ContentDialogResult.None : ContentDialogResult.Primary) || KilnDialog.IsOpen || VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count != 0 || VisualTreeHelper.GetParent(body) != null || Descendants(root.Content).OfType<Grid>().Any(g => g.Name == "KilnModalOverlay"))
                    throw new InvalidOperationException($"Orphan popup or content after iteration {i}");
            }
            Check("100 cancel/install/reopen cycles without orphan controls", true);
            Check("Window remains responsive after popup cancellation", fixture.IsLoaded && root.Size.Width > 0);
            var firstJob = MainWindow.NewJob("Synthetic install progress"); firstJob.FileFraction = .5;
            window.RefreshJobIndicator(); ((FrameworkElement)root.Content).UpdateLayout();
            var indicatorWait = System.Diagnostics.Stopwatch.StartNew();
            while (!Descendants(root.Content).OfType<Border>().Any(b => b.Name == "SidebarJobFill") && indicatorWait.Elapsed < TimeSpan.FromSeconds(3)) { await Task.Delay(16); ((FrameworkElement)root.Content).UpdateLayout(); }
            var fill = Descendants(root.Content).OfType<Border>().Single(b => b.Name == "SidebarJobFill");
            Check("Sidebar switches to an advancing task bar", Math.Abs(fill.Width - 17) < .1);
            var secondJob = MainWindow.NewJob("Synthetic second install"); window.RefreshJobIndicator();
            Check("Sidebar counts concurrent installs", Descendants(root.Content).OfType<TextBlock>().Any(t => t.Text == Kiln.Core.Localization.T("2 个任务", "2 tasks")));
            firstJob.State = Kiln.Core.Job.JobState.Success; secondJob.State = Kiln.Core.Job.JobState.Cancelled; window.RefreshJobIndicator();
            ((FrameworkElement)root.Content).UpdateLayout();
            Check("Sidebar restores its task icon after installs finish", !Descendants(root.Content).OfType<Border>().Any(b => b.Name == "SidebarJobFill"));
            MainWindow.ClearFinishedJobs();
            var fixtureRoot = Path.Combine(Path.GetTempPath(), "kiln-ui-" + Guid.NewGuid().ToString("N"));
            var versionDir = Path.Combine(fixtureRoot, "versions", "UI fixture"); Directory.CreateDirectory(versionDir);
            File.WriteAllText(Path.Combine(versionDir, "UI fixture.json"), "{\"id\":\"UI fixture\",\"mainClass\":\"test\",\"type\":\"release\",\"javaVersion\":{\"majorVersion\":21},\"releaseTime\":\"2026-10-21T00:00:00Z\"}");
            var instance = new Kiln.Core.McInstance(fixtureRoot, "UI fixture");
            var worldDir = Path.Combine(instance.GameDir, "saves", "World with a long display name for layout checks"); Directory.CreateDirectory(worldDir);
            File.WriteAllBytes(Path.Combine(worldDir, "level.dat"), Array.Empty<byte>());
            var packsDir = Path.Combine(instance.GameDir, "resourcepacks"); Directory.CreateDirectory(packsDir);
            using (var archive = System.IO.Compression.ZipFile.Open(Path.Combine(packsDir, "Sample pack.zip"), System.IO.Compression.ZipArchiveMode.Create)) { using var writer = new StreamWriter(archive.CreateEntry("pack.mcmeta").Open()); writer.Write("{}"); }
            var shotsDir = Path.Combine(instance.GameDir, "screenshots"); Directory.CreateDirectory(shotsDir);
            for (var i = 0; i < 3; i++) File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png"), Path.Combine(shotsDir, $"Sample {i}.png"));
            async Task Show(UIElement page) { window.ShowDiagnostics(new Border { Padding = new Thickness(28, 48, 28, 24), Child = page }); await Task.Delay(100); ((FrameworkElement)root.Content).UpdateLayout(); await Task.Delay(30); ((FrameworkElement)root.Content).UpdateLayout(); }
            Ui.SelectTab("instance", 0); await Show(new InstanceDetailPage(instance));
            Check("Overview restores folders and management cards", Descendants(root.Content).OfType<TextBlock>().Any(t => t.Text == Kiln.Core.Localization.T("文件夹", "Folders")) && Descendants(root.Content).OfType<TextBlock>().Any(t => t.Text == Kiln.Core.Localization.T("管理", "Manage")));
            await Show(new ModManager(instance));
            var filterRow = Descendants(root.Content).OfType<Grid>().Single(g => g.Name == "ModFilters");
            var actionsRow = Descendants(root.Content).OfType<Grid>().Single(g => g.Name == "ModActions");
            var modList = Descendants(root.Content).OfType<Border>().Single(g => g.Name == "ModList");
            Check("Mod search, actions and list share both edges", Math.Abs(filterRow.ActualWidth - modList.ActualWidth) < 1 && Math.Abs(actionsRow.ActualWidth - modList.ActualWidth) < 1, new { filters = filterRow.ActualWidth, actions = actionsRow.ActualWidth, list = modList.ActualWidth });
            await Show(new ContentFilesPage(instance, "resourcepacks"));
            Check("Resource pack card shows count and file details", Descendants(root.Content).OfType<TextBlock>().Any(t => t.Text == Kiln.Core.Localization.T("资源包", "Resource packs") + " · 1") && Descendants(root.Content).OfType<TextBlock>().Any(t => t.Text == "Sample pack"));
            await Show(new ContentFilesPage(instance, "screenshots"));
            var gallery = Descendants(root.Content).OfType<Grid>().Single(g => g.Name == "ScreenshotGallery");
            Check("Screenshots use an adaptive multi-column gallery", gallery.ColumnDefinitions.Count >= 2 && gallery.Children.Count == 3);
            await WorkflowUiChecks.Run(window, root, instance, fixtureRoot, (name, passed) => Check(name, passed));
            await InstanceLayoutUiChecks.Run(window, instance, (name, passed) => Check(name, passed));
            await ActivationUiChecks.Run(window, instance, fixtureRoot, (name, passed) => Check(name, passed));
            await ShortcutUiChecks.Run(instance, fixtureRoot, (name, passed) => Check(name, passed));
            await HintUiChecks.Run(window, (name, passed) => Check(name, passed));
            await SystemUiChecks.Run(window, fixtureRoot, (name, passed) => Check(name, passed));
            await AccountUiChecks.Run(window, fixtureRoot, (name, passed) => Check(name, passed));
            await UpdateUiChecks.Run(window, (name, passed) => Check(name, passed));
            await ControlUiChecks.Run(window, (name, passed) => Check(name, passed));
            await SettingsUiChecks.Run(window, fixtureRoot, (name, passed) => Check(name, passed));
            await TaskUiChecks.Run(window, fixtureRoot, (name, passed) => Check(name, passed));
            Check("No unhandled UI exceptions during native regression", App.DiagnosticErrors.Count == 0, App.DiagnosticErrors);
            Ui.SelectTab("instance", 0); await Show(new InstanceDetailPage(instance));
            window.Closed += (_, _) => { if (Path.GetFullPath(fixtureRoot).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Directory.Exists(fixtureRoot)) { try { Directory.Delete(fixtureRoot, true); } catch { } } };
            if (Environment.GetCommandLineArgs().Contains("--show-account-scene")) window.ShowDiagnostics(AccountUiChecks.Preview(fixtureRoot));
            if (Environment.GetCommandLineArgs().Contains("--show-settings")) { Ui.SelectTab("setup", 0); await Show(new SetupPage(Kiln.Core.LauncherSettings.Ephemeral())); }
            if (Environment.GetCommandLineArgs().Contains("--show-tasks")) await Show(TaskUiChecks.Preview());
            if (Environment.GetCommandLineArgs().Contains("--preview-dark")) ((FrameworkElement)window.Content).RequestedTheme = ElementTheme.Dark;
            window.Title = "Kiln · UI 检查";
        }
        catch (Exception e)
        {
            results.Add(new { name = "UI regression exception", passed = false, detail = e.ToString() });
            File.WriteAllText(reportPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { window.SetDiagnosticsRunning(false); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
