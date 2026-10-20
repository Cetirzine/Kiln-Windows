using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

/// 账号页：离线账号、微软正版（设备代码流）、外置登录（authlib-injector）
public sealed class AccountsPage : Page
{
    private readonly StackPanel _list = new() { Spacing = 8 };

    public AccountsPage()
    {
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(0, 16, 0, 0) };
        panel.Children.Add(Ui.Text(Localization.T("账号", "Accounts"), 26, FontWeights.SemiBold));
        panel.Children.Add(Ui.Row(
            Ui.Button(Localization.T("添加离线账号", "Add offline"), async (_, _) => await AddOffline()),
            Ui.Button(Localization.T("登录微软账号", "Sign in with Microsoft"), async (_, _) => await AddMicrosoft()),
            Ui.Button(Localization.T("外置登录", "Third-party login"), async (_, _) => await AddYggdrasil())));
        panel.Children.Add(_list);
        Content = new ScrollViewer { Content = panel };
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        _list.Children.Clear();
        var selected = AccountStore.Shared.Selected;
        foreach (var acc in AccountStore.Shared.Accounts)
        {
            var a = acc;
            var isSelected = a.Id == selected?.Id;
            var header = Ui.Col(
                Ui.Text((isSelected ? "▶ " : "") + a.Name + (a.NeedsLogin == true ? "（需重新登录）" : ""), 15, FontWeights.SemiBold),
                Ui.Text(a.TypeText + (a.Kind == Account.AccountKind.Yggdrasil ? $" · {a.Server}" : ""), 12));
            var select = Ui.Button(Localization.T("使用", "Use"), (_, _) => { AccountStore.Shared.Select(a); Reload(); });
            select.IsEnabled = !isSelected;
            var remove = Ui.Button(Localization.T("删除", "Remove"), (_, _) => { AccountStore.Shared.Remove(a); Reload(); });
            _list.Children.Add(Ui.Row(header, select, remove));
        }
        if (AccountStore.Shared.Accounts.Count == 0)
            _list.Children.Add(Ui.Text(Localization.T("还没有账号。", "No accounts yet."), 13));
    }

    private async Task AddOffline()
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
        try
        {
            var dc = await MicrosoftAuth.RequestDeviceCode();
            var open = Ui.Button(Localization.T("打开登录网页", "Open sign-in page"), (_, _) =>
            {
                try { _ = Windows.System.Launcher.LaunchUriAsync(new Uri(dc.VerificationUri)); }
                catch { }
            });
            var codeText = Ui.Text(dc.UserCode, 32, FontWeights.Bold);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Localization.T("微软登录", "Microsoft sign-in"),
                Content = Ui.Col(
                    Ui.Text(Localization.T($"在浏览器中输入代码 {dc.UserCode}", $"Enter the code {dc.UserCode} in your browser"), 14),
                    codeText,
                    Ui.Text(dc.VerificationUri, 13),
                    open),
                CloseButtonText = Localization.T("取消", "Cancel"),
            };
            var dialogTask = dialog.ShowAsync().AsTask();
            var pollTask = MicrosoftAuth.Poll(dc);
            var done = await Task.WhenAny(dialogTask, pollTask);
            if (done == dialogTask) return; // 用户取消
            var (access, refresh) = pollTask.Result;
            var result = await MicrosoftAuth.Minecraft(access, refresh);
            AccountStore.Shared.AddMicrosoft((result.Name, result.Uuid), result.RefreshToken, result.AccessToken, result.Expires);
            Reload();
            await App.ShowInfo(App.MainAppWindow!, Localization.T($"已登录 {result.Name}", $"Signed in as {result.Name}"));
        }
        catch (Exception e)
        {
            await Ui.ShowError(XamlRoot, e);
        }
    }

    private async Task AddYggdrasil()
    {
        try
        {
            var (okUrl, url) = await Ui.Prompt(XamlRoot, Localization.T("认证服务器地址", "Authentication server"), "littleskin.cn",
                Yggdrasil.Presets[0].Url);
            if (!okUrl) return;
            var (root, meta) = await Yggdrasil.Resolve(url);
            var serverName = Yggdrasil.ServerName(meta, root);
            var (okMail, mail) = await Ui.Prompt(XamlRoot, Localization.T($"{serverName} · 邮箱 / 用户名", $"{serverName} · email / username"), "you@example.com");
            if (!okMail) return;
            var (okPass, pass) = await Ui.Prompt(XamlRoot, Localization.T("密码（不保存）", "Password (not saved)"), "");
            if (!okPass) return;
            var session = await Yggdrasil.Authenticate(root, mail, pass);
            var profile = session.Selected ?? session.Profiles.FirstOrDefault();
            if (profile == null) throw new LauncherException(Localization.T("这个账号没有角色", "This account has no profile"));
            AccountStore.Shared.AddYggdrasil(root, serverName, mail, profile, session.AccessToken, session.ClientToken);
            Reload();
        }
        catch (Exception e)
        {
            await Ui.ShowError(XamlRoot, e);
        }
    }
}

