using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Kiln.App.Pages;

public sealed class GameLogPage : Page
{
    private readonly GameProcess _game;
    private readonly ListView _output = new() { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = false, Padding = new Thickness(14), Background = Solid(0x161615) };
    private readonly TextBox _search = new() { PlaceholderText = Localization.T("筛选日志", "Filter log"), Width = 220 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private long _revision = -1;
    private long _nextLine;
    private string? _lastFilter;
    private readonly System.Collections.ObjectModel.ObservableCollection<LogLine> _lines = new();
    private readonly CheckBox _follow = new() { Content = Localization.T("自动滚动", "Auto scroll"), IsChecked = true };
    private readonly Button _stop;

    public GameLogPage(GameProcess game)
    {
        _game = game;
        var root = new Grid { RowSpacing = 12 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = Ui.Header(Localization.T("游戏日志", "Game log"), Ui.Button(Localization.T("返回", "Back"), (_, _) => MainWindow.Current!.NavigateTo("home")));
        root.Children.Add(header);
        _output.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><TextBlock Text='{Binding Text}' Foreground='{Binding Foreground}' FontFamily='Consolas' FontSize='11.5' IsTextSelectionEnabled='True' TextWrapping='NoWrap' /></DataTemplate>");
        var itemStyle = new Style(typeof(ListViewItem));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        itemStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0d));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        _output.ItemContainerStyle = itemStyle;
        _output.ItemsSource = _lines;
        ScrollViewer.SetHorizontalScrollBarVisibility(_output, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollMode(_output, ScrollMode.Enabled);
        _stop = Ui.Button(Localization.T("结束游戏", "Stop game"), (_, _) => game.Kill());
        var copy = Ui.Button(Localization.T("复制", "Copy"), (_, _) =>
        {
            var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(Diagnostics.Scrub(string.Join(Environment.NewLine, game.Lines), game.Secret));
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
        });
        var open = Ui.Button(Localization.T("打开日志文件", "Open log file"), async (_, _) =>
        {
            try { if (game.LogFilePath is { } path && File.Exists(path)) await Windows.System.Launcher.LaunchFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(path)); }
            catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
        }); open.IsEnabled = game.LogFilePath != null;
        _follow.Checked += (_, _) => ScrollToEnd();
        var actions = Ui.Row(Ui.SearchField(_search), _follow, copy, open, _stop,
            Ui.Button(Localization.T("分析崩溃", "Analyze crash"), async (_, _) =>
            {
                var finding = CrashAnalyzer.Analyze(game.Lines, game.Instance?.GameDir ?? "");
                await App.ShowInfo(App.MainAppWindow!, finding == null ? Localization.T("没有发现已知崩溃原因。", "No known crash cause found.") : finding.Reason + "\n" + finding.Detail);
            }),
            Ui.Button(Localization.T("导出诊断包…", "Export diagnostics…"), async (_, _) => await Export()));
        Grid.SetRow(actions, 1); root.Children.Add(actions);
        Grid.SetRow(_output, 2); root.Children.Add(_output);
        _search.TextChanged += (_, _) => { _revision = -1; Refresh(); };
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
        Content = root;
    }

    private void Refresh()
    {
        _stop.Visibility = _game.State is GameProcess.GameState.Starting or GameProcess.GameState.Running ? Visibility.Visible : Visibility.Collapsed;
        var revision = _game.LineRevision;
        if (_revision == revision) return;
        _revision = revision;
        var snapshot = _game.LogSnapshot();
        _revision = snapshot.Revision;
        var first = snapshot.TotalLines - snapshot.Lines.Count;
        if (_lastFilter != _search.Text) { _lines.Clear(); _nextLine = first; _lastFilter = _search.Text; }
        while (_lines.Count > 0 && _lines[0].Sequence < first) _lines.RemoveAt(0);
        for (var sequence = Math.Max(_nextLine, first); sequence < snapshot.TotalLines; sequence++)
        {
            var line = snapshot.Lines[(int)(sequence - first)];
            if (line.Contains(_search.Text, StringComparison.OrdinalIgnoreCase)) _lines.Add(new LogLine(sequence, Diagnostics.Scrub(line, _game.Secret)));
        }
        _nextLine = snapshot.TotalLines;
        if (_follow.IsChecked == true) ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        if (_output.Items.Count > 0) _output.ScrollIntoView(_output.Items[^1]);
    }

    private static SolidColorBrush Solid(uint rgb) => new(Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    [Microsoft.UI.Xaml.Data.Bindable]
    public sealed class LogLine
    {
        public string Text { get; }
        public long Sequence { get; }
        public Brush Foreground { get; }
        public LogLine(long sequence, string text)
        {
            Sequence = sequence; Text = text;
            Foreground = Solid(text.Contains("ERROR") || text.Contains("Exception") || text.Contains("FATAL") ? 0xff7b7bu : text.Contains("WARN") ? 0xf5c04fu : 0xd6dbe6u);
        }
    }

    private async Task Export()
    {
        if (_game.Instance == null) return;
        var picker = new KilnFileSavePicker();
        
        picker.SuggestedFileName = _game.InstanceName + "-diagnostics";
        picker.FileTypeChoices.Add("ZIP", new List<string> { ".zip" });
        var file = await picker.PickSaveFileAsync();
        if (file == null) return;
        try { await Diagnostics.Export(_game.Instance, _game, file.Path); }
        catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
    }
}
