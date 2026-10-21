using Kiln.App.DotScene;
using System.Security.Cryptography;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Kiln.App;

internal static class ShortcutIcons
{
    internal static async Task<string> Create(string kind, string directory)
    {
        var frames = new List<(int Size, byte[] Png)>();
        foreach (var size in new[] { 16, 32, 48, 256 })
        {
            var pixels = await Task.Run(() => BlockIconControl.RenderIconPixels(kind, size));
            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, (uint)size, (uint)size, 96, 96, pixels);
            await encoder.FlushAsync();
            var png = new byte[(int)stream.Size];
            using var reader = new DataReader(stream.GetInputStreamAt(0)); await reader.LoadAsync((uint)png.Length); reader.ReadBytes(png);
            frames.Add((size, png));
        }
        using var ico = new MemoryStream();
        using (var writer = new BinaryWriter(ico, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)frames.Count);
            var offset = 6 + frames.Count * 16;
            foreach (var (size, png) in frames)
            {
                writer.Write(size == 256 ? (byte)0 : (byte)size); writer.Write(size == 256 ? (byte)0 : (byte)size);
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(png.Length); writer.Write(offset); offset += png.Length;
            }
            foreach (var frame in frames) writer.Write(frame.Png);
        }
        var bytes = ico.ToArray(); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + ".ico");
        if (!File.Exists(path))
        {
            var staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllBytesAsync(staging, bytes); try { File.Move(staging, path); } catch (IOException) when (File.Exists(path)) { } }
            finally { if (File.Exists(staging)) File.Delete(staging); }
        }
        return path;
    }
}
