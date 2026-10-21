using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

/// 构建界面用的工具
internal static class Ui
{
    private static readonly Dictionary<string, Microsoft.UI.Xaml.Media.SolidColorBrush> Palette = new();
    private static ElementTheme _theme = ElementTheme.Default;
    private static Microsoft.UI.Xaml.Media.SolidColorBrush PaletteSource(string key)
    {
        var dark = _theme == ElementTheme.Dark || _theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark;
        return (Microsoft.UI.Xaml.Media.SolidColorBrush)((ResourceDictionary)Application.Current.Resources.ThemeDictionaries[dark ? "Default" : "Light"])[key];
    }
    public static Microsoft.UI.Xaml.Media.Brush Brush(string key)
    {
        if (!Palette.TryGetValue(key, out var brush)) Palette[key] = brush = new(PaletteSource(key).Color);
        return brush;
    }
    internal static void ApplyTheme(ElementTheme theme)
    {
        _theme = theme;
        // Code-built pages share mutable brushes so a system theme change updates existing views.
        foreach (var (key, brush) in Palette) brush.Color = PaletteSource(key).Color;
    }
    public static TextBlock Text(string? text, double? size = null, Windows.UI.Text.FontWeight? weight = null)
    {
        var t = new TextBlock { Text = text ?? "", FontSize = size ?? 13, TextWrapping = TextWrapping.Wrap, Foreground = Brush("KilnText") };
        if (size != null) t.FontSize = size.Value;
        if (weight != null) t.FontWeight = weight.Value;
        return t;
    }

    public static Button Button(string content, RoutedEventHandler onClick)
    {
        // 统一按钮尺寸与对齐（参考原启动器的按钮风格）
        var b = new Button
        {
            Content = content,
            MinHeight = 28,
            Padding = new Thickness(12, 3, 12, 3),
            VerticalAlignment = VerticalAlignment.Center,
        };
        b.Click += onClick;
        return b;
    }

    public static FlowRow Row(params UIElement[] children)
    {
        var p = new FlowRow { Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in children)
        {
            if (c is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
            p.Children.Add(c);
        }
        return p;
    }

    public static StackPanel Col(params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    /// 一行设置项：名称在左，控件在右
    public static Grid Setting(string label, FrameworkElement control, string? tooltip = null)
    {
        var grid = new Grid { Name = "SettingRow" };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var text = Text(label, 13);
        var labels = Col(text); labels.Spacing = 3; labels.Padding = new Thickness(0, 5, 0, 0);
        if (!string.IsNullOrWhiteSpace(tooltip)) { var help = Text(tooltip, 11.5); help.Foreground = Brush("KilnTextSecondary"); labels.Children.Add(help); }
        Grid.SetColumn(labels, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(labels);
        grid.Children.Add(control);
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Top;
        if (control is ComboBox or TextBox or PasswordBox or NumberBox or Microsoft.UI.Xaml.Controls.Button)
        {
            control.Width = 320;
            control.MinWidth = 0;
            control.MaxWidth = 320;
        }
        grid.ColumnSpacing = 16;
        grid.Margin = new Thickness(0, 2, 0, 2);
        return grid;
    }

    public static ComboBox Combo(IEnumerable<string> items, int selected, Action<int> onChange)
    {
        var combo = new ComboBox { MinWidth = 220 };
        foreach (var i in items) combo.Items.Add(i);
        combo.SelectedIndex = selected;
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) onChange(combo.SelectedIndex); };
        return combo;
    }

    public static ToggleSwitch Toggle(bool value, string on, string off, Action<bool> onChange)
    {
        var t = new ToggleSwitch { IsOn = value, OnContent = on, OffContent = off };
        t.Toggled += (_, _) => onChange(t.IsOn);
        return t;
    }

    public static TextBox Box(string? text, string placeholder, Action<string> onChange)
    {
        var b = new TextBox { Text = text ?? "", PlaceholderText = placeholder, MinWidth = 300 };
        b.LostFocus += (_, _) => onChange(b.Text);
        return b;
    }

    public static NumberBox Num(double value, double min, double max, double step, Action<double> onChange)
    {
        var n = new NumberBox { Value = value, Minimum = min, Maximum = max, SmallChange = step, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, MinWidth = 160 };
        n.ValueChanged += (_, _) => { if (double.IsFinite(n.Value)) onChange(n.Value); };
        return n;
    }

    public static async Task<(bool Ok, string Value)> Prompt(XamlRoot root, string title, string placeholder, string initial = "")
    {
        var input = new TextBox { Text = initial, PlaceholderText = placeholder };
        var dialog = new KilnDialog
        {
            XamlRoot = root,
            Title = title,
            Content = input,
            PrimaryButtonText = Localization.T("确定", "OK"),
            CloseButtonText = Localization.T("取消", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        var r = await dialog.ShowAsync();
        return (r == ContentDialogResult.Primary, input.Text.Trim());
    }

    public static async Task ShowError(XamlRoot root, Exception e) =>
        await ShowError(root, e.Friendly());

    public static async Task ShowError(XamlRoot root, string message)
    {
        var dialog = new KilnDialog
        {
            XamlRoot = root,
            Title = Localization.T("出错了", "Error"),
            Content = message,
            CloseButtonText = Localization.T("好", "OK"),
        };
        await dialog.ShowAsync();
    }

    public static ProgressBar Progress(double value = 0, double max = 1)
    {
        var p = new ProgressBar { Value = value, Maximum = max };
        return p;
    }

    public static FrameworkElement Card(string title, params UIElement[] children) => CardWithActions(title, Array.Empty<Button>(), children);

    public static FrameworkElement CardWithActions(string title, IReadOnlyList<Button> actions, params UIElement[] children)
    {
        var body = Col(children); body.Spacing = 12; body.Padding = new Thickness(16);
        var titleText = Text(title, 12.5, FontWeights.SemiBold);
        titleText.Foreground = Brush("KilnTitleText");
        titleText.VerticalAlignment = VerticalAlignment.Center;
        titleText.TextWrapping = TextWrapping.NoWrap; titleText.TextTrimming = TextTrimming.CharacterEllipsis;
        var headerGrid = new Grid { ColumnSpacing = 12 };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.Children.Add(titleText);
        var trailing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, RequestedTheme = ElementTheme.Dark };
        foreach (var action in actions) trailing.Children.Add(action);
        Grid.SetColumn(trailing, 1); headerGrid.Children.Add(trailing);
        var header = new Border { Background = Brush("KilnTitleBar"), MinHeight = 30, Padding = new Thickness(12, actions.Count == 0 ? 0 : 4, 6, actions.Count == 0 ? 0 : 4), Child = headerGrid };
        if (title.Length == 0) header.Visibility = Visibility.Collapsed;
        var stack = Col(header, body); stack.Spacing = 0;
        var surface = new Border { Background = Brush("KilnPanel"), BorderBrush = Brush("KilnEdge"), BorderThickness = new Thickness(1.5), Child = stack };
        var root = new Grid { Margin = new Thickness(0, 0, 3, 3) };
        root.Children.Add(new Border { Background = Brush("KilnHardShadow"), Translation = new System.Numerics.Vector3(3, 3, 0) });
        root.Children.Add(surface); return root;
    }

    private static readonly Dictionary<string, int> TabSelections = new();
    public static void SelectTab(string key, int index) => TabSelections[key] = index;
    public static Grid Tabs(string title, params (string Label, Func<UIElement> Build)[] tabs) => Tabs(title, title, tabs);
    public static Grid Tabs(string key, string title, params (string Label, Func<UIElement> Build)[] tabs)
    {
        var root = new Grid { RowSpacing = 14 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(Header(title));
        var bar = new FlowRow { Spacing = 22 };
        var content = new Border(); Grid.SetRow(content, 2); root.Children.Add(content);
        var buttons = new List<Button>();
        void Select(int index)
        {
            TabSelections[key] = index;
            for (var i = 0; i < buttons.Count; i++)
            {
                buttons[i].BorderThickness = new Thickness(0, 0, 0, i == index ? 3 : 0);
                buttons[i].FontWeight = i == index ? FontWeights.SemiBold : FontWeights.Normal;
            }
            content.Child = tabs[index].Build();
        }
        for (var i = 0; i < tabs.Length; i++)
        {
            var index = i;
            var b = Button(tabs[i].Label, (_, _) => Select(index));
            b.Style = (Style)Application.Current.Resources["KilnNavigationButton"];
            b.Padding = new Thickness(0, 2, 0, 9); b.BorderBrush = Brush("KilnAccent");
            buttons.Add(b); bar.Children.Add(b);
        }
        Grid.SetRow(bar, 1); root.Children.Add(bar); Select(Math.Clamp(TabSelections.GetValueOrDefault(key), 0, tabs.Length - 1)); return root;
    }

    public static ScrollViewer Scroll(params UIElement[] children)
    {
        var content = Col(children); content.Spacing = 14;
        return new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }

    public static Grid RecordRow(UIElement label, params Button[] actions)
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 7, 10, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(label);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var action in actions) { action.Width = 76; action.MinWidth = 0; buttons.Children.Add(action); }
        Grid.SetColumn(buttons, 1); grid.Children.Add(buttons); return grid;
    }

    public static AccountChip AccountButton(bool onImage = false) => new(onImage);

    public static Grid Header(string title, params Button[] actions)
    {
        var root = new Grid { ColumnSpacing = 12 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = Text(title, 21, FontWeights.Bold); text.MaxLines = 1; text.TextTrimming = TextTrimming.CharacterEllipsis; root.Children.Add(text);
        var controls = new StackPanel { Name = "PageHeaderActions", Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var action in actions) controls.Children.Add(action);
        controls.Children.Add(AccountButton()); Grid.SetColumn(controls, 1); root.Children.Add(controls); return root;
    }

    public static FrameworkElement Segmented(ComboBox selection, double? segmentWidth = 88)
    {
        var grid = new Grid { Padding = new Thickness(2), Height = 28, Background = Brush("KilnPanelSecondary"), HorizontalAlignment = HorizontalAlignment.Left };
        var buttons = new List<Button>(); var dividers = new List<Border>();
        for (var i = 0; i < selection.Items.Count; i++)
        {
            if (i > 0)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
                var divider = new Border { Width = 1, Height = 14, VerticalAlignment = VerticalAlignment.Center, Background = Brush("KilnStroke") }; Grid.SetColumn(divider, grid.ColumnDefinitions.Count - 1); grid.Children.Add(divider); dividers.Add(divider);
            }
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = segmentWidth is double width ? new GridLength(width) : new GridLength(1, GridUnitType.Star) });
            var index = i;
            var button = Button(selection.Items[i]?.ToString() ?? "", (_, _) => selection.SelectedIndex = index);
            button.Style = (Style)Application.Current.Resources["KilnNavigationButton"]; button.Padding = new Thickness(4, 0, 4, 0); button.MinHeight = 0; button.FontSize = 12;
            button.HorizontalAlignment = HorizontalAlignment.Stretch; button.VerticalAlignment = VerticalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Center; button.VerticalContentAlignment = VerticalAlignment.Center;
            button.BorderThickness = new Thickness(0); button.Margin = new Thickness(0);
            button.MinWidth = 0;
            Grid.SetColumn(button, grid.ColumnDefinitions.Count - 1); grid.Children.Add(button); buttons.Add(button);
        }
        void Update()
        {
            for (var i = 0; i < buttons.Count; i++)
            {
                var selected = i == selection.SelectedIndex;
                buttons[i].Background = selected ? Brush("KilnTitleBar") : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                buttons[i].Foreground = selected ? Brush("KilnTitleText") : Brush("KilnTextSecondary");
                buttons[i].FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
                if (i > 0) dividers[i - 1].Opacity = i == selection.SelectedIndex || i - 1 == selection.SelectedIndex ? 0 : 1;
            }
        }
        selection.SelectionChanged += (_, _) => Update(); Update();
        grid.HorizontalAlignment = segmentWidth == null ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        var surface = new Grid { Height = 28, HorizontalAlignment = segmentWidth == null ? HorizontalAlignment.Stretch : HorizontalAlignment.Left };
        surface.Children.Add(grid); surface.Children.Add(new Border { BorderBrush = Brush("KilnStroke"), BorderThickness = new Thickness(1), IsHitTestVisible = false }); return surface;
    }

    public static Border SearchField(TextBox input)
    {
        var placeholderText = input.PlaceholderText;
        input.PlaceholderText = "";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input, placeholderText);
        input.Width = double.NaN; input.MinWidth = 0; input.MinHeight = 0; input.Height = double.NaN; input.Padding = new Thickness(0); input.BorderThickness = new Thickness(0); input.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent); input.FontSize = 12.5;
        input.Template = (ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TextBox'><Grid Background='{TemplateBinding Background}'><ScrollViewer x:Name='ContentElement' Padding='0' Margin='0' VerticalAlignment='Center' HorizontalScrollMode='Enabled' HorizontalScrollBarVisibility='Hidden' VerticalScrollMode='Disabled' VerticalScrollBarVisibility='Disabled' IsTabStop='False' ZoomMode='Disabled'/></Grid></ControlTemplate>");
        input.VerticalContentAlignment = VerticalAlignment.Center;
        input.VerticalAlignment = VerticalAlignment.Stretch;
        var grid = new Grid { ColumnSpacing = 6, Padding = new Thickness(9, 0, 9, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new FontIcon { Glyph = "\uE721", FontSize = 11.5, Foreground = Brush("KilnTextSecondary"), VerticalAlignment = VerticalAlignment.Center }); Grid.SetColumn(input, 1); grid.Children.Add(input);
        var placeholder = Text(placeholderText, 12.5); placeholder.Foreground = Brush("KilnTextSecondary"); placeholder.VerticalAlignment = VerticalAlignment.Center; placeholder.IsHitTestVisible = false; placeholder.TextWrapping = TextWrapping.NoWrap;
        Grid.SetColumn(placeholder, 1); grid.Children.Add(placeholder);
        var clear = Button("×", (_, _) => input.Text = ""); clear.Style = (Style)Application.Current.Resources["KilnNavigationButton"]; clear.Padding = new Thickness(0); clear.MinHeight = 0; clear.Width = 16; clear.FontSize = 12;
        Grid.SetColumn(clear, 2); grid.Children.Add(clear); clear.Visibility = input.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        void Update() { clear.Visibility = input.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible; placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; }
        input.TextChanged += (_, _) => Update(); Update();
        return new Border { Child = grid, Background = Brush("KilnPanel"), BorderBrush = Brush("KilnEdge"), BorderThickness = new Thickness(1), Height = 28 };
    }
}

/// Wrap action rows within the viewport instead of clipping a horizontal StackPanel.
public sealed class FlowRow : Panel
{
    public double Spacing { get; set; } = 12;
    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size available)
    {
        double x = 0, y = 0, height = 0, width = 0;
        foreach (var child in Children)
        {
            child.Measure(new Windows.Foundation.Size(available.Width, available.Height));
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > available.Width) { y += height + Spacing; x = 0; height = 0; }
            width = Math.Max(width, x + size.Width); x += size.Width + Spacing; height = Math.Max(height, size.Height);
        }
        return new Windows.Foundation.Size(width, y + height);
    }
    protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size finalSize)
    {
        double x = 0, y = 0, height = 0;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > finalSize.Width) { y += height + Spacing; x = 0; height = 0; }
            child.Arrange(new Windows.Foundation.Rect(x, y, size.Width, size.Height));
            x += size.Width + Spacing; height = Math.Max(height, size.Height);
        }
        return finalSize;
    }
}


