using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Kiln.Core;

// ZipPath.toUri in affected Forge libraries cannot round-trip brackets in Windows paths.
// Junctions expose the same files under stable paths; settings and instance identity stay original.
internal sealed class GamePathAliases
{
    private readonly List<(string Source, string Alias)> _paths = new();
    internal bool Active => _paths.Count > 0;
    internal static bool NeedsAlias(McInstance instance) =>
        (instance.Loaders.Contains("Forge") || instance.Loaders.Contains("NeoForge")) &&
        (HasBrackets(instance.Folder) || HasBrackets(instance.Dir));
    private static bool HasBrackets(string path) => path.IndexOfAny(['[', ']']) >= 0;

    internal static GamePathAliases Prepare(McInstance instance, string? aliasHome = null)
    {
        var result = new GamePathAliases();
        if (!OperatingSystem.IsWindows() || !NeedsAlias(instance)) return result;
        aliasHome ??= Path.Combine(Paths.Support, "PathAliases");
        aliasHome = Path.GetFullPath(aliasHome);
        if (HasBrackets(aliasHome)) throw new LauncherException(T.Invoke(
            "Forge 路径兼容目录也包含方括号，无法建立可用的启动路径。", "The Forge compatibility directory also contains brackets; a usable launch path could not be created."));
        Directory.CreateDirectory(aliasHome);
        void Add(string source)
        {
            source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
            if (result._paths.Any(p => p.Source.Equals(source, StringComparison.OrdinalIgnoreCase))) return;
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.ToUpperInvariant())))[..24].ToLowerInvariant();
            var alias = Path.Combine(aliasHome, "game-" + key);
            EnsureJunction(alias, source);
            result._paths.Add((source, alias));
        }
        // More specific mappings go first, including isolated instances with bracketed names.
        if (HasBrackets(instance.Name)) { Add(instance.Dir); Add(instance.NativesDir); }
        if (HasBrackets(instance.Folder)) Add(instance.Folder);
        result._paths.Sort((a, b) => b.Source.Length.CompareTo(a.Source.Length));
        return result;
    }

    internal string Apply(string value)
    {
        // Do not replace a neighbouring directory whose name merely shares a prefix.
        foreach (var (source, alias) in _paths)
        {
            value = Replace(value, source, alias);
            value = Replace(value, source.Replace('\\', '/'), alias.Replace('\\', '/'));
        }
        return value;
    }
    internal List<string> Apply(IReadOnlyList<string> arguments)
    {
        var result = arguments.Select(Apply).ToList();
        if (Active)
            for (var i = 0; i < result.Count; i++)
            {
                const string prefix = "-XX:SharedArchiveFile=";
                if (!result[i].StartsWith(prefix, StringComparison.Ordinal)) continue;
                var file = result[i][prefix.Length..];
                // Keep a separate generated cache from earlier launches using the original classpath.
                if (Path.GetFileName(file).StartsWith("cds-", StringComparison.Ordinal) && file.EndsWith(".jsa", StringComparison.Ordinal))
                    result[i] = prefix + Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file) + "-winpath.jsa");
            }
        return result;
    }
    private static string Replace(string value, string source, string alias)
    {
        var output = new StringBuilder(); var position = 0;
        while (value.IndexOf(source, position, StringComparison.OrdinalIgnoreCase) is var found && found >= 0)
        {
            var end = found + source.Length;
            var boundary = end == value.Length || value[end] is '\\' or '/' or ';';
            output.Append(value, position, found - position);
            output.Append(boundary ? alias : value.Substring(found, source.Length));
            position = end;
        }
        return output.Append(value, position, value.Length - position).ToString();
    }

    internal static void EnsureJunction(string alias, string target)
    {
        target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        if (!Directory.Exists(target)) throw new DirectoryNotFoundException(target);
        if (File.Exists(alias) || Directory.Exists(alias) || new DirectoryInfo(alias).LinkTarget != null)
        {
            var resolved = new DirectoryInfo(alias).ResolveLinkTarget(false)?.FullName;
            if (resolved == null || !Path.TrimEndingDirectorySeparator(resolved).Equals(target, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The game path alias is occupied by another file or directory: " + alias);
            return;
        }
        Directory.CreateDirectory(alias);
        try
        {
            var substitute = Encoding.Unicode.GetBytes((target.StartsWith(@"\\", StringComparison.Ordinal) ? @"\??\UNC\" + target[2..] : @"\??\" + target) + '\0');
            var print = Encoding.Unicode.GetBytes(target + '\0');
            var data = new byte[16 + substitute.Length + print.Length];
            BitConverter.TryWriteBytes(data.AsSpan(0, 4), 0xA0000003u); // IO_REPARSE_TAG_MOUNT_POINT
            BitConverter.TryWriteBytes(data.AsSpan(4, 2), checked((ushort)(data.Length - 8)));
            BitConverter.TryWriteBytes(data.AsSpan(10, 2), checked((ushort)(substitute.Length - 2)));
            BitConverter.TryWriteBytes(data.AsSpan(12, 2), checked((ushort)substitute.Length));
            BitConverter.TryWriteBytes(data.AsSpan(14, 2), checked((ushort)(print.Length - 2)));
            substitute.CopyTo(data, 16); print.CopyTo(data, 16 + substitute.Length);
            using var handle = CreateFile(alias, 0x40000000, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            if (handle.IsInvalid || !DeviceIoControl(handle, 0x000900A4, data, data.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the game path junction.");
        }
        catch
        {
            // Non-recursive removal of this newly created entry never traverses its target.
            try { Directory.Delete(alias); } catch { }
            throw;
        }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputSize, IntPtr output, int outputSize, out uint returned, IntPtr overlapped);
}
