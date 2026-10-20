using Kiln.App.Pages;
using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.DotScene;

/// 背包样式的方块图标选择器（对应原版 IconPicker）：网格列出原版方块，点击选择
public sealed class IconPicker
{
    /// 显示选择器；返回选中的图标种类（如 "block:diamond_block"），取消返回 null
    public static async Task<string?> Pick(XamlRoot root, string current)
    {
        var grid = new GridView
        {
            MaxWidth = 560,
            MaxHeight = 420,
            SelectionMode = ListViewSelectionMode.Single,
        };
        grid.ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
            "<ItemsWrapGrid Orientation='Horizontal' MaximumRowsOrColumns='8'/></ItemsPanelTemplate>");
        foreach (var kind in BlockTextures.Picker)
        {
            var k = kind;
            var cell = new StackPanel { Spacing = 2 };
            var slot = new Border
            {
                MinWidth = 56,
                MinHeight = 56,
                Padding = new Thickness(6),
                BorderThickness = new Thickness(kind == current ? 2 : 1),
                BorderBrush = kind == current
                    ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Goldenrod)
                    : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                CornerRadius = new CornerRadius(4),
                Child = BlockIconControl.For(k, 40),
            };
            cell.Children.Add(slot);
            var name = k.StartsWith("block:")
                ? k["block:".Length..].Replace('_', ' ')
                : k;
            cell.Children.Add(Ui.Text(Truncate(name), 10));
            ToolTipService.SetToolTip(cell, name);
            grid.Items.Add(cell);
        }
        string? result = null;
        grid.SelectionChanged += (_, _) =>
        {
            if (grid.SelectedIndex >= 0) result = BlockTextures.Picker[grid.SelectedIndex];
        };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = Localization.T("选择版本图标", "Choose the instance icon"),
            Content = new ScrollViewer { Content = grid },
            PrimaryButtonText = Localization.T("确定", "OK"),
            CloseButtonText = Localization.T("取消", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        var choice = await dialog.ShowAsync();
        return choice == ContentDialogResult.Primary ? result ?? current : null;
    }

    private static string Truncate(string s) => s.Length > 10 ? s[..9] + "…" : s;
}

