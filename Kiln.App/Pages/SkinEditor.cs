using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

internal static class SkinEditor
{
    private static string T(string zh, string en) => Localization.T(zh, en);
    public static async Task Show(XamlRoot root, Account account)
    {
        var microsoft = account.Kind == Account.AccountKind.Microsoft;
        var mode = Ui.Combo(new[] { T("默认皮肤", "Default skin"), T("本地皮肤", "Local skin"), T("正版玩家皮肤", "Premium player's skin") }, account.Skin?.Premium != null ? 2 : account.Skin?.File != null ? 1 : 0, _ => { });
        var model = Ui.Toggle(account.Skin?.Slim == true, "Alex", "Steve", _ => { });
        var player = new TextBox { PlaceholderText = T("正版玩家名称", "Premium player name"), Text = account.Skin?.Premium ?? "", MinWidth = 0 };
        var preview = new Image { Width = 96, Height = 192, HorizontalAlignment = HorizontalAlignment.Left };
        var status = Ui.Text("", 11.5); var library = new FlowRow { Spacing = 8 };
        string? local = account.Skin?.File is { } file ? Skins.SkinPath(file) : null;
        string? token = null; byte[]? original = null; Skins.MinecraftProfile? profile = null;
        var cape = new ComboBox { MinWidth = 220 }; cape.Items.Add(T("不显示披风", "No cape")); cape.SelectedIndex = 0;
        var body = new StackPanel { Spacing = 12 };
        async Task Preview()
        {
            try { if (local != null && File.Exists(local)) preview.Source = await SkinPreview.Front(await File.ReadAllBytesAsync(local), model.IsOn); }
            catch (Exception e) { status.Text = e.Friendly(); }
        }
        model.Toggled += async (_, _) => await Preview();
        async Task Choose()
        {
            var picker = new KilnFileOpenPicker(); picker.FileTypeFilter.Add(".png"); var selected = await picker.PickSingleFileAsync(); if (selected == null) return;
            try { var data = await File.ReadAllBytesAsync(selected.Path); Skins.Validate(data); local = selected.Path; mode.SelectedIndex = 1; await Preview(); }
            catch (Exception e) { status.Text = e.Friendly(); }
        }
        async Task FetchPlayer()
        {
            try { var skin = await Skins.Premium(player.Text.Trim()); local = skin.Url; model.IsOn = skin.Slim; mode.SelectedIndex = 2; await Preview(); status.Text = ""; }
            catch (Exception e) { status.Text = e.Friendly(); }
        }
        var loadPlayer = new Grid { ColumnSpacing = 8 }; loadPlayer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); loadPlayer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); loadPlayer.Children.Add(player);
        var fetch = Ui.Button(T("加载", "Load"), async (_, _) => await FetchPlayer()); Grid.SetColumn(fetch, 1); loadPlayer.Children.Add(fetch);
        body.Children.Add(Ui.Row(preview, Ui.Col(Ui.Text(account.Name, 16, FontWeights.Bold), Ui.Button(T("选择皮肤文件…", "Choose skin file…"), async (_, _) => await Choose()), Ui.Setting(T("纤细模型（Alex）", "Slim model (Alex)"), model))));
        if (!microsoft) body.Children.Add(mode);
        body.Children.Add(loadPlayer);
        if (microsoft) body.Children.Add(Ui.Setting(T("披风", "Cape"), cape));
        body.Children.Add(Ui.Text(T("皮肤库", "Skin library"), 12, FontWeights.SemiBold)); body.Children.Add(library); body.Children.Add(status);
        foreach (var saved in Skins.Library())
        {
            var image = new Image { Width = 24, Height = 48 };
            async Task LoadThumb() { try { image.Source = await SkinPreview.Front(await File.ReadAllBytesAsync(saved.Url), saved.Slim, 2); } catch { } }
            _ = LoadThumb();
            var button = new Button { Content = image, Width = 40, Height = 60, Padding = new Thickness(4) }; ToolTipService.SetToolTip(button, saved.Label);
            button.Click += async (_, _) => { local = saved.Url; model.IsOn = saved.Slim; mode.SelectedIndex = 1; await Preview(); };
            var menu = new MenuFlyout(); var remove = new MenuFlyoutItem { Text = T("从皮肤库移除", "Remove from library") }; remove.Click += (_, _) => { Skins.RemoveFromLibrary(saved); library.Children.Remove(button); }; menu.Items.Add(remove); button.ContextFlyout = menu; library.Children.Add(button);
        }
        if (microsoft)
        {
            try
            {
                token = (await AccountStore.Shared.Login(account)).AccessToken; profile = await Skins.Profile(token);
                foreach (var item in profile.Capes) { cape.Items.Add(item.Alias); if (item.Active) cape.SelectedIndex = cape.Items.Count - 1; }
                if (profile.SkinUrl is { } url) { original = await Net.data(new[] { url }); preview.Source = await SkinPreview.Front(original, profile.Slim); model.IsOn = profile.Slim; }
            }
            catch (Exception e) { status.Text = e.Friendly(); }
            body.Children.Add(Ui.Button(T("恢复默认皮肤", "Reset skin"), async (_, _) =>
            {
                try { token ??= (await AccountStore.Shared.Login(account)).AccessToken; if (original != null) Skins.AddToLibrary(original, profile?.Slim ?? false, account.Name); await Skins.Reset(token); status.Text = T("已恢复默认皮肤", "Skin reset"); }
                catch (Exception e) { status.Text = e.Friendly(); }
            }));
        }
        else await Preview();
        while (true)
        {
            var dialog = new KilnDialog { XamlRoot = root, PreferredWidth = 620, Title = T("皮肤 — ", "Skin — ") + account.Name, Content = body, PrimaryButtonText = T("应用", "Apply"), CloseButtonText = T("取消", "Cancel") };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            try
            {
                if (microsoft)
                {
                    token ??= (await AccountStore.Shared.Login(account)).AccessToken;
                    if (local != null)
                    {
                        if (original != null) Skins.AddToLibrary(original, profile?.Slim ?? false, account.Name);
                        await Skins.Upload(token, local, model.IsOn); Skins.AddToLibrary(await File.ReadAllBytesAsync(local), model.IsOn, account.Name);
                    }
                    var id = cape.SelectedIndex <= 0 ? null : profile?.Capes[cape.SelectedIndex - 1].Id;
                    await Skins.SetCape(token, id);
                }
                else
                {
                    Account.OfflineSkin? skin = null;
                    if (mode.SelectedIndex == 2)
                    {
                        var premium = player.Text.Trim(); if (premium.Length == 0) throw new LauncherException(T("请输入正版玩家名称", "Enter a premium player name"));
                        var remote = await Skins.Premium(premium); skin = new Account.OfflineSkin { Premium = premium, Slim = remote.Slim };
                    }
                    else if (mode.SelectedIndex == 1)
                    {
                        if (local == null) throw new LauncherException(T("请选择皮肤文件", "Choose a skin file"));
                        var bytes = await File.ReadAllBytesAsync(local); var saved = Skins.AddToLibrary(bytes, model.IsOn, account.Name); skin = new Account.OfflineSkin { File = saved.File, Slim = model.IsOn };
                    }
                    AccountStore.Shared.SetSkin(account, skin);
                }
                return;
            }
            catch (Exception e) { status.Text = e.Friendly(); }
        }
    }
}
