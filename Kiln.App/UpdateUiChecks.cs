using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Kiln.App;

internal static class UpdateUiChecks
{
    internal static async Task Run(MainWindow window, Action<string, bool> check)
    {
        var fixture = new Updater.Release("99.0.0w", "# 修复\n**更新说明** [发布页](https://example.invalid)", "https://example.invalid", "https://example.invalid/update.zip", "update.zip", new string('a', 64), 1234);
        foreach (var expected in new[] { ContentDialogResult.None, ContentDialogResult.Secondary, ContentDialogResult.Primary })
        {
            var dialog = AppUpdates.CreatePrompt(window, fixture); var shown = dialog.ShowAsync();
            ((FrameworkElement)window.Content).UpdateLayout();
            var overlay = Descendants(window.Content).OfType<Grid>().Single(g => g.Name == "KilnModalOverlay");
            var buttons = Descendants(overlay).OfType<Button>().Where(b => b.Content is string).ToArray();
            var label = expected switch { ContentDialogResult.Primary => dialog.PrimaryButtonText, ContentDialogResult.Secondary => dialog.SecondaryButtonText, _ => dialog.CloseButtonText };
            ((IInvokeProvider)new ButtonAutomationPeer(buttons.Single(b => (string)b.Content == label)).GetPattern(PatternInterface.Invoke)).Invoke();
            var actual = await shown;
            check("Real updater prompt returns the selected action without installing: " + expected, actual == expected && !KilnDialog.IsOpen && dialog.DefaultButton == ContentDialogButton.Primary);
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
}
