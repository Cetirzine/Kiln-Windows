using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;

namespace Kiln.App;

internal static class ActivationUiChecks
{
    internal static async Task Run(MainWindow window, McInstance instance, string fixtureRoot, Action<string, bool> check)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var launches = new List<LaunchLink>();
        using var dispatcher = new ActivationDispatcher(window, async request => { launches.Add(request); await gate.Task; });
        var link = LaunchLink.Create(instance.Name, "World + 世界", "localhost:25565");
        dispatcher.Receive(link);
        check("Startup activation waits until the window is ready", launches.Count == 0);
        dispatcher.Ready();
        check("Ready window receives the complete launch request", launches.Single() == LaunchLink.Parse(link));
        dispatcher.Receive(link); dispatcher.Receive("kiln://play/Another");
        check("Another activation cannot start a second concurrent launch", launches.Count == 1);
        dispatcher.Receive("kiln://tasks");
        check("Task activation works while a launch is pending", await WaitForPage<Pages.JobsPage>(window));
        gate.SetResult(); await Task.Delay(30);

        var fakeGame = new GameProcess(instance.Name) { Instance = instance, State = GameProcess.GameState.Crashed };
        fakeGame.Append("Synthetic crash log fixture\n"); MainWindow.Games.Insert(0, fakeGame);
        try
        {
            dispatcher.Receive("kiln://log");
            check("Crash activation opens the crashed game's log", await WaitForPage<Pages.GameLogPage>(window));
        }
        finally { MainWindow.Games.Remove(fakeGame); }

        var dialog = new KilnDialog { XamlRoot = window.Content.XamlRoot, Title = "Activation fixture", Content = "Synthetic modal", CloseButtonText = "Close test" };
        var shown = dialog.ShowAsync();
        if (shown.IsFaulted) await shown;
        dispatcher.Receive(link); await Task.Delay(150);
        check("Protocol launch waits behind an open custom dialog", launches.Count == 1 && KilnDialog.IsOpen);
        dialog.Hide(); await shown; await Task.Delay(200);
        check("Closing the dialog releases exactly one deferred launch", launches.Count == 2);

        var name = "Kiln.Activation.Test." + Guid.NewGuid().ToString("N");
        using var owner = new InstanceChannel(name);
        if (!owner.TryOwn()) throw new InvalidOperationException("Test activation channel is already owned.");
        var receipt = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.Listen(message =>
        {
            if (!window.DispatcherQueue.TryEnqueue(() => { dispatcher.Receive(message); receipt.TrySetResult(message); })) throw new InvalidOperationException("Test window is unavailable.");
        });
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--activation-test-client"); start.ArgumentList.Add(name); start.ArgumentList.Add("kiln://tasks");
        using var child = Process.Start(start)!;
        try
        {
            var sawWindow = false; var elapsed = Stopwatch.StartNew();
            while (!child.HasExited && elapsed.Elapsed < TimeSpan.FromSeconds(15))
            {
                child.Refresh(); sawWindow |= child.MainWindowHandle != IntPtr.Zero; await Task.Delay(50);
            }
            if (!child.HasExited) throw new TimeoutException("Secondary activation process did not exit.");
            var received = await receipt.Task.WaitAsync(TimeSpan.FromSeconds(3));
            check("Second Kiln process forwards and exits without opening a window", child.ExitCode == 0 && !sawWindow && received == "kiln://tasks");
            check("Cross-process activation reaches the actual task page", await WaitForPage<Pages.JobsPage>(window));
        }
        finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
        receipt = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shortcut = Path.Combine(fixtureRoot, "Forward activation.lnk");
        ShellShortcuts.Write(shortcut, Environment.ProcessPath!, "--activation-test-client " + name + " \"" + link + "\"", Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"), "Isolated shortcut activation fixture");
        using var shortcutProcess = Process.Start(new ProcessStartInfo(shortcut) { UseShellExecute = true });
        var delivered = await receipt.Task.WaitAsync(TimeSpan.FromSeconds(10));
        check("Windows shell shortcut forwards the complete world/server request", delivered == link && launches.Count == 3 && launches[^1] == LaunchLink.Parse(link));
        if (shortcutProcess != null)
        {
            try { await shortcutProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { if (!shortcutProcess.HasExited) shortcutProcess.Kill(entireProcessTree: true); }
        }
    }

    private static async Task<bool> WaitForPage<T>(MainWindow window) where T : Page
    {
        // Navigation commits before WinUI attaches its page to the visual tree. Wait for
        // the actual loaded page rather than assuming a frame finishes within 60ms.
        var elapsed = Stopwatch.StartNew();
        do
        {
            ((FrameworkElement)window.Content).UpdateLayout();
            if (Descendants(window.Content).OfType<T>().Any(page => page.IsLoaded)) return true;
            await Task.Delay(30);
        } while (elapsed.Elapsed < TimeSpan.FromSeconds(3));
        return false;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
}
