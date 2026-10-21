using Kiln.App.Pages;
using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Kiln.App;

// Real settings builders with an in-memory model; no account, registry or user settings writes.
internal static class SettingsUiChecks
{
    internal static async Task Run(MainWindow window, string fixtureRoot, Action<string, bool> check)
    {
        var settings = LauncherSettings.Ephemeral();
        var java = new JavaInfo(Path.Combine(fixtureRoot, "Java 21", "bin", "java.exe"), "21.0.8", 21, "x64", false);
        settings.CustomJavas = new() { java.Path };
        var page = new SetupPage(settings, new[] { java });
        await Show(page.Section(0));
        var automatic = Find<ToggleSwitch>("AutomaticMemory"); var manual = Find<Grid>("ManualMemoryRow");
        var bar = Tree(window.Content).OfType<MemoryAllocationBar>().Single();
        check("Automatic memory hides the manual slider and shows the original quarter-width bar", automatic.IsOn && manual.Visibility == Visibility.Collapsed && Math.Abs(bar.Fraction - .25) < .001);
        automatic.IsOn = false; var memory = Find<Slider>("ManualMemory"); memory.Value = 2.5;
        await Layout();
        check("Manual memory slider updates the setting and allocation bar", manual.Visibility == Visibility.Visible && settings.MemoryGB == 2.5 && Math.Abs(bar.Fraction - 2.5 / Diagnostics.PhysicalMemoryGB) < .001);
        automatic.IsOn = true;
        check("Returning to automatic memory preserves the manual allocation", settings.MemoryAuto && settings.MemoryGB == 2.5 && manual.Visibility == Visibility.Collapsed);
        var mode = Find<ComboBox>("GameWindowMode");
        check("Default game window hides custom dimensions", Find<Grid>("CustomGameWindow").Visibility == Visibility.Collapsed);
        mode.SelectedIndex = (int)WindowMode.Custom; await Layout();
        Find<NumberBox>("GameWindowWidth").Value = 1280; Find<NumberBox>("GameWindowHeight").Value = 720;
        check("Custom game window reveals and stores both dimensions", Find<Grid>("CustomGameWindow").Visibility == Visibility.Visible && settings.WindowWidth == 1280 && settings.WindowHeight == 720);
        mode.SelectedIndex = (int)WindowMode.Fullscreen;
        check("Fullscreen hides dimensions without discarding them", Find<Grid>("CustomGameWindow").Visibility == Visibility.Collapsed && settings.WindowWidth == 1280);
        await Show(page.Section(1)); var gc = Find<Grid>("GarbageCollectorChoice");
        Invoke(Tree(gc).OfType<Button>().Last());
        check("Performance segments select generational ZGC at the original 28-point height", settings.GcMode == GCMode.Zgc && Math.Abs(gc.ActualHeight - 28) < .5);
        await Show(page.Section(2));
        Invoke(Tree(window.Content).OfType<Button>().Single(b => b.Name == "JavaChoice" && (string)b.Tag == java.Path)); await Layout();
        var selected = Tree(window.Content).OfType<Button>().Single(b => b.Name == "JavaChoice" && (string)b.Tag == java.Path);
        var row = Tree(selected).OfType<Grid>().Single(g => g.Name == "JavaRow");
        var tick = Find<FontIcon>("JavaSelectedCheck");
        check("Java selection fills the entire row with a right-aligned check", settings.JavaPath == java.Path && row.ActualWidth > 500 && row.ActualWidth - Right(tick, row) >= 11 && row.ActualWidth - Right(tick, row) <= 14);
        Invoke(Tree(window.Content).OfType<Button>().Single(b => Equals(b.Content, "−"))); await Layout();
        check("Removing a custom Java returns to automatic and removes its row", settings.JavaPath.Length == 0 && settings.CustomJavas.Count == 0 && Tree(window.Content).OfType<Button>().Count(b => b.Name == "JavaChoice") == 1);
        await Show(page.Section(3)); Find<Slider>("DownloadConcurrency").Value = 37;
        check("Concurrent download slider stores its count", settings.DownloadThreads == 37 && Tree(window.Content).OfType<TextBlock>().Any(t => t.Text == "37"));
        await Show(page.Section(4)); var priorLanguage = Localizer.Shared.Language;
        Invoke(Tree(Find<Grid>("LanguageChoice")).OfType<Button>().Last());
        check("Diagnostic settings can change language without changing the user's active language", settings.Language == AppLanguage.En && Localizer.Shared.Language == priorLanguage);
        check("Clear-cache action is restored beside data and logs", Find<Button>("ClearCache").IsEnabled);
        var oldSize = window.AppWindow.Size;
        try
        {
            foreach (var language in new[] { AppLanguage.ZhHans, AppLanguage.ZhHant, AppLanguage.En })
            foreach (var (width, height) in new[] { (920, 620), (1040, 680), (1200, 800) })
            {
                Localizer.Shared.Language = language;
                var scale = ((FrameworkElement)window.Content).XamlRoot.RasterizationScale;
                window.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(height * scale)));
                var actualPage = new SetupPage(settings, new[] { java });
                for (var section = 0; section < 5; section++)
                {
                    await Show(actualPage.Section(section));
                    var viewport = Tree(window.Content).OfType<ScrollViewer>().First(s => s.Content is StackPanel);
                    var rows = Tree(viewport).OfType<Grid>().Where(g => g.Name == "SettingRow" && g.Visibility == Visibility.Visible).ToArray();
                    foreach (var setting in rows)
                    {
                        var control = (FrameworkElement)setting.Children[1];
                        if (Left(control, setting) < 245 || Right(control, setting) > setting.ActualWidth + .5) throw new InvalidOperationException($"Clipped settings control: {section}/{control.GetType().Name}, {setting.ActualWidth}, {Right(control, setting)}");
                        if (control is ComboBox or TextBox or PasswordBox or NumberBox or Button || control.Name is "LanguageChoice" or "GarbageCollectorChoice")
                            if (Math.Abs(control.ActualWidth - 320) > .5 || Math.Abs(control.ActualHeight - 28) > .5) throw new InvalidOperationException($"Incorrect field size: {section}/{control.GetType().Name} {control.ActualWidth}x{control.ActualHeight}");
                    }
                    foreach (var actions in Tree(viewport).OfType<Grid>().Where(g => g.Children.Count > 1 && g.Children.All(c => c is Button)))
                    {
                        var buttons = actions.Children.Cast<Button>().ToArray();
                        if (buttons.Max(b => b.ActualWidth) - buttons.Min(b => b.ActualWidth) > 1 || buttons.Any(b => Right(b, actions) > actions.ActualWidth + .5)) throw new InvalidOperationException("Unequal settings action buttons");
                    }
                }
                check($"All five settings sections align at {width}x{height} ({language})", true);
            }
        }
        finally { Localizer.Shared.Language = priorLanguage; window.AppWindow.Resize(oldSize); }
        var cache = Path.Combine(fixtureRoot, "Cache test"); var outside = Path.Combine(fixtureRoot, "Keep outside.txt");
        Directory.CreateDirectory(Path.Combine(cache, "nested")); File.WriteAllText(Path.Combine(cache, "nested", "payload.txt"), "fixture"); File.WriteAllText(outside, "keep");
        CacheMaintenance.ClearContents(cache);
        check("Cache clearing preserves its root and unrelated files", Directory.Exists(cache) && !Directory.EnumerateFileSystemEntries(cache).Any() && File.ReadAllText(outside) == "keep");
        var rejected = false; try { CacheMaintenance.EnsureIdle(new[] { new Job("Busy fixture") }, Array.Empty<GameProcess>()); } catch (LauncherException) { rejected = true; }
        check("Cache clearing refuses running tasks and game-data overlap", rejected && CacheMaintenance.ContainsGameData(cache, new[] { Path.Combine(cache, "worlds") }) && !CacheMaintenance.ContainsGameData(cache, new[] { cache + " sibling" }));

        async Task Layout() { await Task.Delay(30); ((FrameworkElement)window.Content).UpdateLayout(); }
        async Task Show(UIElement content) { window.ShowDiagnostics(new Border { Padding = new Thickness(28, 48, 28, 24), Child = content }); await Layout(); await Layout(); }
        T Find<T>(string name) where T : FrameworkElement => Tree(window.Content).OfType<T>().Single(e => e.Name == name);
    }
    internal static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    internal static double Left(FrameworkElement element, UIElement relative) => element.TransformToVisual(relative).TransformPoint(new Windows.Foundation.Point()).X;
    internal static double Right(FrameworkElement element, UIElement relative) => Left(element, relative) + element.ActualWidth;
    internal static IEnumerable<DependencyObject> Tree(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var descendant in Tree(child)) yield return descendant; }
    }
}
