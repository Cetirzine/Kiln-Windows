using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;

namespace Kiln.App;

// HomePage.swift AccountChip / Avatar, shared by the scene HUD and every page header.
internal sealed class AccountChip : Button
{
    private readonly bool _onImage;
    private readonly AccountAvatar _avatar = new(22);
    private readonly TextBlock _name = new() { Name = "AccountName", FontSize = 12.5, FontWeight = FontWeights.Medium, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly FontIcon _pin = new() { Name = "AccountPinned", Glyph = "\uE718", FontSize = 9, Opacity = .75 };
    private readonly FontIcon _chevron = new() { Name = "AccountChevron", Glyph = "\uE70D", FontSize = 9, Opacity = .7 };
    private readonly Border _expired = new() { Name = "AccountNeedsLogin", Width = 6, Height = 6, Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 197, 58, 48)) };
    private MainWindow? _owner;
    private bool _hover;
    private bool _pressed;

    internal AccountChip(bool onImage = false, bool bindToStore = true)
    {
        _onImage = onImage; Name = "AccountChip";
        Style = (Style)Application.Current.Resources["KilnAccountButton"];
        Height = 32; MinHeight = 0; MinWidth = 0; MaxWidth = 270;
        Padding = new Thickness(5, 0, 9, 0); CornerRadius = new CornerRadius(3);
        BorderThickness = new Thickness(onImage ? 1 : 0);
        BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(36, 255, 255, 255));
        HorizontalAlignment = HorizontalAlignment.Right; VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(_avatar); Grid.SetColumn(_name, 1); row.Children.Add(_name);
        var markers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        markers.Children.Add(_pin); markers.Children.Add(_expired);
        markers.Children.Add(_chevron);
        Grid.SetColumn(markers, 2); row.Children.Add(markers); Content = row;
        ToolTipService.SetToolTip(_pin, Localization.T("该版本固定使用此账号", "This instance always uses this account"));
        ToolTipService.SetToolTip(_expired, Localization.T("需要重新登录", "Needs to sign in again"));
        Click += (_, _) => MainWindow.Current?.ShowAccounts(this);
        PointerEntered += (_, _) => { _hover = true; ApplyColors(); };
        PointerExited += (_, _) => { _hover = _pressed = false; ApplyColors(); };
        PointerPressed += (_, _) => { _pressed = true; ApplyColors(); };
        PointerReleased += (_, _) => { _pressed = false; ApplyColors(); };
        ActualThemeChanged += (_, _) => ApplyColors();
        Loaded += (_, _) => { if (bindToStore) { _owner = MainWindow.Current; if (_owner != null) _owner.AccountsUpdated += Refresh; Refresh(); } };
        Unloaded += (_, _) => { if (_owner != null) _owner.AccountsUpdated -= Refresh; _owner = null; };
        if (bindToStore) Refresh(); else SetAccount(null, false);
    }

    internal void Refresh()
    {
        var instance = InstanceScanner.Scan(LauncherSettings.Shared.MinecraftFolder).FirstOrDefault(i => i.Name == LauncherSettings.Shared.SelectedInstance);
        Refresh(instance);
    }
    internal void Refresh(McInstance? instance)
    {
        var store = AccountStore.Shared;
        SetAccount(instance == null ? store.Selected : store.AccountFor(instance.Setup), instance != null && store.AccountById(instance.Setup.Account) != null);
    }
    internal void SetAccount(Account? account, bool pinned)
    {
        _name.Text = account?.Name ?? Localization.T("添加账号", "Add account");
        _pin.Visibility = pinned ? Visibility.Visible : Visibility.Collapsed;
        _expired.Visibility = account?.NeedsLogin == true ? Visibility.Visible : Visibility.Collapsed;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, _name.Text);
        ToolTipService.SetToolTip(this, _name.Text);
        _avatar.Update(account); ApplyColors();
    }
    private void ApplyColors()
    {
        Foreground = _onImage ? new SolidColorBrush(Microsoft.UI.Colors.White) : Pages.Ui.Brush("KilnText");
        _name.Foreground = _pin.Foreground = _chevron.Foreground = Foreground;
        Background = _onImage ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(_pressed ? (byte)145 : (byte)115, 0, 0, 0))
            : _hover || _pressed ? Pages.Ui.Brush("KilnHover") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }
}

internal sealed class AccountAvatar : UserControl
{
    private readonly Image _image;
    private readonly FontIcon _placeholder;
    private readonly Border _frame = new() { CornerRadius = new CornerRadius(2) };
    private int _revision;
    internal Task ImageReady { get; private set; } = Task.CompletedTask;
    internal AccountAvatar(int size)
    {
        Name = "AccountAvatar"; Width = Height = size;
        _frame.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(89, 128, 128, 128));
        var grid = new Grid();
        _placeholder = new FontIcon { Glyph = "\uE77B", FontSize = 11, Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(204, 255, 255, 255)) };
        grid.Children.Add(_placeholder);
        _image = new Image { Stretch = Stretch.Fill }; grid.Children.Add(_image); _frame.Child = grid; Content = _frame;
        Unloaded += (_, _) => _revision++;
    }
    internal void Update(Account? account)
    {
        _image.Source = null; _placeholder.Visibility = Visibility.Visible;
        _frame.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(89, 128, 128, 128));
        var revision = ++_revision;
        ImageReady = Load();
        async Task Load()
        {
            try
            {
                var face = account == null ? null : await AccountAvatars.Get(account, (int)Width);
                if (revision != _revision) return;
                _image.Source = face;
                if (face != null) { _placeholder.Visibility = Visibility.Collapsed; _frame.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); }
            }
            catch { /* Keep the same placeholder as the original when a skin is unavailable. */ }
        }
    }
}

internal static class AccountAvatars
{
    private static readonly Dictionary<string, Task<WriteableBitmap?>> Cache = new();
    internal static void Invalidate() => Cache.Clear();
    internal static Task<WriteableBitmap?> Get(Account account, int size)
    {
        var file = account.Skin?.File;
        var key = $"{account.Id}|{account.Name}|{file}|{account.Skin?.Premium}|{size}";
        if (Cache.TryGetValue(key, out var existing) && !(existing.IsCompletedSuccessfully && existing.Result == null)) return existing;
        if (Cache.Count > 64) Cache.Clear();
        return Cache[key] = Load(account, size);
    }
    private static async Task<WriteableBitmap?> Load(Account account, int size)
    {
        try
        {
            byte[] bytes; var skin = account.Skin?.File != null || account.Kind == Account.AccountKind.Yggdrasil;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            if (account.Skin?.File is { } file) bytes = await File.ReadAllBytesAsync(Path.IsPathRooted(file) ? file : Skins.SkinPath(file), timeout.Token);
            else if (account.Kind == Account.AccountKind.Yggdrasil)
            {
                var root = account.Server?.TrimEnd('/') ?? throw new IOException("No skin server.");
                var profile = await Net.json(new[] { root + "/sessionserver/session/minecraft/profile/" + Uri.EscapeDataString(account.Uuid) }, timeout.Token);
                var encoded = profile["properties"].Array.FirstOrDefault(p => p["name"].String == "textures")["value"].String ?? throw new IOException("No skin texture.");
                var textures = Json.Parse(Convert.FromBase64String(encoded));
                var url = textures["textures"]["SKIN"]["url"].String ?? throw new IOException("No skin texture.");
                bytes = await Net.data(new[] { url.Replace("http://", "https://") }, ct: timeout.Token);
            }
            else
            {
                var id = account.Kind == Account.AccountKind.Microsoft ? account.Uuid : account.Skin?.Premium ?? account.Name;
                bytes = await Net.data(new[] { "https://mc-heads.net/avatar/" + Uri.EscapeDataString(id) + "/64" }, ct: timeout.Token);
            }
            return await Decode(bytes, skin, size);
        }
        catch { return null; }
    }
    internal static async Task<WriteableBitmap> Decode(byte[] bytes, bool skin, int size)
    {
        using var source = new MemoryStream(bytes);
        var decoder = await BitmapDecoder.CreateAsync(source.AsRandomAccessStream());
        var width = (int)decoder.PixelWidth; var height = (int)decoder.PixelHeight;
        if (skin && (width != 64 || height is not (32 or 64)) || !skin && (width == 0 || width != height || width > 512)) throw new IOException("Invalid avatar dimensions.");
        var input = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
        var faceSize = skin ? 8 : width; var face = new byte[faceSize * faceSize * 4];
        void Layer(int sx, int sy)
        {
            for (var y = 0; y < faceSize; y++) for (var x = 0; x < faceSize; x++)
            {
                var from = ((sy + y) * width + sx + x) * 4; var to = (y * faceSize + x) * 4; var alpha = input[from + 3];
                for (var c = 0; c < 3; c++) face[to + c] = (byte)((input[from + c] * alpha + face[to + c] * (255 - alpha)) / 255);
                face[to + 3] = (byte)(alpha + face[to + 3] * (255 - alpha) / 255);
            }
        }
        if (skin) { Layer(8, 8); Layer(40, 8); } else Layer(0, 0);
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++) Array.Copy(face, ((y * faceSize / size) * faceSize + x * faceSize / size) * 4, pixels, (y * size + x) * 4, 4);
        var bitmap = new WriteableBitmap(size, size); using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(pixels); bitmap.Invalidate(); return bitmap;
    }
}
