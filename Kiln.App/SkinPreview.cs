using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Kiln.App;

internal static class SkinPreview
{
    // Direct port of Swift SkinRender.front: classic/legacy base and outer layers.
    public static async Task<WriteableBitmap> Front(byte[] bytes, bool slim, int scale = 6)
    {
        using var source = new MemoryStream(bytes);
        var decoder = await BitmapDecoder.CreateAsync(source.AsRandomAccessStream());
        if (decoder.PixelWidth != 64 || decoder.PixelHeight is not (32 or 64)) throw new IOException("Invalid skin dimensions.");
        var input = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
        var target = new byte[16 * 32 * 4];
        void Draw(int sx, int sy, int w, int h, int dx, int dy, bool mirror = false)
        {
            for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
            {
                var si = ((sy + y) * 64 + sx + (mirror ? w - x - 1 : x)) * 4; var di = ((dy + y) * 16 + dx + x) * 4;
                var alpha = input[si + 3];
                for (var c = 0; c < 3; c++) target[di + c] = (byte)((input[si + c] * alpha + target[di + c] * (255 - alpha)) / 255);
                target[di + 3] = (byte)(alpha + target[di + 3] * (255 - alpha) / 255);
            }
        }
        var arm = slim ? 3 : 4; var legacy = decoder.PixelHeight == 32;
        Draw(8, 8, 8, 8, 4, 0); Draw(20, 20, 8, 12, 4, 8); Draw(44, 20, arm, 12, 4 - arm, 8);
        if (legacy) Draw(44, 20, arm, 12, 12, 8, true); else Draw(36, 52, arm, 12, 12, 8);
        Draw(4, 20, 4, 12, 4, 20); if (legacy) Draw(4, 20, 4, 12, 8, 20, true); else Draw(20, 52, 4, 12, 8, 20);
        Draw(40, 8, 8, 8, 4, 0);
        if (!legacy) { Draw(20, 36, 8, 12, 4, 8); Draw(44, 36, arm, 12, 4 - arm, 8); Draw(52, 52, arm, 12, 12, 8); Draw(4, 36, 4, 12, 4, 20); Draw(4, 52, 4, 12, 8, 20); }
        var pixels = new byte[16 * scale * 32 * scale * 4];
        for (var y = 0; y < 32 * scale; y++) for (var x = 0; x < 16 * scale; x++) Array.Copy(target, ((y / scale) * 16 + x / scale) * 4, pixels, (y * 16 * scale + x) * 4, 4);
        var bitmap = new WriteableBitmap(16 * scale, 32 * scale); using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(pixels); bitmap.Invalidate(); return bitmap;
    }
}
