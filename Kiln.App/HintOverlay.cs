using Kiln.App.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace Kiln.App;

internal enum HintKind { Info, Success, Error }
internal static class Hint
{
    internal static void Show(string text, HintKind kind = HintKind.Info) => MainWindow.Current?.ShowHint(text, kind);
}

internal sealed class HintOverlay : Grid
{
    private sealed class Item(string text, HintKind kind, Border card, Border dot, Grid frame, double expires)
    {
        internal readonly string Text = text;
        internal readonly HintKind Kind = kind;
        internal readonly Border Card = card, Dot = dot;
        internal readonly Grid Frame = frame;
        internal readonly double Expires = expires;
        internal bool Hovering;
    }
    private readonly StackPanel _panel = new() { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(20) };
    private readonly List<Item> _items = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Func<double> _now;
    internal int Count => _items.Count;

    internal HintOverlay(Func<double>? clock = null)
    {
        _now = clock ?? (() => Environment.TickCount64 / 1000.0);
        Children.Add(_panel);
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => { if (_items.Count > 0) _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
        ActualThemeChanged += (_, _) => { foreach (var item in _items) SetColor(item); };
    }

    internal void Show(string text, HintKind kind)
    {
        var dot = new Border { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0) };
        var label = Ui.Text(text, 12.5); label.IsTextSelectionEnabled = true;
        var close = Ui.Button("×", (_, _) => { }); close.Style = (Style)Application.Current.Resources["KilnNavigationButton"]; close.Width = 18; close.Height = 18; close.MinHeight = 0; close.Padding = new Thickness(0); close.FontSize = 13; close.VerticalAlignment = VerticalAlignment.Top;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(close, Kiln.Core.Localization.T("关闭提示", "Dismiss notification"));
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        row.Children.Add(dot); Grid.SetColumn(label, 1); row.Children.Add(label); Grid.SetColumn(close, 2); row.Children.Add(close);
        var card = new Border { Name = "KilnHintCard", Width = 320, Padding = new Thickness(12, 9, 8, 9), Background = Ui.Brush("KilnPanel"), BorderBrush = Ui.Brush("KilnEdge"), BorderThickness = new Thickness(1.5), Child = row };
        var frame = new Grid { Margin = new Thickness(0, 0, 3, 3) };
        frame.Children.Add(new Border { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(56, 30, 30, 29)), Translation = new System.Numerics.Vector3(3, 3, 0) }); frame.Children.Add(card);
        var length = new System.Globalization.StringInfo(text).LengthInTextElements;
        var item = new Item(text, kind, card, dot, frame, _now() + Math.Max(kind == HintKind.Error ? 6 : 3.5, length * .09));
        close.Click += (_, _) => Dismiss(item);
        card.PointerEntered += (_, _) => item.Hovering = true;
        card.PointerExited += (_, _) => item.Hovering = false;
        _items.Add(item); SetColor(item);
        _panel.Children.Add(frame);
        card.Loaded += (_, _) => { if (_items.Contains(item)) Animate(frame, true); };
        while (_items.Count > 4) Dismiss(_items[0]);
        _timer.Start();
    }

    private void SetColor(Item item)
    {
        var dark = ActualTheme == ElementTheme.Dark;
        var color = item.Kind switch { HintKind.Success => dark ? 0x6CBF5Au : 0x2F7A24u, HintKind.Error => dark ? 0xF07167u : 0xB8322Au, _ => 0u };
        item.Dot.Background = item.Kind == HintKind.Info ? Ui.Brush("KilnTextSecondary") : new SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(color >> 16), (byte)(color >> 8), (byte)color));
    }

    internal void Tick()
    {
        foreach (var item in _items.Where(i => _now() >= i.Expires && (!i.Hovering || KilnDialog.IsOpen)).ToArray()) Dismiss(item);
        if (_items.Count == 0) _timer.Stop();
    }
    internal void SetHoverForTest(bool hovering) { foreach (var item in _items) item.Hovering = hovering; }
    private async void Dismiss(Item item)
    {
        if (!_items.Remove(item)) return;
        var frame = item.Frame;
        if (!frame.IsLoaded) { _panel.Children.Remove(frame); return; }
        Animate(frame, false); await Task.Delay(250); _panel.Children.Remove(frame);
    }
    private static void Animate(UIElement element, bool entering)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element); var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, entering ? 0 : 1); animation.InsertKeyFrame(1, entering ? 1 : 0); animation.Duration = TimeSpan.FromMilliseconds(250);
        visual.StartAnimation("Opacity", animation);
        var offset = visual.Compositor.CreateVector3KeyFrameAnimation(); offset.InsertKeyFrame(0, new System.Numerics.Vector3(0, entering ? 8 : 0, 0)); offset.InsertKeyFrame(1, new System.Numerics.Vector3(0, entering ? 0 : 8, 0)); offset.Duration = animation.Duration;
        visual.StartAnimation("Translation", offset);
    }
}
