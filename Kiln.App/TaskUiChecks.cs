using Kiln.App.Pages;
using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Kiln.App;

internal static class TaskUiChecks
{
    internal static UIElement Preview()
    {
        MainWindow.ClearFinishedJobs();
        var running = MainWindow.NewJob("安装 Minecraft 1.21.1 · Fabric", silent: true); running.Enter("下载游戏文件 / Downloading game files", 1, 4); running.FileFraction = .72; running.FileText = "57 / 80"; running.Speed = 3145728;
        var success = MainWindow.NewJob("下载资源包 / Resource pack", silent: true); success.State = Job.JobState.Success;
        var failure = MainWindow.NewJob("下载测试 / Download test", silent: true); failure.Fail("连接超时。请检查网络后重试。 / The connection timed out. Check your network and retry.");
        var cancelled = MainWindow.NewJob("Java 安装 / Java installation", silent: true); cancelled.State = Job.JobState.Cancelled;
        return new JobsPage();
    }
    internal static async Task Run(MainWindow window, string fixtureRoot, Action<string, bool> check)
    {
        var progress = Ui.Progress(.25); progress.Width = 320;
        var area = new ScrollViewer { Width = 320, Height = 120, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Enabled, ZoomMode = ZoomMode.Disabled, Content = new Border { Width = 960, Height = 960, Background = Ui.Brush("KilnPanelSecondary") } };
        var fixture = new Border { Padding = new Thickness(28, 48, 28, 24), Child = Ui.Col(progress, area), VerticalAlignment = VerticalAlignment.Top };
        window.ShowDiagnostics(fixture); await Layout();
        var indicator = Part<Rectangle>(progress, "DeterminateProgressBarIndicator");
        check("Kiln progress fill matches the current value", Math.Abs(indicator.ActualWidth - 80) < 1 && Math.Abs(progress.ActualHeight - 4) < .5);
        progress.Value = .75; await Layout();
        check("Kiln progress fill advances and resizes with its container", Math.Abs(indicator.ActualWidth - 240) < 1);
        progress.Width = 160; await Layout(); check("Progress geometry updates when the window size changes", Math.Abs(indicator.ActualWidth - 120) < 1);
        progress.ShowError = true; await Layout(); check("Progress error state uses the Kiln warning palette", ((SolidColorBrush)indicator.Fill).Color == ((SolidColorBrush)Ui.Brush("KilnRed")).Color);
        progress.ShowError = false; progress.ShowPaused = true; await Layout(); check("Progress pause state uses the Kiln pause palette", ((SolidColorBrush)indicator.Fill).Color == ((SolidColorBrush)Ui.Brush("KilnYellow")).Color);
        progress.ShowPaused = false; progress.IsIndeterminate = true; await Layout();
        var indeterminate = Part<Rectangle>(progress, "IndeterminateProgressBarIndicator");
        check("Indeterminate progress uses the custom indicator", indeterminate.Opacity > .5 && indeterminate.ActualWidth > 0 && indicator.Opacity == 0);
        var animationStart = ((CompositeTransform)indeterminate.RenderTransform).TranslateX; await Task.Delay(200);
        check("Indeterminate progress animates inside its clipped track", Math.Abs(((CompositeTransform)indeterminate.RenderTransform).TranslateX - animationStart) > 1);
        progress.IsIndeterminate = false;
        var vertical = Part<ScrollBar>(area, "VerticalScrollBar"); var horizontal = Part<ScrollBar>(area, "HorizontalScrollBar");
        check("Scroll view uses Kiln viewport and both custom scrollbar templates", Part<Grid>(area, "KilnViewportRoot").ActualWidth == 320 && Part<Grid>(vertical, "KilnScrollRoot").ActualWidth <= 8.5 && Part<Grid>(horizontal, "KilnScrollRoot").ActualHeight <= 8.5);
        area.ChangeView(280, 340, null, true); await Layout();
        check("Custom scrollbars track native viewport offsets", Math.Abs(vertical.Value - 340) < 1 && Math.Abs(horizontal.Value - 280) < 1);
        var before = area.VerticalOffset;
        ((IInvokeProvider)new RepeatButtonAutomationPeer(Part<RepeatButton>(vertical, "VerticalLargeIncrease")).GetPattern(PatternInterface.Invoke)).Invoke(); await Layout();
        check("Scrollbar page-click advances the actual viewport", area.VerticalOffset > before);
        area.ChangeView(area.ScrollableWidth, area.ScrollableHeight, null, true); await Layout();
        check("Custom scrollbar thumbs stay within their tracks at the end", SettingsUiChecks.Right(Part<Thumb>(horizontal, "HorizontalThumb"), horizontal) <= horizontal.ActualWidth + .5 && Bottom(Part<Thumb>(vertical, "VerticalThumb"), vertical) <= vertical.ActualHeight + .5);
        var job = MainWindow.NewJob("下载任务：长标题与进度 / Install progress", silent: true); var failed = MainWindow.NewJob("Failed fixture", silent: true);
        try
        {
            job.Enter("下载文件 / Downloading files", 1, 4); job.FileFraction = .996; job.FileText = "17 / 34"; job.Speed = 1048576;
            failed.Fail("Synthetic network failure");
            var page = new JobsPage(); window.ShowDiagnostics(new Border { Padding = new Thickness(28, 48, 28, 24), Child = page }); await Task.Delay(80); ((FrameworkElement)window.Content).UpdateLayout();
            var row = SettingsUiChecks.Tree(page).OfType<UserControl>().Single(c => c.Name == "TaskRow" && c.Tag is Guid id && id == job.Id);
            var failedRow = SettingsUiChecks.Tree(page).OfType<UserControl>().Single(c => c.Name == "TaskRow" && c.Tag is Guid id && id == failed.Id);
            check("Actual task rows show stage, truncated percentage and real progress", Part<TextBlock>(row, "TaskPercent").Text == "49%" && Math.Abs(Part<ProgressBar>(row, "TaskProgress").Value - .499) < .001 && Part<TextBlock>(row, "TaskStatus").Text == job.Stage);
            check("Failed task row keeps its error text and red state", Part<TextBlock>(failedRow, "TaskStatus").Text == failed.Error && ((SolidColorBrush)Part<FontIcon>(failedRow, "TaskIcon").Foreground).Color == ((SolidColorBrush)Ui.Brush("KilnRed")).Color && Part<ProgressBar>(failedRow, "TaskProgress").Visibility == Visibility.Collapsed);
            var cancel = Part<Button>(row, "CancelTask"); SettingsUiChecks.Invoke(cancel);
            check("Actual task Cancel button requests cancellation and disables repeat clicks", job.Token.IsCancellationRequested && !cancel.IsEnabled);
            job.FileFraction = double.NaN; await Task.Delay(550);
            check("Non-finite progress cannot corrupt the task UI", Part<ProgressBar>(row, "TaskProgress").Value == 0 && Part<TextBlock>(row, "TaskPercent").Text == "0%");
            job.State = Job.JobState.Success; await Task.Delay(550);
            check("Successful task hides progress and cancellation with the original green state", Part<ProgressBar>(row, "TaskProgress").Visibility == Visibility.Collapsed && cancel.Visibility == Visibility.Collapsed && ((SolidColorBrush)Part<FontIcon>(row, "TaskIcon").Foreground).Color == ((SolidColorBrush)Ui.Brush("KilnGreen")).Color);
        }
        finally { job.State = failed.State = Job.JobState.Cancelled; MainWindow.ClearFinishedJobs(); window.RefreshJobIndicator(); }
        using (var server = new DownloadFixture())
        {
            var download = MainWindow.NewJob("Loopback download progress fixture", silent: true); download.Enter("Downloading fixture", 0, 1);
            var destination = System.IO.Path.Combine(fixtureRoot, "task-download.bin");
            var downloading = Transfer();
            try
            {
                var actualPage = new JobsPage(); window.ShowDiagnostics(new Border { Padding = new Thickness(28, 48, 28, 24), Child = actualPage });
                UserControl? actualRow = null; var timeout = System.Diagnostics.Stopwatch.StartNew();
                while (timeout.Elapsed < TimeSpan.FromSeconds(8) && !downloading.IsCompleted)
                {
                    await Task.Delay(100); ((FrameworkElement)window.Content).UpdateLayout();
                    actualRow = SettingsUiChecks.Tree(actualPage).OfType<UserControl>().SingleOrDefault(c => c.Name == "TaskRow" && c.Tag is Guid id && id == download.Id);
                    if (actualRow != null && Part<ProgressBar>(actualRow, "TaskProgress").Value > 0) break;
                }
                check("Real Downloader byte transfers advance the actual task progress bar", actualRow != null && !downloading.IsCompleted && download.Fraction > 0 && Part<ProgressBar>(actualRow, "TaskProgress").Value > 0 && Part<Rectangle>(actualRow, "DeterminateProgressBarIndicator").ActualWidth > 0);
                SettingsUiChecks.Invoke(Part<Button>(actualRow!, "CancelTask")); await downloading.WaitAsync(TimeSpan.FromSeconds(10)); await Task.Delay(550);
                check("Cancelling the real task stops its HTTP download and hides progress", download.State == Job.JobState.Cancelled && Part<ProgressBar>(actualRow!, "TaskProgress").Visibility == Visibility.Collapsed);
                var clear = Part<Button>(actualPage, "ClearFinishedTasks"); SettingsUiChecks.Invoke(clear); await Task.Delay(60);
                check("Clear finished tasks restores the original empty state", MainWindow.AllJobs.Count == 0 && Part<StackPanel>(actualPage, "EmptyTasks").Visibility == Visibility.Visible && clear.Visibility == Visibility.Collapsed);
            }
            finally { download.Cancel(); await downloading.WaitAsync(TimeSpan.FromSeconds(10)); MainWindow.ClearFinishedJobs(); window.RefreshJobIndicator(); }
            async Task Transfer()
            {
                try { await Downloader.Run(new[] { new DownloadItem(server.Url, destination, size: DownloadFixture.Length) { Urls = new[] { server.Url } } }, download, threads: 1, checkHash: false); download.State = Job.JobState.Success; }
                catch (OperationCanceledException) { download.State = Job.JobState.Cancelled; }
                catch (Exception error) { download.Fail(error.Friendly()); }
            }
        }
        async Task Layout() { await Task.Delay(60); fixture.UpdateLayout(); }
    }
    private static T Part<T>(DependencyObject root, string name) where T : FrameworkElement => SettingsUiChecks.Tree(root).OfType<T>().Single(e => e.Name == name);
    private static double Bottom(FrameworkElement element, UIElement root) => element.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, element.ActualHeight)).Y;
    private sealed class DownloadFixture : IDisposable
    {
        internal const int Length = 4 * 1024 * 1024;
        private readonly System.Net.Sockets.TcpListener _listener = new(System.Net.IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly CancellationToken _token;
        internal string Url { get; }
        internal DownloadFixture()
        {
            _token = _stop.Token; _listener.Start(); Url = "http://127.0.0.1:" + ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port + "/fixture.bin"; _ = Serve();
        }
        private async Task Serve()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_token); await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, System.Text.Encoding.ASCII, false, 1024, leaveOpen: true);
                var lines = 0; while (await reader.ReadLineAsync(_token) is { Length: > 0 }) if (++lines > 100) throw new IOException("Unexpected fixture request");
                await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {Length}\r\nConnection: close\r\n\r\n"), _token);
                var block = new byte[16384];
                for (var sent = 0; sent < Length; sent += block.Length) { await stream.WriteAsync(block, _token); await Task.Delay(20, _token); }
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or System.Net.Sockets.SocketException) { }
        }
        public void Dispose() { _stop.Cancel(); _listener.Stop(); _stop.Dispose(); }
    }
}
