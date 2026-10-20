using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

/// 构建界面用的工具
internal static class Ui
{
    public static TextBlock Text(string? text, double? size = null, Windows.UI.Text.FontWeight? weight = null)
    {
        var t = new TextBlock { Text = text ?? "", TextWrapping = TextWrapping.Wrap };
        if (size != null) t.FontSize = size.Value;
        if (weight != null) t.FontWeight = weight.Value;
        return t;
    }

    public static Button Button(string content, RoutedEventHandler onClick)
    {
        var b = new Button { Content = content };
        b.Click += onClick;
        return b;
    }

    public static StackPanel Row(params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var c in children) p.Children.Add(c);
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
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = Text(label, 14);
        text.VerticalAlignment = VerticalAlignment.Center;
        if (tooltip != null) ToolTipService.SetToolTip(text, tooltip);
        Grid.SetColumn(text, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        grid.Margin = new Thickness(0, 8, 0, 8);
        return grid;
    }

    public static ComboBox Combo(IEnumerable<string> items, int selected, Action<int> onChange)
    {
        var combo = new ComboBox { MinWidth = 220 };
        foreach (var i in items) combo.Items.Add(i);
        combo.SelectedIndex = selected;
        combo.SelectionChanged += (_, _) => onChange(combo.SelectedIndex);
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
        var n = new NumberBox { Value = value, Minimum = min, Maximum = max, SmallChange = step, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, MinWidth = 160 };
        n.ValueChanged += (_, _) => onChange(n.Value);
        return n;
    }

    public static async Task<(bool Ok, string Value)> Prompt(XamlRoot root, string title, string placeholder, string initial = "")
    {
        var input = new TextBox { Text = initial, PlaceholderText = placeholder };
        var dialog = new ContentDialog
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
        var dialog = new ContentDialog
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
}


