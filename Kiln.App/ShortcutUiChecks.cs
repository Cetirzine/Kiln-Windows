using Kiln.Core;
using System.Runtime.InteropServices;

namespace Kiln.App;

internal static class ShortcutUiChecks
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr instance, string path, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    internal static async Task Run(McInstance instance, string fixtureRoot, Action<string, bool> check)
    {
        var desktop = Path.Combine(fixtureRoot, "test-desktop"); var icons = Path.Combine(fixtureRoot, "test-icons");
        Directory.CreateDirectory(desktop);
        var existing = Path.Combine(desktop, "Kiln - " + instance.Name + ".lnk"); File.WriteAllText(existing, "Existing shortcut must be preserved");
        var first = await SystemIntegration.CreateShortcutFile(instance, desktop, icons);
        var second = await SystemIntegration.CreateShortcutFile(instance, desktop, icons);
        check("Desktop shortcuts reserve unique names without overwriting files", first != second && File.ReadAllText(existing) == "Existing shortcut must be preserved");
        var link = ShellShortcuts.Read(first);
        check("Native shell link targets Kiln and preserves the complete launch URI", string.Equals(link.Target, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase) && link.Arguments == "\"" + LaunchLink.Create(instance.Name) + "\"");
        check("Shortcut carries the production launcher taskbar identity", link.AppId == ShellShortcuts.AppId);
        check("Desktop link uses a persistent instance icon", link.Icon.StartsWith(icons + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(link.Icon) && ShellShortcuts.Read(second).Icon == link.Icon);
        foreach (var size in new[] { 16, 32, 48, 256 })
        {
            var icon = LoadImage(IntPtr.Zero, link.Icon, 1, size, size, 0x10);
            if (icon == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), $"Windows could not load {size}px shortcut icon");
            DestroyIcon(icon);
        }
        check("Windows loads instance icons at 16, 32, 48 and 256 pixels", true);
        var grass = await ShortcutIcons.Create("Grass", icons); var kiln = await ShortcutIcons.Create("Kiln", icons);
        check("Different instance block icons remain distinct", grass != kiln);
    }
}
