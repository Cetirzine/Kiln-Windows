using Kiln.App.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;

namespace Kiln.App;

// A Kiln window drawn over the current XamlRoot. No Fluent dialog chrome.
public sealed class KilnDialog
{
    public XamlRoot? XamlRoot { get; set; }
    public object? Title { get; set; }
    public object? Content { get; set; }
    private string _primaryText = "", _secondaryText = "", _closeText = "";
    public string PrimaryButtonText { get => _primaryText; set { _primaryText = value; if (_primaryButton != null) _primaryButton.Content = value; } }
    public string SecondaryButtonText { get => _secondaryText; set { _secondaryText = value; if (_secondaryButton != null) _secondaryButton.Content = value; } }
    public string CloseButtonText { get => _closeText; set { _closeText = value; if (_closeButton != null) _closeButton.Content = value; } }
    public ContentDialogButton DefaultButton { get; set; }
    private bool _primaryEnabled = true;
    public bool IsPrimaryButtonEnabled { get => _primaryEnabled; set { _primaryEnabled = value; if (_primaryButton != null) _primaryButton.IsEnabled = value; } }
    private bool _secondaryEnabled = true;
    public bool IsSecondaryButtonEnabled { get => _secondaryEnabled; set { _secondaryEnabled = value; if (_secondaryButton != null) _secondaryButton.IsEnabled = value; } }
    public double PreferredWidth { get; set; }
    public UIElement? LeadingContent { get; set; }
    public bool SheetLayout { get; set; }
    public Control? InitialFocus { get; set; }
    public static bool IsOpen => _openCount > 0;
    private static int _openCount;
    private Grid? _overlay;
    private TaskCompletionSource<ContentDialogResult>? _completion;
    private FrameworkElement? _previousFocus;
    private Border? _bodyHost;
    private Button? _primaryButton;
    private Button? _secondaryButton;
    private Button? _closeButton;
    private bool _closing;
    private ContentDialogResult _result;
    public event Action? Closing;
    public async Task<ContentDialogResult> ShowAsync()
    {
        if (_completion != null) throw new InvalidOperationException("This dialog is already open.");
        var root = XamlRoot ?? App.MainAppWindow?.Content.XamlRoot ?? throw new InvalidOperationException("No active window.");
        var host = App.MainAppWindow?.Content is Panel windowPanel && windowPanel.XamlRoot == root ? windowPanel : root.Content as Panel ?? throw new InvalidOperationException("The dialog needs a panel at the window root.");
        _completion = new TaskCompletionSource<ContentDialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _closing = false; _result = ContentDialogResult.None;
        _previousFocus = FocusManager.GetFocusedElement(root) as FrameworkElement;
        var overlay = new Grid { Name = "KilnModalOverlay", Width = root.Size.Width, Height = root.Size.Height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(82, 0, 0, 0)) };
        Grid.SetColumnSpan(overlay, Math.Max(1, (host as Grid)?.ColumnDefinitions.Count ?? 1));
        Grid.SetRowSpan(overlay, Math.Max(1, (host as Grid)?.RowDefinitions.Count ?? 1));
        Canvas.SetZIndex(overlay, 10000);
        var width = PreferredWidth > 0 ? PreferredWidth : Content is string ? 440d : 560d;
        var surface = new Grid { Name = "KilnDialogSurface", Width = Math.Min(width, Math.Max(280, root.Size.Width - 64)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        surface.Children.Add(new Border { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(90, 0, 0, 0)), Translation = new System.Numerics.Vector3(6, 6, 0) });
        var panel = new StackPanel { Spacing = 0 };
        var title = Ui.Text(Title?.ToString(), 13, FontWeights.SemiBold); title.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White); title.TextTrimming = TextTrimming.CharacterEllipsis; title.MaxLines = 1;
        var header = new Border { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 29)), MinHeight = 30, Padding = new Thickness(12, 6, 12, 6), Child = title }; if (!SheetLayout) panel.Children.Add(header);
        UIElement body = Content is UIElement element ? element : Ui.Text(Content?.ToString(), 13);
        _bodyHost = new Border { Child = body };
        var scroll = new ScrollViewer { Content = _bodyHost, MaxHeight = Math.Max(120, root.Size.Height - 220), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var content = Ui.Col(); content.Spacing = SheetLayout ? 12 : 16; content.Padding = new Thickness(SheetLayout ? 22 : 20);
        if (SheetLayout && !string.IsNullOrEmpty(Title?.ToString())) content.Children.Add(Ui.Text(Title.ToString(), 17, FontWeights.Bold));
        content.Children.Add(scroll);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        if (!string.IsNullOrEmpty(CloseButtonText)) { _closeButton = Ui.Button(CloseButtonText, (_, _) => Finish(ContentDialogResult.None)); buttons.Children.Add(_closeButton); }
        if (!string.IsNullOrEmpty(SecondaryButtonText))
        {
            var secondary = Ui.Button(SecondaryButtonText, (_, _) => Finish(ContentDialogResult.Secondary)); secondary.IsEnabled = IsSecondaryButtonEnabled; buttons.Children.Add(secondary);
            _secondaryButton = secondary;
        }
        if (!string.IsNullOrEmpty(PrimaryButtonText))
        {
            var primary = Ui.Button(PrimaryButtonText, (_, _) => Finish(ContentDialogResult.Primary)); primary.IsEnabled = IsPrimaryButtonEnabled;
            primary.BorderBrush = Ui.Brush("KilnAccent"); primary.FontWeight = FontWeights.SemiBold; buttons.Children.Add(primary);
            if (SheetLayout) { primary.Background = Ui.Brush("KilnAccent"); primary.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White); }
            _primaryButton = primary;
        }
        var footer = new Grid { ColumnSpacing = 12 };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (LeadingContent != null) footer.Children.Add(new Border { Child = LeadingContent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(buttons, 1); footer.Children.Add(buttons);
        content.Children.Add(footer); panel.Children.Add(content);
        surface.Children.Add(new Border { Background = Ui.Brush("KilnPanel"), BorderBrush = Ui.Brush("KilnEdge"), BorderThickness = new Thickness(1.5), Child = panel }); overlay.Children.Add(surface);
        overlay.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Escape) { args.Handled = true; Finish(ContentDialogResult.None); }
            else if (args.Key == Windows.System.VirtualKey.Enter && DefaultButton == ContentDialogButton.Primary && IsPrimaryButtonEnabled && !string.IsNullOrEmpty(PrimaryButtonText)) { args.Handled = true; Finish(ContentDialogResult.Primary); }
        };
        _overlay = overlay;
        var completion = _completion;
        void Resize(XamlRoot sender, XamlRootChangedEventArgs args) { overlay.Width = root.Size.Width; overlay.Height = root.Size.Height; surface.Width = Math.Min(width, Math.Max(280, root.Size.Width - 64)); scroll.MaxHeight = Math.Max(120, root.Size.Height - 220); }
        root.Changed += Resize;
        var disabled = UnderlayControls(host).Where(c => c.IsEnabled).ToList();
        // Keep every dialog control in the window's visual tree: no detached Popup HWND.
        // Disable the underlying content rather than letting keyboard focus escape the modal.
        foreach (var control in disabled) control.IsEnabled = false;
        _openCount++;
        try
        {
            var focusTarget = InitialFocus ?? buttons.Children.OfType<Control>().FirstOrDefault();
            overlay.Loaded += (_, _) => { if (!_closing) focusTarget?.Focus(FocusState.Programmatic); };
            host.Children.Add(overlay);
            foreach (var child in buttons.Children) if (child is Control control) { control.Focus(FocusState.Programmatic); break; }
            return await completion.Task;
        }
        finally
        {
            root.Changed -= Resize;
            overlay.Visibility = Visibility.Collapsed; host.Children.Remove(overlay); _bodyHost.Child = null;
            if (LeadingContent != null && footer.Children.FirstOrDefault() is Border leading) leading.Child = null;
            foreach (var control in disabled) control.IsEnabled = true;
            _openCount--;
            _primaryButton = null; _secondaryButton = null; _closeButton = null; _bodyHost = null;
            _overlay = null; _completion = null;
            if (_previousFocus is Control { IsLoaded: true } previous && previous.XamlRoot == root) previous.Focus(FocusState.Programmatic);
            _previousFocus = null;
        }
    }
    public void Hide() => Finish(ContentDialogResult.None);
    private static IEnumerable<Control> UnderlayControls(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Control control) yield return control;
            else foreach (var descendant in UnderlayControls(child)) yield return descendant;
        }
    }
    private void Finish(ContentDialogResult result)
    {
        if (_completion == null || _overlay == null || _closing) return;
        _closing = true; _result = result; Closing?.Invoke();
        var overlay = _overlay; var completion = _completion;
        // The clicked button must finish dispatching its input event before its tree is closed.
        if (!overlay.DispatcherQueue.TryEnqueue(() => { overlay.Visibility = Visibility.Collapsed; completion.TrySetResult(result); })) completion.TrySetResult(result);
    }
}
