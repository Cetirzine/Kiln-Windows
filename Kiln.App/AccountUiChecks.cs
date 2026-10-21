using Kiln.Core;
using Kiln.Core.DotScene;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;

namespace Kiln.App;

internal static class AccountUiChecks
{
    internal static FrameworkElement Preview(string fixtureRoot)
    {
        var account = new Account { Name = "Pixel_Player", Uuid = Account.OfflineUuid("Pixel_Player"), Kind = Account.AccountKind.Offline, Skin = new() { File = Path.Combine(fixtureRoot, "account-avatar.png") } };
        var content = new StackPanel { Spacing = 16, Padding = new Thickness(28, 48, 28, 24) };
        var plain = new AccountChip(bindToStore: false); plain.SetAccount(account, false);
        var header = new Grid(); header.Children.Add(Pages.Ui.Text("账号入口与 26.3 场景检查", 21, FontWeights.Bold)); header.Children.Add(plain); content.Children.Add(header);
        var hero = new Grid { Height = 350 };
        hero.Children.Add(new DotScene.DotSceneControl { Scene = new WildernessBound() });
        var hud = new AccountChip(onImage: true, bindToStore: false) { Margin = new Thickness(0, 18, 22, 0), VerticalAlignment = VerticalAlignment.Top }; hud.SetAccount(account, true); hero.Children.Add(hud);
        content.Children.Add(hero);
        var empty = new AccountChip(bindToStore: false); content.Children.Add(empty);
        return content;
    }

    internal static async Task Run(MainWindow window, string fixtureRoot, Action<string, bool> check)
    {
        var pixels = new byte[64 * 64 * 4];
        void Fill(int sx, int sy, int width, int height, byte r, byte g, byte b, byte a)
        {
            for (var y = sy; y < sy + height; y++) for (var x = sx; x < sx + width; x++)
            { var i = (y * 64 + x) * 4; pixels[i] = b; pixels[i + 1] = g; pixels[i + 2] = r; pixels[i + 3] = a; }
        }
        Fill(8, 8, 8, 8, 155, 90, 55, 255); Fill(8, 8, 8, 2, 52, 35, 22, 255);
        Fill(9, 11, 2, 1, 30, 90, 215, 255); Fill(13, 11, 2, 1, 30, 90, 215, 255);
        Fill(11, 14, 2, 1, 85, 38, 29, 255); Fill(40, 8, 2, 2, 255, 255, 255, 128);
        var skin = Path.Combine(fixtureRoot, "account-avatar.png");
        using (var stream = File.Create(skin))
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream.AsRandomAccessStream());
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, 64, 64, 96, 96, pixels); await encoder.FlushAsync();
        }
        var account = new Account { Name = "Pixel_Player", Uuid = Account.OfflineUuid("Pixel_Player"), Kind = Account.AccountKind.Offline, Skin = new() { File = skin }, NeedsLogin = true };
        var home = new AccountChip(onImage: true, bindToStore: false); var header = new AccountChip(bindToStore: false);
        var panel = new StackPanel { Spacing = 20, Padding = new Thickness(28, 48, 28, 24), Background = Pages.Ui.Brush("KilnPanel") };
        panel.Children.Add(home); panel.Children.Add(header); window.ShowDiagnostics(panel);
        await Task.Delay(60); panel.UpdateLayout();
        var emptyLabel = Descendants(header).OfType<TextBlock>().Single(t => t.Name == "AccountName");
        check("Signed-out account chip retains avatar, label and chevron", emptyLabel.Text == Localization.T("添加账号", "Add account") && Descendants(header).OfType<AccountAvatar>().Single().ActualWidth == 22 && Descendants(header).OfType<FontIcon>().Any(f => f.Name == "AccountChevron"));
        home.SetAccount(account, true); header.SetAccount(account, true);
        await Task.WhenAll(Descendants(home).OfType<AccountAvatar>().Select(a => a.ImageReady).Concat(Descendants(header).OfType<AccountAvatar>().Select(a => a.ImageReady)));
        panel.UpdateLayout();
        check("Signed-in chip shows a skin face, player name, pin and expired marker", Descendants(home).OfType<Image>().Any(i => i.Source is WriteableBitmap) && Descendants(home).OfType<TextBlock>().Any(t => t.Text == account.Name) && Descendants(home).OfType<FontIcon>().Single(f => f.Name == "AccountPinned").Visibility == Visibility.Visible && Descendants(home).OfType<Border>().Single(b => b.Name == "AccountNeedsLogin").Visibility == Visibility.Visible);
        check("Scene and page account chips share 32pt height and right edge", home.ActualHeight == 32 && header.ActualHeight == 32 && Math.Abs(home.TransformToVisual(panel).TransformPoint(new Windows.Foundation.Point(home.ActualWidth, 0)).X - header.TransformToVisual(panel).TransformPoint(new Windows.Foundation.Point(header.ActualWidth, 0)).X) < .5);
        check("Scene account chip uses original white text and 45 percent black glass", home.Foreground is SolidColorBrush fg && fg.Color == Microsoft.UI.Colors.White && home.Background is SolidColorBrush bg && bg.Color.A == 115 && bg.Color.R == 0 && home.BorderBrush is SolidColorBrush edge && edge.Color.A == 36 && home.CornerRadius.TopLeft == 3);
        var bitmap = await AccountAvatars.Decode(await File.ReadAllBytesAsync(skin), true, 22); var actual = bitmap.PixelBuffer.ToArray();
        check("Avatar composites the hat and scales pixels without interpolation", actual[0] == 138 && actual[1] == 145 && actual[2] == 153 && actual[3] == 255 && actual[(10 * 22 + 10) * 4] == 55);
        account.NeedsLogin = false; home.SetAccount(account, false);
        check("Account selection clears outdated pin and login markers", Descendants(home).OfType<FontIcon>().Single(f => f.Name == "AccountPinned").Visibility == Visibility.Collapsed && Descendants(home).OfType<Border>().Single(b => b.Name == "AccountNeedsLogin").Visibility == Visibility.Collapsed);
        var oldSize = window.AppWindow.Size; var oldLanguage = Localizer.Shared.Language;
        try
        {
            account.Name = "A_very_long_player_display_name_for_layout";
            foreach (var language in new[] { AppLanguage.ZhHans, AppLanguage.ZhHant, AppLanguage.En })
            foreach (var (width, height) in new[] { (920, 620), (1040, 680), (1200, 800) })
            {
                Localizer.Shared.Language = language;
                var scale = ((FrameworkElement)window.Content).XamlRoot.RasterizationScale;
                window.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(height * scale)));
                var title = Pages.Ui.Header(Localization.T("版本", "Instances"), Pages.Ui.Button(Localization.T("切换文件夹…", "Game folders…"), (_, _) => { }), Pages.Ui.Button(Localization.T("导入…", "Import…"), (_, _) => { }), Pages.Ui.Button(Localization.T("安装新版本", "Install a version"), (_, _) => { }));
                window.ShowDiagnostics(new Border { Padding = new Thickness(28, 48, 28, 24), Child = title });
                await Task.Delay(40); ((FrameworkElement)window.Content).UpdateLayout();
                var chip = Descendants(title).OfType<AccountChip>().Single(); chip.SetAccount(account, true); title.UpdateLayout();
                var actions = Descendants(title).OfType<StackPanel>().Single(p => p.Name == "PageHeaderActions");
                double Center(FrameworkElement element) => element.TransformToVisual(title).TransformPoint(new Windows.Foundation.Point(0, element.ActualHeight / 2)).Y;
                var children = actions.Children.OfType<FrameworkElement>().ToArray();
                var sameRow = children.All(e => Math.Abs(Center(e) - Center(chip)) < .5);
                var right = chip.TransformToVisual(title).TransformPoint(new Windows.Foundation.Point(chip.ActualWidth, 0)).X;
                var avatar = Descendants(chip).OfType<AccountAvatar>().Single(); var name = Descendants(chip).OfType<TextBlock>().Single(t => t.Name == "AccountName");
                check($"Version header keeps actions, avatar and ID on one row at {width}x{height} ({language})", sameRow && Math.Abs(right - title.ActualWidth) < .5 && Math.Abs(Center(avatar) - Center(name)) < .5 && chip.ActualHeight == 32);
            }
        }
        finally { Localizer.Shared.Language = oldLanguage; window.AppWindow.Resize(oldSize); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
}
