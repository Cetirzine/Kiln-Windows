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
            var actions = Ui.Row(select, remove);
            // 皮肤与正版档案（对应原版 AccountUI）
            var skin = Ui.Button(Localization.T("皮肤…", "Skin…"), async (_, _) => await SkinsDialog(a));
            if (a.Kind == Account.AccountKind.Offline) actions.Children.Insert(0, skin);
            if (a.Kind == Account.AccountKind.Microsoft)
                actions.Children.Insert(0, Ui.Button(Localization.T("正版档案…", "Profile…"), async (_, _) => await ProfileDialog(a)));
            _list.Children.Add(Ui.Row(header, actions));
        }
        if (AccountStore.Shared.Accounts.Count == 0)
            _list.Children.Add(Ui.Text(Localization.T("还没有账号。", "No accounts yet."), 13));
    }

    /// 正版档案：改名（实时可用性检查）、拥有的游戏、联机/聊天权限与封禁状态
    private async Task ProfileDialog(Account acc)
    {
        try
        {
            var login = await AccountStore.Shared.Login(acc);
            var panel = new StackPanel { Spacing = 10, MinWidth = 380 };
            panel.Children.Add(Ui.Text(Localization.T("加载中…", "Loading…"), 13));
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Localization.T($"正版档案 — {acc.Name}", $"Profile — {acc.Name}"),
                Content = panel,
                CloseButtonText = Localization.T("关闭", "Close"),
            };
            _ = dialog.ShowAsync();
            panel.Children.Clear();
            var token = login.AccessToken;
            var change = await MinecraftServices.GetNameChange(token);
            panel.Children.Add(Ui.Text(
                (change.NextChange != null
                    ? Localization.T($"下次可改名：{change.NextChange:yyyy-MM-dd}", $"Next name change: {change.NextChange:yyyy-MM-dd}")
                    : Localization.T("现在可以改名", "You can change your name now")) + $" · {acc.Name}", 13));
            var nameBox = new TextBox { Header = Localization.T("改名", "Change name"), PlaceholderText = acc.Name };
            var statusText = Ui.Text("", 12);
            var busy = false;
            nameBox.TextChanged += async (_, _) =>
            {
                var name = nameBox.Text.Trim();
                if (!MinecraftServices.IsValidName(name) || busy) return;
                busy = true;
                try
                {
                    var status = await MinecraftServices.NameStatusOf(name, token);
                    statusText.Text = status switch
                    {
                        MinecraftServices.NameStatus.Available => Localization.T("✓ 可以使用", "✓ Available"),
                        MinecraftServices.NameStatus.Taken => Localization.T("已被使用", "Taken"),
                        _ => Localization.T("不可用", "Not allowed"),
                    };
                }
                catch { }
                busy = false;
            };
            panel.Children.Add(nameBox);
            panel.Children.Add(statusText);
            var changeBtn = Ui.Button(Localization.T("改名", "Change"), async (_, _) =>
            {
                try
                {
                    var newName = await MinecraftServices.ChangeName(nameBox.Text.Trim(), token);
                    AccountStore.Shared.UpdateName(acc, newName);
                    await App.ShowInfo(App.MainAppWindow!, Localization.T($"已改名为 {newName}", $"Renamed to {newName}"));
                    dialog.Hide();
                    Reload();
                }
                catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
            });
            panel.Children.Add(changeBtn);
            try
            {
                var own = await MinecraftServices.GetOwnership(token);
                panel.Children.Add(Ui.Text(
                    (own.JavaPurchased ? Localization.T("已买断 Java 版", "Java Edition (owned)")
                                        : Localization.T("Game Pass（未买断）", "Game Pass (not owned)"))
                    + (own.Bedrock ? " · Bedrock" : "") + (own.Dungeons ? " · Dungeons" : "") + (own.Legends ? " · Legends" : ""), 12));
            }
            catch { }
            try
            {
                var attr = await MinecraftServices.GetAttributes(token);
                var lines = new List<string>
                {
                    (attr.Multiplayer ? "✓ " : "✗ ") + Localization.T("多人游戏", "Multiplayer"),
                    (attr.Realms ? "✓ " : "✗ ") + "Realms",
                    (attr.Chat ? "✓ " : "✗ ") + Localization.T("聊天", "Chat"),
                };
                if (attr.MultiplayerBan is { } ban)
                    lines.Add((ban.Expires != null
                        ? Localization.T($"联机封禁中（至 {ban.Expires:yyyy-MM-dd}）", $"Banned from multiplayer (until {ban.Expires:yyyy-MM-dd})")
                        : Localization.T("联机封禁中", "Banned from multiplayer"))
                        + (ban.Reason != null ? $": {ban.Reason}" : ""));
                panel.Children.Add(Ui.Text(string.Join(" · ", lines), 12));
            }
            catch { }
        }
        catch (Exception e)
        {
            await Ui.ShowError(XamlRoot, e);
        }
    }

    /// 皮肤管理：离线账号可用本地皮肤文件或同名正版玩家的皮肤
    private async Task SkinsDialog(Account acc)
    {
        var skin = acc.Skin ?? new Account.OfflineSkin();
        var panel = new StackPanel { Spacing = 10, MinWidth = 380 };
        var fileBox = new TextBox { Header = Localization.T("皮肤文件（PNG 64×64 / 64×32）", "Skin file (PNG 64×64 / 64×32)"), PlaceholderText = skin.File ?? "" };
        var premiumBox = new TextBox { Header = Localization.T("同名正版玩家的皮肤（输入玩家名）", "Use a premium player's skin (name)"), PlaceholderText = skin.Premium ?? "" };
        var slim = Ui.Toggle(skin.Slim, "Alex（纤细）", "Steve（经典）", _ => { });
        panel.Children.Add(Ui.Button(Localization.T("选择皮肤文件…", "Choose skin file…"), async (_, _) =>
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow!));
            picker.FileTypeFilter.Add(".png");
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            try
            {
                var name = Skins.ImportSkin(file.Path);
                fileBox.Text = name;
                Skins.AddToLibrary(await File.ReadAllBytesAsync(file.Path), slim.IsOn, acc.Name);
            }
            catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
        }));
        panel.Children.Add(fileBox);
        panel.Children.Add(premiumBox);
        panel.Children.Add(slim);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Localization.T($"皮肤 — {acc.Name}", $"Skin — {acc.Name}"),
            Content = panel,
            PrimaryButtonText = Localization.T("保存", "Save"),
            CloseButtonText = Localization.T("取消", "Cancel"),
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var newSkin = new Account.OfflineSkin
        {
            File = fileBox.Text.Trim().Length > 0 ? fileBox.Text.Trim() : null,
            Premium = premiumBox.Text.Trim().Length > 0 ? premiumBox.Text.Trim() : null,
            Slim = slim.IsOn,
        };
        AccountStore.Shared.SetSkin(acc, newSkin.IsEmpty ? null : newSkin);
        Reload();
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

