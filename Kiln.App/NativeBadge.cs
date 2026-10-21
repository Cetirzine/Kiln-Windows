using System.Runtime.InteropServices;

namespace Kiln.App;

internal static class NativeBadge
{
    internal static IntPtr Create(int count)
    {
        var pixels = new byte[16 * 16 * 4];
        for (var y = 1; y < 15; y++) for (var x = 1; x < 15; x++) { var i = (y * 16 + x) * 4; pixels[i] = 207; pixels[i + 1] = 73; pixels[i + 2] = 36; pixels[i + 3] = 255; }
        var text = count > 99 ? "99+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var scale = text.Length > 2 ? 1 : 2; var left = (16 - (text.Length * 4 - 1) * scale) / 2; var top = (16 - 5 * scale) / 2;
        foreach (var digit in text)
        {
            var bits = digit switch { '0' => "111101101101111", '1' => "010110010010111", '2' => "111001111100111", '3' => "111001111001111", '4' => "101101111001001", '5' => "111100111001111", '6' => "111100111101111", '7' => "111001001001001", '8' => "111101111101111", '9' => "111101111001111", _ => "000010111010000" };
            for (var i = 0; i < bits.Length; i++) if (bits[i] == '1')
                for (var dy = 0; dy < scale; dy++) for (var dx = 0; dx < scale; dx++) { var p = ((top + i / 3 * scale + dy) * 16 + left + i % 3 * scale + dx) * 4; pixels[p] = pixels[p + 1] = pixels[p + 2] = pixels[p + 3] = 255; }
            left += 4 * scale;
        }
        var header = new BitmapHeader { Size = 40, Width = 16, Height = -16, Planes = 1, Bits = 32 };
        var color = CreateDIBSection(IntPtr.Zero, ref header, 0, out var data, IntPtr.Zero, 0);
        if (color == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var mask = IntPtr.Zero;
        try
        {
            Marshal.Copy(pixels, 0, data, pixels.Length); mask = CreateBitmap(16, 16, 1, 1, new byte[32]);
            if (mask == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var info = new IconInfo { IsIcon = true, Color = color, Mask = mask }; var icon = CreateIconIndirect(ref info);
            if (icon == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return icon;
        }
        finally { if (mask != IntPtr.Zero) DeleteObject(mask); DeleteObject(color); }
    }
    internal static void Destroy(IntPtr icon) => DestroyIcon(icon);
    [StructLayout(LayoutKind.Sequential)] private struct BitmapHeader { public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize; public int Xppm, Yppm; public uint ColorsUsed, Important; }
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { [MarshalAs(UnmanagedType.Bool)] public bool IsIcon; public uint X, Y; public IntPtr Mask, Color; }
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapHeader info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bits, byte[] data);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr bitmap);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CreateIconIndirect(ref IconInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
}
