using Kiln.App.Pages;
using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Kiln.App;

internal static class HomeUiChecks
{
    internal static async Task Run(MainWindow window, Action<string, bool> check)
    {
        var games = new List<GameProcess>(); var hidden = new HashSet<Guid>();
        for (var preset = 0; preset < 3; preset++)
        {
            games.Clear(); hidden.Clear(); window.ApplyWindowPreset(preset);
            var page = new HomePage(games, hidden); window.ShowDiagnostics(page); await Layout();
            var hero = Tree(page).OfType<Grid>().Single(g => g.Name == "HomePixelRegion");
            var height = hero.ActualHeight;
            for (var i = 0; i < 3; i++) games.Add(new GameProcess("A long instance name for session layout " + i) { State = i == 0 ? GameProcess.GameState.Running : GameProcess.GameState.Exited });
            page.RefreshRunningGames(); await Layout();
            check($"Home sessions do not shrink the pixel animation at window preset {preset}", height > 180 && Math.Abs(hero.ActualHeight - height) < .1 && !Tree(page).OfType<ScrollViewer>().Any());
            Invoke(Find<Button>(page, "HomeRunningRecords")); await Layout();
            var overlay = Tree(window.Content).OfType<Grid>().Single(g => g.Name == "KilnModalOverlay");
            check($"Home session dialog keeps all three close controls inside the window at preset {preset}",
                Tree(overlay).OfType<Button>().Where(b => b.Name.StartsWith("DismissGame_")).Count() == 3 && Tree(overlay).OfType<Button>().All(b => b.TransformToVisual(window.Content).TransformPoint(new(0, 0)).X + b.ActualWidth <= ((FrameworkElement)window.Content).ActualWidth + 1));
            var running = games[0]; Invoke(Find<Button>(overlay, "DismissGame_" + running.Id.ToString("N"))); await Layout();
            check($"Closing a session hides only its record and keeps its process tracked at preset {preset}", hidden.Contains(running.Id) && !running.Killed && games.Contains(running) && running.State == GameProcess.GameState.Running && !Tree(window.Content).OfType<Button>().Any(b => b.Name == "DismissGame_" + running.Id.ToString("N")));
            var close = Tree(overlay).OfType<Button>().Single(b => b.Content is string s && s == Localization.T("关闭", "Close")); Invoke(close); await Layout();
            var replacement = new HomePage(games, hidden); window.ShowDiagnostics(replacement); await Layout();
            replacement.RefreshRunningGames();
            check($"Hidden sessions stay dismissed after navigating back at preset {preset}", Find<Button>(replacement, "HomeRunningRecords").Content.ToString()!.EndsWith("2"));
            Invoke(Find<Button>(replacement, "DismissHomeSessions")); await Layout();
            check($"Home can dismiss all records without terminating games at preset {preset}", Find<Button>(replacement, "HomeRunningRecords").Visibility == Visibility.Collapsed && games.All(g => !g.Killed));
            games.Insert(0, new GameProcess("New session")); replacement.RefreshRunningGames(); await Layout();
            check($"A new game session appears after older records were dismissed at preset {preset}", Find<Button>(replacement, "HomeRunningRecords").Visibility == Visibility.Visible && Find<Button>(replacement, "HomeRunningRecords").Content.ToString()!.EndsWith("1"));
        }
        window.ApplyWindowPreset(1);
        async Task Layout() { await Task.Delay(100); ((FrameworkElement)window.Content).UpdateLayout(); }
    }
    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement => Tree(root).OfType<T>().Single(v => v.Name == name);
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var item in Tree(child)) yield return item; }
    }
}
