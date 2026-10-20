using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using Kiln.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Kiln.App;

internal static class RemoteImages
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> InFlight = new();
    private static readonly SemaphoreSlim Slots = new(6);
    [DllImport("webp", CallingConvention = CallingConvention.Cdecl)] private static extern int WebPGetInfo(byte[] data, nuint size, out int width, out int height);
    [DllImport("webp", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr WebPDecodeBGRA(byte[] data, nuint size, out int width, out int height);
    [DllImport("webp", CallingConvention = CallingConvention.Cdecl)] private static extern void WebPFree(IntPtr buffer);

    public static async Task Set(Image image, string url)
    {
        try
        {
            image.Source = new WriteableBitmap(1, 1);
            var lazy = InFlight.GetOrAdd(url, key => new Lazy<Task<byte[]>>(() => Download(key)));
            byte[] bytes;
            try { bytes = await lazy.Value; }
            finally { InFlight.TryRemove(new KeyValuePair<string, Lazy<Task<byte[]>>>(url, lazy)); }
            if (bytes.Length > 12 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[8] == 'W' && bytes[9] == 'E')
            {
                var decoded = await Task.Run(() => DecodeWebp(bytes));
                var bitmap = new WriteableBitmap(decoded.Width, decoded.Height);
                using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(decoded.Pixels);
                bitmap.Invalidate(); image.Source = bitmap;
            }
            else
            {
                using var stream = new MemoryStream(bytes);
                var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream.AsRandomAccessStream()); image.Source = bitmap;
            }
        }
        catch (Exception e) { Log.log($"[Image] {new Uri(url).Host}: {e.Friendly()}"); }
    }
    private static async Task<byte[]> Download(string url)
    {
        var folder = Path.Combine(Paths.Cache, "images"); Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".image");
        if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromDays(7)) return await File.ReadAllBytesAsync(file);
        await Slots.WaitAsync();
        try
        {
            var bytes = await Net.data(new[] { url }, timeout: 20);
            if (bytes.Length > 16 * 1024 * 1024) throw new IOException("Image is too large.");
            var temporary = file + "." + Guid.NewGuid().ToString("N");
            await File.WriteAllBytesAsync(temporary, bytes); File.Move(temporary, file, true); return bytes;
        }
        finally { Slots.Release(); }
    }
    private static (byte[] Pixels, int Width, int Height) DecodeWebp(byte[] bytes)
    {
        if (WebPGetInfo(bytes, (nuint)bytes.Length, out var width, out var height) == 0 || width <= 0 || height <= 0 || (long)width * height > 16000000) throw new IOException("Invalid WebP image.");
        var pointer = WebPDecodeBGRA(bytes, (nuint)bytes.Length, out width, out height);
        if (pointer == IntPtr.Zero) throw new IOException("WebP decoding failed.");
        try
        {
            var pixels = new byte[checked(width * height * 4)]; Marshal.Copy(pointer, pixels, 0, pixels.Length);
            // WriteableBitmap uses premultiplied BGRA.
            for (var p = 0; p < pixels.Length; p += 4)
            {
                var alpha = pixels[p + 3]; if (alpha == 255) continue;
                pixels[p] = (byte)(pixels[p] * alpha / 255); pixels[p + 1] = (byte)(pixels[p + 1] * alpha / 255); pixels[p + 2] = (byte)(pixels[p + 2] * alpha / 255);
            }
            return (pixels, width, height);
        }
        finally { WebPFree(pointer); }
    }
}
