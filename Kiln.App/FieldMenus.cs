using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App;

// Native text commands retain selection, undo and keyboard behavior with a Kiln menu.
public static class FieldMenus
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(FieldMenus), new PropertyMetadata(false, Changed));
    public static bool GetEnabled(DependencyObject value) => (bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value, bool enabled) => value.SetValue(EnabledProperty, enabled);
    private static void Changed(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is not true || owner is not Control control) return;
        var menu = new MenuFlyout();
        MenuFlyoutItem Add(string zh, string en, Action action)
        {
            var item = new MenuFlyoutItem { Text = Localization.T(zh, en) }; item.Click += (_, _) => action(); menu.Items.Add(item); return item;
        }
        if (control is TextBox text)
        {
            var undo = Add("撤销", "Undo", text.Undo); var redo = Add("重做", "Redo", text.Redo);
            menu.Items.Add(new MenuFlyoutSeparator());
            var cut = Add("剪切", "Cut", text.CutSelectionToClipboard); var copy = Add("复制", "Copy", text.CopySelectionToClipboard); var paste = Add("粘贴", "Paste", text.PasteFromClipboard);
            menu.Items.Add(new MenuFlyoutSeparator()); var all = Add("全选", "Select all", text.SelectAll);
            menu.Opening += (_, _) => { undo.IsEnabled = !text.IsReadOnly && text.CanUndo; redo.IsEnabled = !text.IsReadOnly && text.CanRedo; cut.IsEnabled = !text.IsReadOnly && text.SelectionLength > 0; copy.IsEnabled = text.SelectionLength > 0; paste.IsEnabled = !text.IsReadOnly; all.IsEnabled = text.Text.Length > 0; };
            text.SelectionFlyout = null;
        }
        else if (control is PasswordBox password) Add("粘贴", "Paste", password.PasteFromClipboard);
        else return;
        control.ContextFlyout = menu;
    }
}
