using Kiln.App.Pages;
using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Kiln.App.DotScene;

public static class IconPicker
{
    public static async Task<string?> Pick(XamlRoot root, string current, FrameworkElement? anchor = null)
    {
        await BlockTextures.Shared.Load(InstanceScanner.Scan(LauncherSettings.Shared.MinecraftFolder));
        await BlockTextures.Shared.Ensure(BlockTextures.Picker);
        var completion = new TaskCompletionSource<string?>();
        var popup = new Popup { XamlRoot = root, IsLightDismissEnabled = true, ShouldConstrainToRootBounds = true };
        var body = new StackPanel { Spacing = 10, Width = 324 };
        var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Ui.Text(Localization.T("选择图标", "Choose an icon"), 13, FontWeights.SemiBold));
        var reset = Ui.Button(Localization.T("恢复默认", "Use default"), (_, _) => { completion.TrySetResult(""); popup.IsOpen = false; }); Grid.SetColumn(reset, 1); header.Children.Add(reset); body.Children.Add(header);
        var grid = new Grid(); for (var i = 0; i < 9; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        var choices = BlockTextures.Picker.Where(kind => BlockTextures.Shared.Exists(kind) || !BlockTextures.Shared.HasTextures).ToList();
        for (var n = 0; n < choices.Count; n++)
        {
            if (n % 9 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            var kind = choices[n];
            var slot = new Grid { Width = 36, Height = 36, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 139, 139, 139)) };
            var block = BlockIconControl.For(kind, 26); block.HorizontalAlignment = HorizontalAlignment.Center; block.VerticalAlignment = VerticalAlignment.Center; slot.Children.Add(block);
            slot.Children.Add(new Border { IsHitTestVisible = false, BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 55, 55, 55)), BorderThickness = new Thickness(1, 1, 0, 0) });
            slot.Children.Add(new Border { IsHitTestVisible = false, BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.White), BorderThickness = new Thickness(0, 0, 1, 1) });
            if (kind == current) slot.Children.Add(new Border { BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.White), BorderThickness = new Thickness(2), IsHitTestVisible = false });
            var button = new Button { Content = slot, Padding = new Thickness(0), Width = 36, Height = 36, Style = (Style)Application.Current.Resources["KilnNavigationButton"] };
            ToolTipService.SetToolTip(button, kind.StartsWith("block:") ? kind[6..] : kind);
            button.Click += (_, _) => { completion.TrySetResult(kind); popup.IsOpen = false; };
            Grid.SetColumn(button, n % 9); Grid.SetRow(button, n / 9); grid.Children.Add(button);
        }
        body.Children.Add(grid);
        if (!BlockTextures.Shared.HasTextures) body.Children.Add(Ui.Text(Localization.T("安装任意一个 1.13 或更新版本后，这里会显示游戏里的方块材质。", "Install any version from 1.13 on to see in-game block textures here."), 11.5));
        var input = new TextBox { PlaceholderText = Localization.T("材质名，例如 diamond_ore", "Texture name, e.g. diamond_ore"), MinWidth = 0 };
        var error = Ui.Text("", 11.5);
        async Task UseCustom()
        {
            var name = input.Text.Trim().ToLowerInvariant().Replace("minecraft:", "").Replace(' ', '_'); if (name.Length == 0) return;
            var kind = "block:" + name; await BlockTextures.Shared.Ensure(new[] { kind });
            if (BlockTextures.Shared.Exists(kind)) { completion.TrySetResult(kind); popup.IsOpen = false; }
            else error.Text = Localization.T($"没有找到方块材质 {name}。", $"No block texture named {name} was found.");
        }
        var custom = new Grid { ColumnSpacing = 6 }; custom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); custom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        custom.Children.Add(input); var use = Ui.Button(Localization.T("使用", "Use"), async (_, _) => await UseCustom()); Grid.SetColumn(use, 1); custom.Children.Add(use);
        input.KeyDown += async (_, args) => { if (args.Key == Windows.System.VirtualKey.Enter) { args.Handled = true; await UseCustom(); } };
        body.Children.Add(Ui.Text(Localization.T("其他方块", "Another block"), 12, FontWeights.Medium)); body.Children.Add(custom); body.Children.Add(error);
        var border = new Border { Padding = new Thickness(12), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 198, 198, 198)), BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.White), BorderThickness = new Thickness(2, 2, 0, 0), Child = body, RequestedTheme = ElementTheme.Light };
        var panel = new Border { BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 85, 85, 85)), BorderThickness = new Thickness(0, 0, 2, 2), Child = border };
        popup.Child = panel;
        var origin = anchor?.TransformToVisual(root.Content).TransformPoint(new Windows.Foundation.Point(0, anchor.ActualHeight)) ?? new Windows.Foundation.Point((root.Size.Width - 352) / 2, 90);
        panel.Measure(new Windows.Foundation.Size(352, root.Size.Height - 40));
        popup.HorizontalOffset = Math.Clamp(origin.X, 12, Math.Max(12, root.Size.Width - 364));
        popup.VerticalOffset = Math.Clamp(origin.Y + 6, 12, Math.Max(12, root.Size.Height - panel.DesiredSize.Height - 12));
        popup.Closed += (_, _) => completion.TrySetResult(null);
        popup.IsOpen = true;
        return await completion.Task;
    }
}
