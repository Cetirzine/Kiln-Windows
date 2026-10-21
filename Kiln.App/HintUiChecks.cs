using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Kiln.App;

internal static class HintUiChecks
{
    internal static async Task Run(MainWindow window, Action<string, bool> check)
    {
        var now = 0d; var hints = new HintOverlay(() => now);
        window.ShowDiagnostics(hints);
        for (var i = 0; i < 5; i++) hints.Show("Temporary hint " + i, HintKind.Success);
        await Task.Delay(300); ((FrameworkElement)window.Content).UpdateLayout();
        var cards = Descendants(hints).OfType<Border>().Where(b => b.Name == "KilnHintCard").ToArray();
        var positions = cards.Select(c => c.TransformToVisual(hints).TransformPoint(new Windows.Foundation.Point()).Y).Order().ToArray();
        check("Hint overlay keeps four separate nonmodal cards", hints.Count == 4 && cards.Length == 4 && positions.Zip(positions.Skip(1)).All(p => p.Second - p.First >= 32) && !KilnDialog.IsOpen);
        foreach (var card in cards)
        {
            var origin = card.TransformToVisual(window.Content).TransformPoint(new Windows.Foundation.Point());
            if (origin.X < 0 || origin.Y < 0 || origin.X + card.ActualWidth > ((FrameworkElement)window.Content).ActualWidth || origin.Y + card.ActualHeight > ((FrameworkElement)window.Content).ActualHeight) throw new InvalidOperationException("Hint card outside the window.");
        }
        hints.SetHoverForTest(true); now = 10; hints.Tick();
        check("Hover keeps expired hints visible", hints.Count == 4);
        hints.SetHoverForTest(false); hints.Tick(); await Task.Delay(300);
        check("Expired hints clear after the pointer leaves", hints.Count == 0 && !Descendants(hints).OfType<Border>().Any(b => b.Name == "KilnHintCard"));
        hints.Show("Closable synthetic hint", HintKind.Info); await Task.Delay(30);
        var close = Descendants(hints).OfType<Button>().Single();
        ((IInvokeProvider)new ButtonAutomationPeer(close).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(300);
        check("Hint close control removes its card", hints.Count == 0 && !Descendants(hints).OfType<Button>().Any());
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
}
