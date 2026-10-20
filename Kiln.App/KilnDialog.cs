using Kiln.App.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;

namespace Kiln.App;

// A Kiln window drawn over the current XamlRoot. No Fluent dialog chrome.
public sealed class KilnDialog : ContentDialog
{
    public double PreferredWidth { get; set; }
    private Popup? _popup;
    private TaskCompletionSource<ContentDialogResult>? _completion;
    private FrameworkElement? _previousFocus;
    private Border? _bodyHost;
    private long? _primaryEnabledToken;
    public new async Task<ContentDialogResult> ShowAsync()
    {
        if (_completion != null) throw new InvalidOperationException("This dialog is already open.");
        var root = XamlRoot ?? App.MainAppWindow?.Content.XamlRoot ?? throw new InvalidOperationException("No active window.");
        _completion = new TaskCompletionSource<ContentDialogResult>();
        _previousFocus = FocusManager.GetFocusedElement(root) as FrameworkElement;
        var overlay = new Grid { Width = root.Size.Width, Height = root.Size.Height, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(82, 0, 0, 0)) };
        var width = PreferredWidth > 0 ? PreferredWidth : Content is string ? 440d : 560d;
        var surface = new Grid { Width = Math.Min(width, Math.Max(280, root.Size.Width - 64)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        surface.Children.Add(new Border { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(90, 0, 0, 0)), Translation = new System.Numerics.Vector3(6, 6, 0) });
        var panel = new StackPanel { Spacing = 0 };
        var title = Ui.Text(Title?.ToString(), 13, FontWeights.SemiBold); title.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White); title.TextTrimming = TextTrimming.CharacterEllipsis; title.MaxLines = 1;
        var header = new Border { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 29)), MinHeight = 30, Padding = new Thickness(12, 6, 12, 6), Child = title }; panel.Children.Add(header);
        UIElement body = Content is UIElement element ? element : Ui.Text(Content?.ToString(), 13);
        _bodyHost = new Border { Child = body };
        var scroll = new ScrollViewer { Content = _bodyHost, MaxHeight = Math.Max(120, root.Size.Height - 220), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var content = Ui.Col(scroll); content.Spacing = 16; content.Padding = new Thickness(20);
        var buttons = new FlowRow { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        if (!string.IsNullOrEmpty(CloseButtonText)) buttons.Children.Add(Ui.Button(CloseButtonText, (_, _) => Finish(ContentDialogResult.None)));
        if (!string.IsNullOrEmpty(SecondaryButtonText))
        {
            var secondary = Ui.Button(SecondaryButtonText, (_, _) => Finish(ContentDialogResult.Secondary)); secondary.IsEnabled = IsSecondaryButtonEnabled; buttons.Children.Add(secondary);
        }
        if (!string.IsNullOrEmpty(PrimaryButtonText))
        {
            var primary = Ui.Button(PrimaryButtonText, (_, _) => Finish(ContentDialogResult.Primary)); primary.IsEnabled = IsPrimaryButtonEnabled;
            primary.BorderBrush = Ui.Brush("KilnAccent"); primary.FontWeight = FontWeights.SemiBold; buttons.Children.Add(primary);
            _primaryEnabledToken = RegisterPropertyChangedCallback(IsPrimaryButtonEnabledProperty, (_, _) => primary.IsEnabled = IsPrimaryButtonEnabled);
        }
        content.Children.Add(buttons); panel.Children.Add(content);
        surface.Children.Add(new Border { Background = Ui.Brush("KilnPanel"), BorderBrush = Ui.Brush("KilnEdge"), BorderThickness = new Thickness(1.5), Child = panel }); overlay.Children.Add(surface);
        overlay.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Escape) { args.Handled = true; Finish(ContentDialogResult.None); }
            else if (args.Key == Windows.System.VirtualKey.Enter && DefaultButton == ContentDialogButton.Primary && IsPrimaryButtonEnabled && !string.IsNullOrEmpty(PrimaryButtonText)) { args.Handled = true; Finish(ContentDialogResult.Primary); }
        };
        _popup = new Popup { XamlRoot = root, Child = overlay, IsLightDismissEnabled = false, ShouldConstrainToRootBounds = true };
        _popup.IsOpen = true;
        foreach (var child in buttons.Children) if (child is Control control) { control.Focus(FocusState.Programmatic); break; }
        var completion = _completion;
        try { return await completion.Task; }
        finally
        {
            _popup.IsOpen = false; _popup.Child = null; _bodyHost.Child = null;
            if (_primaryEnabledToken is { } token) UnregisterPropertyChangedCallback(IsPrimaryButtonEnabledProperty, token);
            _primaryEnabledToken = null;
            _popup = null; _completion = null;
            if (_previousFocus is Control previous) previous.Focus(FocusState.Programmatic);
        }
    }
    public new void Hide() => Finish(ContentDialogResult.None);
    private void Finish(ContentDialogResult result) => _completion?.TrySetResult(result);
}
