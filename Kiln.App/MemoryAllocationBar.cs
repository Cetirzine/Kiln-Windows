using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App;

internal sealed class MemoryAllocationBar : UserControl
{
    private readonly double _total;
    private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, Background = Pages.Ui.Brush("KilnTextSecondary") };
    private readonly Grid _track = new() { Name = "MemoryAllocationTrack", Height = 8, Background = Pages.Ui.Brush("KilnPanelSecondary") };
    private readonly TextBlock _caption = Pages.Ui.Text("", 11);
    internal double Fraction { get; private set; }
    internal MemoryAllocationBar(double total)
    {
        _total = total; Name = "MemoryAllocation"; _track.Children.Add(_fill); _caption.Foreground = Pages.Ui.Brush("KilnTextSecondary");
        var root = Pages.Ui.Col(_track, _caption); root.Spacing = 6; Content = root;
        _track.SizeChanged += (_, _) => _fill.Width = _track.ActualWidth * Fraction;
        Update(null);
    }
    internal void Update(double? allocated)
    {
        Fraction = _total <= 0 ? 0 : Math.Clamp((allocated ?? _total * .25) / _total, 0, 1);
        _fill.Width = _track.ActualWidth * Fraction;
        var amount = allocated is { } value ? $"{value:0.0} GB" : Localization.T("自动", "Automatic");
        _caption.Text = _total <= 0 ? Localization.T("无法获取物理内存容量", "Physical memory size is unavailable") : Localization.T($"物理内存 {_total:0} GB · 游戏分配：{amount}", $"Physical memory {_total:0} GB · Game: {amount}");
    }
}
