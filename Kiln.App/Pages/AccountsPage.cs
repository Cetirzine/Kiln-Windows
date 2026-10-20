using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

public sealed class AccountsPage : Page
{
    private readonly StackPanel _list = new() { Spacing = 0 };
    private static string T(string zh, string en) => Localization.T(zh, en);
    public AccountsPage(bool compact = false)
    {
        var name = new TextBox { PlaceholderText = T("玩家名称", "Player name"), MinWidth = 0 };
        var warning = Ui.Text("", 11);
        void Add()
        {
            var value = name.Text.Trim(); var error = AccountStore.ValidateOffline(value);
            if (error != null) { warning.Text = error; return; }
            AccountStore.Shared.AddOffline(value); name.Text = ""; warning.Text = ""; Reload();
        }
        var offline = new Grid { ColumnSpacing = 8 }; offline.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); offline.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); offline.Children.Add(name);
        var add = Ui.Button(T("确定", "OK"), (_, _) => Add()); Grid.SetColumn(add, 1); offline.Children.Add(add);
        name.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; Add(); } };
        name.TextChanged += (_, _) => warning.Text = name.Text.Length > 16 ? T("1.20.3 起名称最多 16 个字符", "Since 1.20.3 names are limited to 16 characters") : "";
        var panel = Ui.Col(_list, new Border { Height = 1, Background = Ui.Brush("KilnStroke") }, Ui.Text(T("添加离线账号", "Add offline account"), 12, FontWeights.SemiBold), offline, warning,
            Ui.Row(Ui.Button(T("微软账号登录", "Sign in with Microsoft"), async (_, _) => await AddMicrosoft()), Ui.Button(T("外置登录", "Third-party"), async (_, _) => await AddYggdrasil())),
            Ui.Button(T("购买 Minecraft", "Buy Minecraft"), async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.minecraft.net/store/minecraft-java-bedrock-edition-pc")))); panel.Spacing = 12;
        if (!compact) panel.Children.Insert(0, Ui.Text(T("账号", "Accounts"), 21, FontWeights.Bold));
        Content = new ScrollViewer { Content = panel, MaxHeight = compact ? 500 : double.PositiveInfinity };
        Loaded += (_, _) => Reload();
    }
    private void Reload()
    {
        _list.Children.Clear();
        var store = AccountStore.Shared;
        foreach (var account in store.Accounts)
        {
            var labels = Ui.Col(Ui.Text(account.Name, 13, FontWeights.SemiBold), Ui.Text(account.TypeText + (account.NeedsLogin == true ? T(" · 需要重新登录", " · Needs to sign in again") : ""), 11.5)); labels.Spacing = 2;
            var row = new Grid { ColumnSpacing = 8, Padding = new Thickness(8, 7, 8, 7), Background = account.Id == store.Selected?.Id ? Ui.Brush("KilnPanelSecondary") : null };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var select = new Button { Content = labels, HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0), Style = (Style)Application.Current.Resources["KilnNavigationButton"] };
            select.Click += async (_, _) => { if (account.NeedsLogin == true) { if (account.Kind == Account.AccountKind.Microsoft) await AddMicrosoft(); else await AddYggdrasil(account); } else { store.Select(account); Reload(); MainWindow.Current!.AccountChanged(); } };
            row.Children.Add(select);
            var more = new Button { Content = "···", Padding = new Thickness(6, 2, 6, 2) }; Grid.SetColumn(more, 1); row.Children.Add(more);
            var menu = new MenuFlyout();
            void Action(string title, Func<Task> work) { var item = new MenuFlyoutItem { Text = title }; item.Click += async (_, _) => { try { await work(); } catch (Exception e) { await Ui.ShowError(XamlRoot, e); } }; menu.Items.Add(item); }
            Action(T("皮肤…", "Skin…"), async () => await SkinEditor.Show(XamlRoot, account));
            if (account.Kind == Account.AccountKind.Microsoft) Action(T("正版档案与改名…", "Profile & name…"), async () => await ProfileEditor.Show(XamlRoot, account));
            if (account.Kind == Account.AccountKind.Offline) Action(T("修改名称…", "Rename…"), async () =>
            {
                var (ok, name) = await Ui.Prompt(XamlRoot, T("修改离线名称", "Rename offline account"), T("玩家名称", "Player name"), account.Name); if (!ok) return;
                if (AccountStore.ValidateOffline(name) is { } error) throw new LauncherException(error);
                var renamed = store.RenameOffline(account, name) ?? throw new LauncherException(T("已经有同名离线账号", "An offline account already has this name"));
                foreach (var folder in new[] { LauncherSettings.Shared.MinecraftFolder }.Concat(LauncherSettings.Shared.ExtraFolders).Distinct(StringComparer.OrdinalIgnoreCase))
                    foreach (var instance in InstanceScanner.Scan(folder)) { var setup = instance.Setup; if (setup.Account == account.Id) { setup.Account = renamed.Id; LauncherSettings.Shared.SaveInstanceSetup(setup, instance.Dir); } }
                Reload(); MainWindow.Current!.AccountChanged();
            });
            else Action(T("重新登录…", "Sign in again…"), async () => { if (account.Kind == Account.AccountKind.Microsoft) await AddMicrosoft(); else await AddYggdrasil(account); });
            var selected = InstanceScanner.Scan(LauncherSettings.Shared.MinecraftFolder).FirstOrDefault(i => i.Name == LauncherSettings.Shared.SelectedInstance);
            if (selected != null) Action(selected.Setup.Account == account.Id ? T("取消固定给此版本", "Unpin from this instance") : T("固定给此版本使用", "Pin to this instance"), () => { var setup = selected.Setup; setup.Account = setup.Account == account.Id ? null : account.Id; LauncherSettings.Shared.SaveInstanceSetup(setup, selected.Dir); MainWindow.Current!.AccountChanged(); return Task.CompletedTask; });
            Action(T("复制 UUID", "Copy UUID"), () => { var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(account.Uuid); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data); return Task.CompletedTask; });
            Action(T("上移", "Move up"), () => { store.Move(account, -1); Reload(); return Task.CompletedTask; });
            Action(T("下移", "Move down"), () => { store.Move(account, 1); Reload(); return Task.CompletedTask; });
            Action(T("删除…", "Remove…"), async () =>
            {
                var dialog = new KilnDialog { XamlRoot = XamlRoot, Title = T("删除账号", "Remove account"), Content = account.Name + "（" + account.TypeText + "）", PrimaryButtonText = T("删除", "Remove"), CloseButtonText = T("取消", "Cancel") };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return; store.Remove(account); Reload(); MainWindow.Current!.AccountChanged();
            });
            more.Flyout = menu; _list.Children.Add(row);
        }
        if (store.Accounts.Count == 0) _list.Children.Add(Ui.Text(T("还没有账号。", "No accounts yet."), 12));
    }    private async Task AddOffline()
    {
        var (ok, name) = await Ui.Prompt(XamlRoot, Localization.T("玩家名", "Player name"), "Steve");
        if (!ok) return;
        var error = AccountStore.ValidateOffline(name);
        if (error != null) { await Ui.ShowError(XamlRoot, error); return; }
        AccountStore.Shared.AddOffline(name);
        Reload();
    }

    private async Task AddMicrosoft()
    {
        KilnDialog? dialog = null;
        using var cancellation = new CancellationTokenSource();
        try
        {
            var dc = await MicrosoftAuth.RequestDeviceCode();
            var open = Ui.Button(T("打开登录网页", "Open sign-in page"), async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri(dc.VerificationUri)));
            var copy = Ui.Button(T("复制代码", "Copy code"), (_, _) => { var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(dc.UserCode); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data); });
            var status = Ui.Text(T("等待浏览器完成登录…", "Waiting for browser sign-in…"), 12);
            dialog = new KilnDialog { XamlRoot = XamlRoot, Title = T("微软账号登录", "Sign in with Microsoft"), Content = Ui.Col(Ui.Text(dc.UserCode, 32, FontWeights.Bold), Ui.Text(dc.VerificationUri, 12), Ui.Row(open, copy), status), CloseButtonText = T("取消", "Cancel") };
            var shown = dialog.ShowAsync(); var poll = MicrosoftAuth.Poll(dc, cancellation.Token);
            if (await Task.WhenAny(shown, poll) == shown)
            {
                cancellation.Cancel(); try { await poll; } catch (OperationCanceledException) { } catch (Exception e) { Log.log("[Auth] " + e.Friendly()); } return;
            }
            var (access, refresh) = await poll; status.Text = T("正在读取 Minecraft 档案…", "Loading Minecraft profile…");
            var result = await MicrosoftAuth.Minecraft(access, refresh);
            AccountStore.Shared.AddMicrosoft((result.Name, result.Uuid), result.RefreshToken, result.AccessToken, result.Expires);
            dialog.Hide(); await shown; Reload(); MainWindow.Current!.AccountChanged();
        }
        catch (Exception e) { dialog?.Hide(); await Ui.ShowError(XamlRoot, e); }
        finally { cancellation.Cancel(); dialog?.Hide(); }
    }
    private async Task AddYggdrasil(Account? existing = null)
    {
        var server = new TextBox { Text = existing?.Server ?? Yggdrasil.Presets[0].Url, Header = T("认证服务器", "Authentication server") };
        var username = new TextBox { Text = existing?.LoginName ?? "", Header = T("邮箱 / 用户名", "Email / username") };
        var password = new PasswordBox { Header = T("密码（不保存）", "Password (not saved)") };
        var presets = new ComboBox { PlaceholderText = T("常用服务器", "Common servers") }; foreach (var preset in Yggdrasil.Presets) presets.Items.Add(preset.Url);
        presets.SelectionChanged += (_, _) => { if (presets.SelectedItem is string value) server.Text = value; };
        var status = Ui.Text("", 11.5); var form = Ui.Col(presets, server, username, password, status);
        while (true)
        {
            var dialog = new KilnDialog { XamlRoot = XamlRoot, Title = T("外置登录", "Third-party login"), Content = form, PrimaryButtonText = T("登录", "Sign in"), CloseButtonText = T("取消", "Cancel") };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            try
            {
                var (root, meta) = await Yggdrasil.Resolve(server.Text.Trim());
                var session = await Yggdrasil.Authenticate(root, username.Text.Trim(), password.Password);
                var profile = session.Selected;
                if (profile == null && session.Profiles.Count > 1)
                {
                    var choices = new ListView(); foreach (var candidate in session.Profiles) choices.Items.Add(candidate.Name); choices.SelectedIndex = 0;
                    var choose = new KilnDialog { XamlRoot = XamlRoot, Title = T("选择角色", "Choose a profile"), Content = choices, PrimaryButtonText = T("确定", "OK"), CloseButtonText = T("取消", "Cancel") };
                    if (await choose.ShowAsync() != ContentDialogResult.Primary) return;
                    profile = session.Profiles[choices.SelectedIndex];
                }
                profile ??= session.Profiles.FirstOrDefault();
                if (profile == null) throw new LauncherException(T("这个账号没有角色", "This account has no profile"));
                AccountStore.Shared.AddYggdrasil(root, Yggdrasil.ServerName(meta, root), username.Text.Trim(), profile, session.AccessToken, session.ClientToken);
                password.Password = ""; Reload(); MainWindow.Current!.AccountChanged(); return;
            }
            catch (Exception e) { status.Text = e.Friendly(); }
        }
    }
}
