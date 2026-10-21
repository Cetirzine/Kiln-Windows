using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

public sealed class AccountsPage : Page
{
    private bool _signingIn;
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly FlowRow _quickActions = new() { Spacing = 14 };
    private XamlRoot DialogRoot => MainWindow.Current!.Content.XamlRoot;
    private static string T(string zh, string en) => Localization.T(zh, en);
    public AccountsPage(bool compact = false)
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var name = new TextBox { PlaceholderText = T("玩家名称", "Player name"), MinWidth = 0, Height = 30, MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center };
        var warning = Ui.Text("", 11); warning.Visibility = Visibility.Collapsed;
        void Add()
        {
            var value = name.Text.Trim(); var error = AccountStore.ValidateOffline(value);
            if (error != null) { warning.Text = error; warning.Visibility = Visibility.Visible; return; }
            AccountStore.Shared.AddOffline(value); name.Text = ""; warning.Text = ""; warning.Visibility = Visibility.Collapsed; Reload(); MainWindow.Current!.AccountChanged();
        }
        var offline = new Grid { ColumnSpacing = 8 }; offline.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); offline.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); offline.Children.Add(name);
        var add = ActionButton(T("确定", "OK"), (_, _) => Add()); add.Width = 64; add.IsEnabled = false; Grid.SetColumn(add, 1); offline.Children.Add(add);
        name.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; Add(); } };
        name.TextChanged += (_, _) => { add.IsEnabled = name.Text.Trim().Length > 0; warning.Text = name.Text.Length > 16 ? T("1.20.3 起名称最多 16 个字符", "Since 1.20.3 names are limited to 16 characters") : ""; warning.Visibility = warning.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible; };
        var signIns = new Grid { ColumnSpacing = 8 };
        signIns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) }); signIns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        var microsoft = ActionButton(T("微软账号登录", "Sign in with Microsoft"), async (_, _) => await AddMicrosoft()); microsoft.Background = Ui.Brush("KilnAccent"); microsoft.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
        var thirdParty = ActionButton(T("外置登录", "Third-party"), async (_, _) => await AddYggdrasil()); Grid.SetColumn(thirdParty, 1); signIns.Children.Add(microsoft); signIns.Children.Add(thirdParty);
        var buy = ActionButton(T("购买 Minecraft", "Buy Minecraft"), async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.minecraft.net/store/minecraft-java-bedrock-edition-pc")));
        buy.Style = (Style)Application.Current.Resources["KilnNavigationButton"]; buy.Foreground = Ui.Brush("KilnAccent"); buy.Padding = new Thickness(0); buy.HorizontalAlignment = HorizontalAlignment.Left; buy.Height = 24; buy.MinHeight = 24;
        var panel = Ui.Col(_list, _quickActions, new Border { Height = 1, Background = Ui.Brush("KilnStroke") }, Ui.Text(T("添加离线账号", "Add offline account"), 12, FontWeights.SemiBold), offline, warning,
            new Border { Height = 1, Background = Ui.Brush("KilnStroke") }, signIns, buy); panel.Spacing = 12;
        if (!compact) panel.Children.Insert(0, Ui.Text(T("账号", "Accounts"), 21, FontWeights.Bold));
        Content = new ScrollViewer { Content = panel, MaxHeight = compact ? 500 : double.PositiveInfinity, HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled };
        Loaded += (_, _) => Reload();
    }
    private static Button ActionButton(string title, RoutedEventHandler action)
    {
        var button = Ui.Button(title, action); button.MinWidth = 0; button.Height = 30; button.Padding = new Thickness(10, 3, 10, 3); button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.Content = new TextBlock { Text = title, FontSize = 12, TextWrapping = TextWrapping.NoWrap, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
        return button;
    }
    private void Reload()
    {
        _list.Children.Clear();
        var store = AccountStore.Shared;
        _quickActions.Children.Clear();
        var currentInstance = InstanceScanner.Scan(LauncherSettings.Shared.MinecraftFolder).FirstOrDefault(i => i.Name == LauncherSettings.Shared.SelectedInstance);
        if ((currentInstance == null ? store.Selected : store.AccountFor(currentInstance.Setup)) is { } effective)
        {
            var skin = ActionButton(T("更换皮肤", "Change skin"), async (_, _) => { var root = DialogRoot; await MainWindow.Current!.CloseAccountsAsync(); await SkinEditor.Show(root, effective); });
            skin.Style = (Style)Application.Current.Resources["KilnNavigationButton"]; skin.Foreground = Ui.Brush("KilnAccent"); skin.Padding = new Thickness(0); skin.HorizontalAlignment = HorizontalAlignment.Left; _quickActions.Children.Add(skin);
            if (effective.Kind == Account.AccountKind.Microsoft)
            {
                var profile = ActionButton(T("正版档案与改名", "Profile & name"), async (_, _) => { var root = DialogRoot; await MainWindow.Current!.CloseAccountsAsync(); await ProfileEditor.Show(root, effective); });
                profile.Style = (Style)Application.Current.Resources["KilnNavigationButton"]; profile.Foreground = Ui.Brush("KilnAccent"); profile.Padding = new Thickness(0); _quickActions.Children.Add(profile);
            }
        }
        _quickActions.Visibility = _quickActions.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
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
            void Action(string title, Func<Task> work) { var item = new MenuFlyoutItem { Text = title }; item.Click += async (_, _) => { try { await MainWindow.Current!.CloseAccountsAsync(); await work(); } catch (Exception e) { await Ui.ShowError(DialogRoot, e); } }; menu.Items.Add(item); }
            Action(T("皮肤…", "Skin…"), async () => await SkinEditor.Show(DialogRoot, account));
            if (account.Kind == Account.AccountKind.Microsoft) Action(T("正版档案与改名…", "Profile & name…"), async () => await ProfileEditor.Show(DialogRoot, account));
            if (account.Kind == Account.AccountKind.Offline) Action(T("修改名称…", "Rename…"), async () =>
            {
                var (ok, name) = await Ui.Prompt(DialogRoot, T("修改离线名称", "Rename offline account"), T("玩家名称", "Player name"), account.Name); if (!ok) return;
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
                var dialog = new KilnDialog { XamlRoot = DialogRoot, Title = T("删除账号", "Remove account"), Content = account.Name + "（" + account.TypeText + "）", PrimaryButtonText = T("删除", "Remove"), CloseButtonText = T("取消", "Cancel") };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return; store.Remove(account); Reload(); MainWindow.Current!.AccountChanged();
            });
            more.Flyout = menu; _list.Children.Add(row);
        }
        if (store.Accounts.Count == 0) _list.Children.Add(Ui.Text(T("还没有账号。", "No accounts yet."), 12));
    }

    private async Task AddMicrosoft()
    {
        if (_signingIn) return; _signingIn = true;
        var window = MainWindow.Current!; var root = window.Content.XamlRoot;
        KilnDialog? dialog = null;
        Task<ContentDialogResult>? shown = null;
        Task<MicrosoftAuth.Result>? login = null;
        using var cancellation = new CancellationTokenSource();
        try
        {
            await window.CloseAccountsAsync();
            MicrosoftAuth.DeviceCode? device = null;
            var code = Ui.Text("···", 32, FontWeights.Bold);
            var address = Ui.Text("", 12);
            var open = ActionButton(T("打开登录网页", "Open sign-in page"), async (_, _) => { if (device != null) await Windows.System.Launcher.LaunchUriAsync(new Uri(device.VerificationUri)); });
            var copy = ActionButton(T("复制代码", "Copy code"), (_, _) => { if (device == null) return; var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(device.UserCode); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data); });
            open.IsEnabled = copy.IsEnabled = false;
            var actions = new Grid { ColumnSpacing = 8 }; actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.Children.Add(open); Grid.SetColumn(copy, 1); actions.Children.Add(copy);
            var stage = T("正在获取登录代码…", "Requesting sign-in code…");
            var stageTime = System.Diagnostics.Stopwatch.StartNew();
            var status = Ui.Text(stage, 12);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) => status.Text = stage + T($"（{stageTime.Elapsed.TotalSeconds:0} 秒）", $" ({stageTime.Elapsed.TotalSeconds:0}s)");
            void SetStage(string value) { stage = value; stageTime.Restart(); status.Text = value; Log.log("[Auth] " + value); }
            dialog = new KilnDialog { XamlRoot = root, Title = T("微软账号登录", "Sign in with Microsoft"), Content = Ui.Col(code, address, actions, status), CloseButtonText = T("取消", "Cancel") };
            dialog.Closing += () => { cancellation.Cancel(); timer.Stop(); };
            shown = dialog.ShowAsync();
            timer.Start();
            async Task<MicrosoftAuth.Result> SignIn()
            {
                using var deviceTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token); deviceTimeout.CancelAfter(TimeSpan.FromSeconds(25));
                try { device = await MicrosoftAuth.RequestDeviceCode(deviceTimeout.Token); }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new LauncherException(T("获取微软登录代码超时，请检查网络后重试。", "Requesting a Microsoft sign-in code timed out. Check the network and retry.")); }
                cancellation.Token.ThrowIfCancellationRequested();
                code.Text = device.UserCode; address.Text = device.VerificationUri; open.IsEnabled = copy.IsEnabled = true; SetStage(T("等待浏览器完成登录…", "Waiting for browser sign-in…"));
                var (access, refresh) = await MicrosoftAuth.Poll(device, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                using var profileTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token); profileTimeout.CancelAfter(TimeSpan.FromSeconds(90));
                try { return await MicrosoftAuth.Minecraft(access, refresh, profileTimeout.Token, SetStage); }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new LauncherException(T($"{stage}\n连接超时，请检查 Xbox Live / Minecraft 服务的网络连接后重试。", $"{stage}\nThe connection timed out. Check connectivity to Xbox Live / Minecraft services and retry.")); }
            }
            login = SignIn();
            if (await Task.WhenAny(shown, login) == shown) { cancellation.Cancel(); await shown; try { await login; } catch (OperationCanceledException) { } catch (Exception e) { Log.log("[Auth] Cancelled sign-in: " + e.Friendly()); } return; }
            var result = await login; cancellation.Token.ThrowIfCancellationRequested();
            AccountStore.Shared.AddMicrosoft((result.Name, result.Uuid), result.RefreshToken, result.AccessToken, result.Expires);
            dialog.Hide(); await shown; Reload(); MainWindow.Current!.AccountChanged();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception e) { dialog?.Hide(); if (shown != null) await shown; await Ui.ShowError(root, e); }
        finally { cancellation.Cancel(); dialog?.Hide(); if (shown != null) await shown; _signingIn = false; }
    }
    private async Task AddYggdrasil(Account? existing = null)
    {
        if (_signingIn) return; _signingIn = true;
        try { await MainWindow.Current!.CloseAccountsAsync(); await AddYggdrasilCore(existing); }
        catch (Exception e) { await Ui.ShowError(DialogRoot, e); }
        finally { _signingIn = false; }
    }
    private async Task AddYggdrasilCore(Account? existing)
    {
        var server = new TextBox { Text = existing?.Server ?? Yggdrasil.Presets[0].Url, Header = T("认证服务器", "Authentication server") };
        var username = new TextBox { Text = existing?.LoginName ?? "", Header = T("邮箱 / 用户名", "Email / username") };
        var password = new PasswordBox { Header = T("密码（不保存）", "Password (not saved)") };
        var presets = new ComboBox { PlaceholderText = T("常用服务器", "Common servers") }; foreach (var preset in Yggdrasil.Presets) presets.Items.Add(preset.Url);
        presets.SelectionChanged += (_, _) => { if (presets.SelectedItem is string value) server.Text = value; };
        var status = Ui.Text("", 11.5); var form = Ui.Col(presets, server, username, password, status);
        while (true)
        {
            var dialog = new KilnDialog { XamlRoot = DialogRoot, Title = T("外置登录", "Third-party login"), Content = form, PrimaryButtonText = T("登录", "Sign in"), CloseButtonText = T("取消", "Cancel") };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            try
            {
                var (root, meta) = await Yggdrasil.Resolve(server.Text.Trim());
                var session = await Yggdrasil.Authenticate(root, username.Text.Trim(), password.Password);
                var profile = session.Selected;
                if (profile == null && session.Profiles.Count > 1)
                {
                    var choices = new ListView(); foreach (var candidate in session.Profiles) choices.Items.Add(candidate.Name); choices.SelectedIndex = 0;
                    var choose = new KilnDialog { XamlRoot = DialogRoot, Title = T("选择角色", "Choose a profile"), Content = choices, PrimaryButtonText = T("确定", "OK"), CloseButtonText = T("取消", "Cancel") };
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
