using Kiln.Core;
using Microsoft.UI.Xaml;

namespace Kiln.App;

internal sealed class ActivationDispatcher : IDisposable
{
    private readonly MainWindow _window;
    private readonly Func<LaunchLink, Task> _launch;
    private readonly Queue<LaunchLink> _pending = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _ready, _launching, _disposed;

    internal ActivationDispatcher(MainWindow window, Func<LaunchLink, Task>? launch = null)
    {
        _window = window;
        _launch = launch ?? (async request =>
        {
            var instance = request.Resolve(InstanceScanner.Scan(LauncherSettings.Shared.MinecraftFolder), LauncherSettings.Shared.SelectedInstance);
            LauncherSettings.Shared.SelectedInstance = instance.Name;
            window.NavigateTo("home");
            await LaunchCoordinator.Launch(instance, request.Options());
        });
        _timer.Tick += (_, _) => Pump();
    }

    internal void Ready() { _ready = true; Pump(); }
    internal void Receive(string message)
    {
        if (_disposed) return;
        SystemIntegration.BringToFront(_window);
        if (message.Length == 0 || LaunchLink.Parse(message) is not { } request || request.Action == LauncherAction.Show) return;
        // The original ignores another launch while preparing a game.
        if (request.Action == LauncherAction.Launch && (_launching || MainWindow.LaunchJob?.State == Job.JobState.Running)) return;
        if (_pending.Count >= 32 || _pending.Contains(request)) return;
        _pending.Enqueue(request); Pump();
    }

    private void Pump()
    {
        if (_disposed || _pending.Count == 0) { _timer.Stop(); return; }
        if (!_ready || KilnDialog.IsOpen) { _timer.Start(); return; }
        _timer.Stop();
        while (_pending.TryDequeue(out var request))
        {
            switch (request.Action)
            {
                case LauncherAction.Tasks: _window.NavigateTo("jobs"); break;
                case LauncherAction.Log:
                    if (MainWindow.Games.FirstOrDefault(g => g.State == GameProcess.GameState.Crashed) is { } game) _window.ShowGameLog(game);
                    break;
                case LauncherAction.Launch:
                    if (!_launching && MainWindow.LaunchJob?.State != Job.JobState.Running) _ = Launch(request);
                    break;
            }
        }
    }

    private async Task Launch(LaunchLink request)
    {
        _launching = true;
        try { await _launch(request); }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!_disposed) await Pages.Ui.ShowError(_window.Content.XamlRoot, e); }
        finally { _launching = false; }
    }

    public void Dispose() { _disposed = true; _timer.Stop(); _pending.Clear(); }
}
