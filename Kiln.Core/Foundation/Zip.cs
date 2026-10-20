using System.IO.Compression;
using System.Text;

namespace Kiln.Core;

/// 基于 System.IO.Compression 的 zip / jar 读取与解压（对应原版的 unzip / ditto 封装）
public static class Zip
{
    /// 解压整个压缩包；含绝对路径或 ../ 的条目会被清理（对应原版改用 unzip 的兜底行为）
    public static void ExtractAll(string archive, string dest)
    {
        Directory.CreateDirectory(dest);
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            var target = SafePath(dest, entry.FullName);
            if (string.IsNullOrEmpty(target)) continue;
            if (entry.FullName.EndsWith('/') || string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    /// 去掉绝对路径、盘符与 .. 段；全部不安全时返回 null
    private static string? SafePath(string dest, string name)
    {
        var segments = name.Split('/', '\\').Where(s => s.Length > 0 && s != "." && s != "..").ToList();
        if (segments.Count == 0) return null;
        if (segments[0].Length == 2 && segments[0][1] == ':') segments.RemoveAt(0);
        if (segments.Count == 0) return null;
        return Path.Combine(new[] { dest }.Concat(segments).ToArray());
    }

    /// 读取压缩包中的单个文件
    public static byte[]? Read(string archive, string entryName)
    {
        try
        {
            using var zip = ZipFile.OpenRead(archive);
            var entry = zip.GetEntry(entryName);
            if (entry == null) return null;
            using var ms = new MemoryStream();
            using var es = entry.Open();
            es.CopyTo(ms);
            return ms.Length == 0 ? null : ms.ToArray();
        }
        catch { return null; }
    }

    /// 解压单个文件到指定位置
    public static void Extract(string archive, string entry, string dest)
    {
        var d = Read(archive, entry)
            ?? throw new LauncherException(T.Invoke($"压缩包中缺少 {entry}", $"{entry} is missing from the archive"));
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.WriteAllBytes(dest, d);
    }

    /// 列出压缩包中的文件
    public static List<string> List(string archive)
    {
        try
        {
            using var zip = ZipFile.OpenRead(archive);
            return zip.Entries.Select(e => e.FullName).ToList();
        }
        catch { return new(); }
    }

    /// 读取 jar 的 Main-Class
    public static string? MainClass(string jar)
    {
        var d = Read(jar, "META-INF/MANIFEST.MF");
        if (d == null) return null;
        foreach (var raw in Encoding.UTF8.GetString(d).Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("Main-Class:", StringComparison.Ordinal))
                return line["Main-Class:".Length..].Trim();
        }
        return null;
    }
}
