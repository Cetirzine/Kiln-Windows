using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

// Matches the original two-second collection loop without blocking WinUI's input thread.
internal sealed class ManualDownloadsForm : UserControl
{
    private IReadOnlyList<Modpack.ManualFile> _remaining;
    private readonly int _total;
    private readonly TextBlock _heading = Ui.Text("", 17, FontWeights.Bold);
    private readonly TextBlock _status = Ui.Text("", 11.5);
    private readonly Dictionary<string, FrameworkElement> _rows = new();
    private CancellationTokenSource? _polling;
    private readonly Func<IReadOnlyList<Modpack.ManualFile>, IReadOnlyList<Modpack.ManualFile>> _collect;
    private readonly Button _openAll;
    private Task? _refresh;
    private bool _stopped;
    public int RemainingCount => _remaining.Count;
    internal string PollState => $"loaded={IsLoaded}, stopped={_stopped}, polling={_polling != null}, refresh={_refresh?.Status}";
    public FrameworkElement Actions { get; }
    public event Action? Changed;
    private static string T(string zh, string en) => Localization.T(zh, en);

    public ManualDownloadsForm(IReadOnlyList<Modpack.ManualFile> files, Func<IReadOnlyList<Modpack.ManualFile>, IReadOnlyList<Modpack.ManualFile>>? collect = null)
    {
        _remaining = files.DistinctBy(f => f.Id).ToList(); _total = _remaining.Count;
        _collect = collect ?? (pending => Modpack.CollectManualDownloads(pending));
        var explanation = Ui.Text(T("这些文件的作者不允许第三方启动器下载。点击“下载”打开 CurseForge 页面下载，Kiln 会自动从“下载”文件夹取回并放到正确位置。", "The authors don't allow third-party launchers to download these files. Click Download to open CurseForge; Kiln picks up the files from Downloads and moves them into place."), 12); explanation.Foreground = Ui.Brush("KilnTextSecondary");
        var list = new StackPanel { Spacing = 0 };
        foreach (var file in _remaining)
        {
            var labels = Ui.Col(Ui.Text(file.Title, 13, FontWeights.SemiBold), Ui.Text(file.FileName, 11.5)); labels.Spacing = 3;
            var row = Ui.RecordRow(labels, Ui.Button(T("下载", "Download"), async (_, _) => await Open(file.PageUrl)));
            var line = new Border { BorderBrush = Ui.Brush("KilnStroke"), BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
            _rows[file.Id] = line; list.Children.Add(line);
        }
        _openAll = Ui.Button(T("全部打开", "Open all"), async (_, _) => { foreach (var file in _remaining.ToArray()) await Open(file.PageUrl); });
        var actions = Ui.Row(_openAll); actions.Spacing = 8; Actions = actions;
        _status.Foreground = Ui.Brush("KilnTextSecondary");
        var body = Ui.Col(_heading, explanation, new ScrollViewer { Content = list, MaxHeight = 240, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, _status); body.Spacing = 12; Content = body;
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
        Render();
    }
    private static async Task Open(string address) { if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http") await Windows.System.Launcher.LaunchUriAsync(uri); }
    public Task RefreshAsync() => _refresh is { IsCompleted: false } current ? current : _refresh = RefreshCore();
    public void Start()
    {
        if (_polling is { IsCancellationRequested: false }) return;
        _stopped = false; var polling = new CancellationTokenSource(); _polling = polling;
        _ = Poll(polling);
    }
    private async Task Poll(CancellationTokenSource polling)
    {
        try
        {
            await RefreshAsync();
            while (!polling.IsCancellationRequested && _remaining.Count > 0)
            {
                // Match the original Swift task loop without depending on visual frame updates.
                await Task.Delay(TimeSpan.FromSeconds(2), polling.Token);
                await RefreshAsync();
            }
        }
        catch (OperationCanceledException) when (polling.IsCancellationRequested) { }
        finally { if (ReferenceEquals(_polling, polling)) _polling = null; polling.Dispose(); }
    }
    private async Task RefreshCore()
    {
        if (_stopped || _remaining.Count == 0) return;
        try
        {
            var remaining = await Task.Run(() => _collect(_remaining));
            if (_stopped) return;
            _remaining = remaining; Render(); Changed?.Invoke();
        }
        catch (Exception e) { if (!_stopped) _status.Text = e.Friendly(); }
    }
    private void Render()
    {
        _heading.Text = _remaining.Count == 0 ? T("全部文件已就位", "All files are in place") : T($"还有 {_remaining.Count} 个文件需要手动下载", $"{_remaining.Count} files need to be downloaded manually");
        var ids = _remaining.Select(f => f.Id).ToHashSet();
        foreach (var row in _rows) row.Value.Visibility = ids.Contains(row.Key) ? Visibility.Visible : Visibility.Collapsed;
        _openAll.Visibility = _remaining.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _status.Text = _remaining.Count == 0 ? T("全部文件已就位，可以启动游戏。", "All files are in place. You can start the game.") : T($"正在等待下载……（{_total - _remaining.Count}/{_total}）", $"Waiting for downloads… ({_total - _remaining.Count}/{_total})");
        if (_remaining.Count == 0) _polling?.Cancel();
    }
    public void Stop() { _stopped = true; _polling?.Cancel(); }
}
