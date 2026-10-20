using System.IO.Compression;

namespace Kiln.Core;

/// 版本管理：重命名、复制；存档的导入与导出
public static class InstanceManager
{
    /// 检查新名称，返回错误信息
    public static string? ValidateName(string name, string folder)
    {
        var n = name.Trim();
        if (n.Length == 0) return T.Invoke("请输入名称", "Enter a name");
        if (n.IndexOfAny("/\\:!*?\"<>|".ToCharArray()) >= 0 || n.StartsWith('.'))
            return T.Invoke("名称不能包含 / \\ : ! * ? \" < > | 或以 . 开头",
                "The name can't contain / \\ : ! * ? \" < > | or start with .");
        if (Directory.Exists(Path.Combine(folder, "versions", n)))
            return T.Invoke($"已存在名为 {n} 的版本", $"An instance named {n} already exists");
        return null;
    }

    /// 依赖这个版本的其他版本（inheritsFrom）
    public static List<string> DependentsOf(McInstance instance)
    {
        var versions = Path.Combine(instance.Folder, "versions");
        var result = new List<string>();
        string[] names;
        try { names = Directory.GetDirectories(versions).Select(d => Path.GetFileName(d)!).ToArray(); }
        catch { return result; }
        foreach (var n in names)
        {
            if (n == instance.Name) continue;
            try
            {
                var j = Json.Parse(File.ReadAllBytes(Path.Combine(versions, n, $"{n}.json")));
                if (j["inheritsFrom"].String == instance.Name) result.Add(n);
            }
            catch { }
        }
        return result;
    }

    /// 重命名
    public static void Rename(McInstance instance, string newName)
    {
        if (ValidateName(newName, instance.Folder) is { } e) throw new LauncherException(e);
        var deps = DependentsOf(instance);
        if (deps.Count > 0)
            throw new LauncherException(T.Invoke($"{string.Join("、", deps)} 依赖这个版本，不能重命名",
                $"{string.Join(", ", deps)} depend on this instance, so it can't be renamed"));
        var target = Path.Combine(instance.Folder, "versions", newName);
        Directory.Move(instance.Dir, target);
        RenameContents(target, instance.Name, newName);
        if (LauncherSettings.Shared.SelectedInstance == instance.Name)
            LauncherSettings.Shared.SelectedInstance = newName;
    }

    /// 复制为新版本
    public static void Duplicate(McInstance instance, string newName)
    {
        if (ValidateName(newName, instance.Folder) is { } e) throw new LauncherException(e);
        var target = Path.Combine(instance.Folder, "versions", newName);
        CopyDirectory(instance.Dir, target);
        RenameContents(target, instance.Name, newName);
        // 新版本从未启动过
        var setup = LauncherSettings.Shared.InstanceSetupOf(target);
        setup.LastPlayed = null;
        LauncherSettings.Shared.SaveInstanceSetup(setup, target);
    }

    private static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(src))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(src))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    /// 版本文件夹内与名称相关的文件：<名称>.json（及其中的 id）、<名称>.jar、natives
    private static void RenameContents(string dir, string old, string @new)
    {
        var oldJson = Path.Combine(dir, $"{old}.json");
        var newJson = Path.Combine(dir, $"{@new}.json");
        var j = new Dictionary<string, Json>(Json.Parse(File.ReadAllBytes(oldJson)).Object)
        {
            ["id"] = Json.Of(@new),
        };
        File.WriteAllBytes(newJson, Json.From(j).Data());
        File.Delete(oldJson);
        var oldJar = Path.Combine(dir, $"{old}.jar");
        if (File.Exists(oldJar)) File.Move(oldJar, Path.Combine(dir, $"{@new}.jar"));
        // natives 会在启动时重新解压
        try { Directory.Delete(Path.Combine(dir, $"{old}-natives"), recursive: true); } catch { }
        // 类数据共享缓存与类路径相关，改名后失效
        var kilnDir = Path.Combine(dir, ".kiln");
        if (Directory.Exists(kilnDir))
            foreach (var f in Directory.EnumerateFiles(kilnDir))
                if (Path.GetExtension(f) is ".jsa" or ".aot")
                    try { File.Delete(f); } catch { }
    }

    // MARK: 存档

    public sealed record World(string Url, DateTime LastPlayed, string? Icon)
    {
        public string Name => Path.GetFileName(Url);
    }

    public static IReadOnlyList<World> Worlds(McInstance instance)
    {
        var saves = Path.Combine(instance.GameDir, "saves");
        var result = new List<World>();
        string[] dirs;
        try { dirs = Directory.GetDirectories(saves); }
        catch { return result; }
        foreach (var d in dirs)
        {
            var level = Path.Combine(d, "level.dat");
            if (!File.Exists(level)) continue;
            DateTime date;
            try { date = File.GetLastWriteTimeUtc(level); }
            catch { date = DateTime.MinValue; }
            var icon = Path.Combine(d, "icon.png");
            result.Add(new World(d, date, File.Exists(icon) ? icon : null));
        }
        return result.OrderByDescending(w => w.LastPlayed).ToList();
    }

    /// 导出存档为 zip（包含存档文件夹本身，可直接导入其他启动器或解压到 saves）
    public static void ExportWorld(World world, string output)
    {
        try { File.Delete(output); } catch { }
        var parent = Path.GetDirectoryName(world.Url)!;
        using var zip = ZipFile.Open(output, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(world.Url, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(parent, file);
            zip.CreateEntryFromFile(file, rel);
        }
    }

    /// 导入存档 zip：压缩包根目录可以是存档文件夹，也可以直接是 level.dat
    public static string ImportWorld(string archive, McInstance instance)
    {
        var tmp = Path.Combine(Paths.Cache, "world-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            Zip.ExtractAll(archive, tmp);
            // 找到包含 level.dat 的目录
            string? root = null;
            if (File.Exists(Path.Combine(tmp, "level.dat")))
            {
                root = tmp;
            }
            else
            {
                foreach (var f in Directory.EnumerateFiles(tmp, "level.dat", SearchOption.AllDirectories))
                    if (!f.Contains("__MACOSX"))
                    {
                        root = Path.GetDirectoryName(f);
                        break;
                    }
            }
            if (root == null)
                throw new LauncherException(T.Invoke("压缩包中没有找到存档（level.dat）", "No world (level.dat) was found in the archive"));
            var saves = Path.Combine(instance.GameDir, "saves");
            Directory.CreateDirectory(saves);
            var name = root == tmp
                ? Path.GetFileNameWithoutExtension(archive)
                : Path.GetFileName(root)!;
            var baseName = name;
            var i = 2;
            while (Directory.Exists(Path.Combine(saves, name))) { name = $"{baseName} ({i})"; i++; }
            Directory.Move(root, Path.Combine(saves, name));
            return name;
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }
}
