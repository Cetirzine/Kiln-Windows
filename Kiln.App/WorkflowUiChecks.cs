using Kiln.App.Pages;
using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Kiln.App;

internal static class WorkflowUiChecks
{
    public static async Task Run(MainWindow window, XamlRoot root, McInstance instance, string fixtureRoot, Action<string, bool> check)
    {
        var config = Path.Combine(instance.GameDir, "config"); Directory.CreateDirectory(config); File.WriteAllText(Path.Combine(config, "test.cfg"), "test");
        var items = Modpack.ExportItems(instance);
        var export = new ModpackExportForm("Workflow fixture", items);
        window.ShowDiagnostics(export); await Task.Delay(100); export.UpdateLayout();
        check("Export UI keeps screenshots opt-in", export.Selected.Contains("config") && !export.Selected.Contains("screenshots"));
        var personal = Descendants(export).OfType<CheckBox>().Single(c => c.Tag is string s && s == "screenshots");
        ((IToggleProvider)new CheckBoxAutomationPeer(personal).GetPattern(PatternInterface.Toggle)).Toggle(); await Task.Delay(30);
        check("Custom checkbox toggles file selection", export.Selected.Contains("screenshots") && personal.IsChecked == true && Descendants(personal).OfType<FontIcon>().Any(i => i.Name == "CheckGlyph" && i.Visibility == Visibility.Visible));
        foreach (var input in Descendants(export).OfType<TextBox>()) { if (Equals(input.Tag, "PackName")) input.Text = "Edited pack"; if (Equals(input.Tag, "PackVersion")) input.Text = "3.0.1"; if (Equals(input.Tag, "PackSummary")) input.Text = "Edited summary"; }
        check("Export UI reads all editable metadata", export.PackName == "Edited pack" && export.Version == "3.0.1" && export.Summary == "Edited summary");

        var sourceA = Path.Combine(fixtureRoot, "Source A"); var sourceB = Path.Combine(fixtureRoot, "Source B"); Directory.CreateDirectory(sourceA); Directory.CreateDirectory(sourceB);
        var candidates = new[] { new LauncherImport.Candidate { Name = "Example instance", ContentDir = sourceA, GameVersion = "1.21.1", Source = LauncherImport.Source.Prism }, new LauncherImport.Candidate { Name = "A very long instance name for clipping and alignment verification", ContentDir = sourceB, GameVersion = "1.21.1", Source = LauncherImport.Source.MultiMC }, new LauncherImport.Candidate { Name = "Unsupported instance", ContentDir = sourceB + "-unsupported", Unsupported = "Unsupported test loader", Source = LauncherImport.Source.CurseForge } };
        var importer = new LauncherImportForm(candidates, new[] { sourceA });
        window.ShowDiagnostics(importer); await Task.Delay(100); importer.UpdateLayout();
        check("Import UI marks imported and unsupported sources", Descendants(importer).OfType<TextBlock>().Any(t => t.Text == Localization.T("已导入", "Imported")) && Descendants(importer).OfType<CheckBox>().Any(c => !c.IsEnabled));
        var second = Descendants(importer).OfType<CheckBox>().Single(c => Equals(c.Tag, candidates[1].Id));
        ((IToggleProvider)new CheckBoxAutomationPeer(second).GetPattern(PatternInterface.Toggle)).Toggle(); await Task.Delay(30);
        check("Import UI supports selecting multiple sources", importer.Selected.Count == 2);
        await importer.Stop();

        var downloads = Path.Combine(fixtureRoot, "Download fixtures"); Directory.CreateDirectory(downloads);
        var expected = System.Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes("fixture"))).ToLowerInvariant();
        var files = new[] { new Modpack.ManualFile("Sample A", "sample-a.jar", "https://example.invalid", Path.Combine(fixtureRoot, "Collected", "a.jar"), expected), new Modpack.ManualFile("Sample B", "sample-b.jar", "https://example.invalid", Path.Combine(fixtureRoot, "Collected", "b.jar"), expected) };
        var trace = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var manual = new ManualDownloadsForm(files, pending => { var left = Modpack.CollectManualDownloads(pending, downloads); trace.Enqueue($"{elapsed.ElapsedMilliseconds}ms:{pending.Count}->{left.Count}"); return left; });
        var manualDialog = new KilnDialog { XamlRoot = root, Content = manual, LeadingContent = manual.Actions, SheetLayout = true, PreferredWidth = 520, CloseButtonText = "Later" };
        var manualShown = manualDialog.ShowAsync();
        manual.Start();
        try
        {
            if (manualShown.IsFaulted) await manualShown;
            ((FrameworkElement)root.Content).UpdateLayout(); await Task.Delay(100); manual.UpdateLayout(); await manual.RefreshAsync();
            File.WriteAllText(Path.Combine(downloads, "sample-a.jar"), "fixture");
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (manual.RemainingCount == 2 && wait.Elapsed < TimeSpan.FromSeconds(6)) await Task.Delay(50);
            if (manual.RemainingCount != 1 || !File.Exists(files[0].Destination)) throw new InvalidOperationException($"Automatic collection: {manual.PollState}, left={manual.RemainingCount}, destination={File.Exists(files[0].Destination)}, trace={string.Join(';', trace)}");
            check("Manual download UI automatically collects without a click", manual.RemainingCount == 1 && File.Exists(files[0].Destination));
            File.WriteAllText(Path.Combine(downloads, "sample-b.jar"), "fixture"); await manual.RefreshAsync();
            check("Manual download UI reaches the completed state", manual.RemainingCount == 0 && Descendants(manual).OfType<TextBlock>().Any(t => t.Text == Localization.T("全部文件已就位", "All files are in place")));
        }
        finally { manual.Stop(); manualDialog.Hide(); await manualShown; }

        var oldLanguage = Localizer.Shared.Language; var oldSize = window.AppWindow.Size;
        try
        {
            foreach (var language in new[] { AppLanguage.ZhHans, AppLanguage.ZhHant, AppLanguage.En })
            {
                Localizer.Shared.Language = language;
                foreach (var (width, height) in new[] { (920, 620), (1040, 680), (1200, 800) })
                {
                    window.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(width * root.RasterizationScale), (int)(height * root.RasterizationScale))); await Task.Delay(50);
                    var exportForm = new ModpackExportForm("Layout fixture", items);
                    await Inspect(new KilnDialog { XamlRoot = root, Title = Localization.T("导出整合包", "Export modpack"), Content = exportForm, PreferredWidth = 520, SheetLayout = true, PrimaryButtonText = Localization.T("导出…", "Export…"), CloseButtonText = Localization.T("取消", "Cancel") });
                    var importForm = new LauncherImportForm(candidates, new[] { sourceA });
                    await Inspect(new KilnDialog { XamlRoot = root, Title = Localization.T("从其他启动器导入", "Import from other launchers"), Content = importForm, LeadingContent = importForm.ChooseButton, PreferredWidth = 560, SheetLayout = true, PrimaryButtonText = Localization.T("导入（1）", "Import (1)"), CloseButtonText = Localization.T("取消", "Cancel") }); await importForm.Stop();
                    var info = new Modpack.Info(Modpack.Format.Modrinth, "Example modpack", "2.0", "A sample modpack", "1.21.1", ("Fabric", "0.16.0"), 48, Json.Null);
                    await Inspect(new KilnDialog { XamlRoot = root, Title = Localization.T("安装整合包", "Install modpack"), Content = new ModpackInstallForm(info, fixtureRoot), PreferredWidth = 480, SheetLayout = true, PrimaryButtonText = Localization.T("安装", "Install"), CloseButtonText = Localization.T("取消", "Cancel") });
                    check($"Workflow sheets stay inside {width}x{height} ({language})", true);
                }
            }
        }
        finally { Localizer.Shared.Language = oldLanguage; window.AppWindow.Resize(oldSize); }

        var cancelPack = Path.Combine(fixtureRoot, "cancel-fixture.mrpack");
        using (var archive = System.IO.Compression.ZipFile.Open(cancelPack, System.IO.Compression.ZipArchiveMode.Create))
        { using var writer = new StreamWriter(archive.CreateEntry("modrinth.index.json").Open()); writer.Write("{\"formatVersion\":1,\"name\":\"Cancel fixture\",\"versionId\":\"1.0.0\",\"dependencies\":{\"minecraft\":\"1.21.1\"},\"files\":[]}"); }
        var jobsBefore = MainWindow.AllJobs.Count;
        var cancelledInstall = ImportPage.InstallModpack(root, cancelPack);
        await Dismiss(Localization.T("取消", "Cancel"));
        check("Cancelling the real install confirmation creates no job", await cancelledInstall == ImportPage.InstallResult.Cancelled && MainWindow.AllJobs.Count == jobsBefore);
        var invalidPack = Path.Combine(fixtureRoot, "invalid-fixture.zip"); File.WriteAllText(invalidPack, "not a modpack");
        var failedInstall = ImportPage.InstallModpack(root, invalidPack);
        await Dismiss(Localization.T("好", "OK"));
        check("Invalid modpack reports failure rather than cancellation", await failedInstall == ImportPage.InstallResult.Failed);

        async Task Dismiss(string text)
        {
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(3))
            {
                ((FrameworkElement)root.Content).UpdateLayout();
                var overlay = Descendants(root.Content).OfType<Grid>().FirstOrDefault(g => g.Name == "KilnModalOverlay");
                var button = overlay == null ? null : Descendants(overlay).OfType<Button>().FirstOrDefault(b => b.Content is string value && value == text);
                if (button != null) { ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); return; }
                await Task.Delay(20);
            }
            throw new InvalidOperationException("Expected workflow dialog button: " + text);
        }

        async Task Inspect(KilnDialog dialog)
        {
            var shown = dialog.ShowAsync();
            try
            {
                if (shown.IsFaulted) await shown;
                await Task.Delay(50); ((FrameworkElement)root.Content).UpdateLayout();
                var overlay = Descendants(root.Content).OfType<Grid>().Single(g => g.Name == "KilnModalOverlay");
                var surface = Descendants(overlay).OfType<Grid>().Single(g => g.Name == "KilnDialogSurface");
                var surfaceOrigin = surface.TransformToVisual(root.Content).TransformPoint(new Windows.Foundation.Point(0, 0));
                var footerPositions = new List<double>();
                foreach (var button in Descendants(overlay).OfType<Button>().Where(b => b.Visibility == Visibility.Visible && b.ActualWidth > 0))
                {
                    var origin = button.TransformToVisual(root.Content).TransformPoint(new Windows.Foundation.Point(0, 0));
                    if (origin.X < -.5 || origin.Y < -.5 || origin.X + button.ActualWidth > root.Size.Width + .5 || origin.Y + button.ActualHeight > root.Size.Height + .5) throw new InvalidOperationException("Sheet button outside the window: " + button.Content);
                    if (origin.X < surfaceOrigin.X - .5 || origin.Y < surfaceOrigin.Y - .5 || origin.X + button.ActualWidth > surfaceOrigin.X + surface.ActualWidth + .5 || origin.Y + button.ActualHeight > surfaceOrigin.Y + surface.ActualHeight + .5) throw new InvalidOperationException("Sheet button outside the dialog: " + button.Content);
                    if (button.Content is string title && (title == dialog.PrimaryButtonText || title == dialog.CloseButtonText)) footerPositions.Add(origin.Y);
                }
                if (footerPositions.Count > 1 && footerPositions.Max() - footerPositions.Min() > 1) throw new InvalidOperationException("Dialog footer buttons wrapped onto separate rows.");
            }
            finally { dialog.Hide(); await shown; }
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
}
