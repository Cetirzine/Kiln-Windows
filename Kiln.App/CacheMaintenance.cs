using Kiln.Core;

namespace Kiln.App;

internal static class CacheMaintenance
{
    internal static void EnsureIdle(IEnumerable<Job> jobs, IEnumerable<GameProcess> games)
    {
        if (jobs.Any(j => j.State == Job.JobState.Running) || games.Any(g => g.State is GameProcess.GameState.Starting or GameProcess.GameState.Running))
            throw new LauncherException(Localization.T("请先结束正在运行的任务和游戏，再清理缓存。", "Finish running tasks and games before clearing the cache."));
    }
    internal static void ClearContents(string directory)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (Directory.GetParent(root) == null || root == Path.GetPathRoot(root)) throw new ArgumentException("A volume root is not a cache directory.");
        if (!Directory.Exists(root)) return;
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            var target = Path.GetFullPath(entry);
            if (!string.Equals(Path.GetDirectoryName(target), root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unexpected cache entry.");
            var attributes = File.GetAttributes(target);
            if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(target, recursive: (attributes & FileAttributes.ReparsePoint) == 0);
            else File.Delete(target);
        }
    }
    internal static bool ContainsGameData(string cache, IEnumerable<string> games)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cache));
        return games.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)))
            .Any(p => p.Equals(root, StringComparison.OrdinalIgnoreCase) || p.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }
}
