using Kiln.App.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Kiln.App;

internal static class ControlUiChecks
{
    internal static async Task Run(MainWindow window, Action<string, bool> check)
    {
        var field = new TextBox { Width = 320, PlaceholderText = "搜索 / Search" };
        var password = new PasswordBox { Width = 320, PlaceholderText = "API key" };
        var combo = Ui.Combo(new[] { "第一项 / First", "第二项 / Second", "第三项 / Third" }, 0, _ => { }); combo.Width = 320;
        var stored = 2d; var number = Ui.Num(stored, 1, 3, .5, value => stored = value); number.Width = 320;
        var slider = new Slider { Width = 320, Minimum = 1, Maximum = 10, Value = 2, StepFrequency = .5 };
        var content = Ui.Col(field, password, combo, number, slider); content.HorizontalAlignment = HorizontalAlignment.Left;
        var fixture = new Border { Padding = new Thickness(28, 48, 28, 24), Child = content };
        window.ShowDiagnostics(fixture); await Layout();
        T Part<T>(DependencyObject parent, string name) where T : FrameworkElement => SettingsUiChecks.Tree(parent).OfType<T>().Single(e => e.Name == name);
        double Center(FrameworkElement child, FrameworkElement parent) => child.TransformToVisual(parent).TransformPoint(new Windows.Foundation.Point(0, child.ActualHeight / 2)).Y;
        if (Math.Abs(field.ActualHeight - 28) > .5 || Math.Abs(password.ActualHeight - 28) > .5) throw new InvalidOperationException($"Field heights: text {field.ActualHeight} minimum {field.MinHeight}, password {password.ActualHeight} minimum {password.MinHeight}; " + string.Join("; ", SettingsUiChecks.Tree(field).OfType<FrameworkElement>().Select(e => $"{e.Name}/{e.GetType().Name} {e.ActualHeight} desired {e.DesiredSize.Height} min {e.MinHeight}")));
        check("Custom text/password fields keep placeholders centered in 28-point surfaces", Math.Abs(Center(Part<TextBlock>(field, "PlaceholderTextContentPresenter"), field) - 14) < 1 && Math.Abs(Center(Part<TextBlock>(password, "PlaceholderTextContentPresenter"), password) - 14) < 1);
        field.Header = "名称 / Name"; field.Description = "说明 / Description"; password.Header = "密钥 / Key"; await Layout();
        check("Custom fields preserve visible headers and descriptions", Part<ContentPresenter>(field, "HeaderContentPresenter").Visibility == Visibility.Visible && Part<ContentPresenter>(field, "DescriptionPresenter").Visibility == Visibility.Visible && Part<ContentPresenter>(password, "HeaderContentPresenter").Visibility == Visibility.Visible);
        field.Header = null; field.Description = null; password.Header = null;
        field.Text = "Select this text"; field.Select(0, 6); field.IsReadOnly = true;
        var menu = (MenuFlyout)field.ContextFlyout; menu.ShowAt(field); await Layout();
        var items = menu.Items.OfType<MenuFlyoutItem>().ToArray();
        check("Custom read-only editing menu keeps Copy and Select All, disabling mutations", items.Length == 6 && !items[0].IsEnabled && !items[2].IsEnabled && items[3].IsEnabled && !items[4].IsEnabled && items[5].IsEnabled && field.SelectionFlyout == null);
        ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(items[5]).GetPattern(PatternInterface.Invoke)).Invoke(); await Layout();
        check("Select All from the custom menu uses the native text selection", field.SelectionLength == field.Text.Length);
        menu.Hide(); field.IsReadOnly = false;
        check("Password editing menu exposes Paste only", password.ContextFlyout is MenuFlyout secure && secure.Items.OfType<MenuFlyoutItem>().Count() == 1);
        for (var i = 0; i < 12; i++)
        {
            combo.IsDropDownOpen = true; await Layout();
            var container = combo.ContainerFromIndex((i + 1) % 3) as ComboBoxItem ?? throw new InvalidOperationException("The dropdown did not generate its item containers");
            container.IsSelected = true;
            combo.IsDropDownOpen = false; await Layout();
            if (combo.SelectedIndex != (i + 1) % 3) throw new InvalidOperationException("Combo selection was not committed");
        }
        check("Custom dropdown selects and reopens repeatedly without orphan popups", VisualTreeHelper.GetOpenPopupsForXamlRoot(fixture.XamlRoot).Count == 0);
        var up = Part<RepeatButton>(number, "UpSpinButton"); var down = Part<RepeatButton>(number, "DownSpinButton");
        ((IInvokeProvider)new RepeatButtonAutomationPeer(up).GetPattern(PatternInterface.Invoke)).Invoke(); await Layout();
        check("Custom numeric spinner increments and updates the bound setting", number.Value == 2.5 && stored == 2.5);
        number.Value = 3; await Layout(); check("Numeric spinner disables increment at the maximum", !up.IsEnabled && down.IsEnabled);
        number.Value = 1; await Layout(); check("Numeric spinner disables decrement at the minimum", up.IsEnabled && !down.IsEnabled);
        number.Value = double.NaN; check("Clearing a numeric field does not save an invalid value", stored == 1);
        var thumb = Part<Thumb>(slider, "HorizontalThumb"); var before = SettingsUiChecks.Left(thumb, slider);
        ((IRangeValueProvider)new SliderAutomationPeer(slider).GetPattern(PatternInterface.RangeValue)).SetValue(8); await Layout();
        check("Custom slider moves its thumb when its accessible value changes", slider.Value == 8 && SettingsUiChecks.Left(thumb, slider) > before + 50 && SettingsUiChecks.Right(thumb, slider) <= slider.ActualWidth + .5);
        var colors = new List<Windows.UI.Color>(); var textColors = new List<Windows.UI.Color>();
        var marker = Ui.Text("Theme label"); content.Children.Add(marker);
        var shell = (FrameworkElement)window.Content; var originalTheme = shell.RequestedTheme;
        try
        {
            foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
            {
                shell.RequestedTheme = theme; await Layout();
                colors.Add(((SolidColorBrush)Part<Border>(field, "BorderElement").Background).Color);
                textColors.Add(((SolidColorBrush)marker.Foreground).Color);
            }
        }
        finally { shell.RequestedTheme = originalTheme; await Layout(); }
        check("Custom input surfaces resolve both light and dark palettes", colors[0] != colors[1]);
        check("Existing code-built page labels update with a system theme change", textColors[0] != textColors[1] && textColors[0].R < 100 && textColors[1].R > 200);
        async Task Layout() { await Task.Delay(30); fixture.UpdateLayout(); }
    }
}
