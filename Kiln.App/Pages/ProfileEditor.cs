using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;
internal static class ProfileEditor
{
    private static string T(string zh, string en) => Localization.T(zh, en);
    public static async Task Show(XamlRoot root, Account account)
    {
        var token = (await AccountStore.Shared.Login(account)).AccessToken;
        var body = new StackPanel { Spacing = 12 }; body.Children.Add(Ui.Text(account.Name, 18, FontWeights.Bold));
        MinecraftServices.NameChange? change = null;
        try { change = await MinecraftServices.GetNameChange(token); body.Children.Add(Ui.Text(change.NextChange is { } date ? T($"下次可改名：{date.ToLocalTime():yyyy-MM-dd}", $"Next name change: {date.ToLocalTime():yyyy-MM-dd}") : T("现在可以改名", "You can change your name now"), 12)); }
        catch (Exception e) { body.Children.Add(Ui.Text(e.Friendly(), 12)); }
        try
        {
            var ownership = await MinecraftServices.GetOwnership(token);
            body.Children.Add(Ui.Card(T("拥有的游戏", "Owned games"), Ui.Text((ownership.JavaPurchased ? T("Java 版（已购买）", "Java Edition (purchased)") : T("Java 版（Game Pass）", "Java Edition (Game Pass)")) + (ownership.Bedrock ? " · Bedrock" : "") + (ownership.Dungeons ? " · Dungeons" : "") + (ownership.Legends ? " · Legends" : ""), 12)));
        }
        catch (Exception e) { body.Children.Add(Ui.Text(e.Friendly(), 12)); }
        try
        {
            var attributes = await MinecraftServices.GetAttributes(token);
            var status = new List<UIElement>();
            foreach (var item in new[] { (T("多人游戏", "Multiplayer"), attributes.Multiplayer), ("Realms", attributes.Realms), (T("聊天", "Chat"), attributes.Chat) }) status.Add(Ui.Text(item.Item1 + (item.Item2 ? T("：允许", ": Allowed") : T("：禁用", ": Disabled")), 12));
            if (attributes.MultiplayerBan is { } ban) status.Add(Ui.Text(T("联机封禁：", "Multiplayer ban: ") + (ban.Expires is { } expires ? expires.ToLocalTime().ToString("yyyy-MM-dd") : T("永久", "Permanent")) + " " + ban.Reason, 12));
            body.Children.Add(Ui.Card(T("联机权限", "Online permissions"), status.ToArray()));
        }
        catch (Exception e) { body.Children.Add(Ui.Text(e.Friendly(), 12)); }
        var input = new TextBox { PlaceholderText = T("新名称", "New name") }; var hint = Ui.Text("", 12);
        body.Children.Add(input); body.Children.Add(hint);
        var generation = 0; var active = true; var available = false;
        KilnDialog? dialog = null;
        input.TextChanged += async (_, _) =>
        {
            var revision = ++generation; available = false; if (dialog != null) dialog.IsPrimaryButtonEnabled = false;
            var name = input.Text.Trim();
            if (!MinecraftServices.IsValidName(name)) { hint.Text = T("3–16 个字母、数字或下划线", "3–16 letters, digits or underscores"); return; }
            hint.Text = T("正在检查…", "Checking…");
            try
            {
                await Task.Delay(350); if (!active || revision != generation) return;
                var status = await MinecraftServices.NameStatusOf(name, token); if (!active || revision != generation) return;
                available = status == MinecraftServices.NameStatus.Available;
                hint.Text = available ? T("可以使用", "Available") : status == MinecraftServices.NameStatus.Taken ? T("已被使用", "Taken") : T("不可用", "Not allowed");
                if (dialog != null) dialog.IsPrimaryButtonEnabled = available && change?.Allowed == true;
            }
            catch (Exception e) { if (active && revision == generation) hint.Text = e.Friendly(); }
        };
        try
        {
            while (true)
            {
                dialog = new KilnDialog { XamlRoot = root, PreferredWidth = 580, Title = T("正版档案与改名", "Profile & name"), Content = body, PrimaryButtonText = T("改名…", "Change name…"), CloseButtonText = T("关闭", "Close"), IsPrimaryButtonEnabled = available && change?.Allowed == true };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                var newName = input.Text.Trim();
                var confirm = new KilnDialog { XamlRoot = root, Title = T("确认修改玩家名称", "Confirm player name change"), Content = account.Name + " → " + newName, PrimaryButtonText = T("确定改名", "Change name"), CloseButtonText = T("取消", "Cancel") };
                if (await confirm.ShowAsync() != ContentDialogResult.Primary) continue;
                try { var renamed = await MinecraftServices.ChangeName(newName, token); AccountStore.Shared.UpdateName(account, renamed); MainWindow.Current!.AccountChanged(); return; }
                catch (Exception e) { hint.Text = e.Friendly(); }
            }
        }
        finally { active = false; generation++; }
    }
}
