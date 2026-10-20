using System.Text.RegularExpressions;

namespace Kiln.Core;

/// 可随 Forge 一起安装的附加组件：作为 Forge Mod 放入实例的 mods 文件夹
public static class OptiFine
{
    public sealed record Version(string Game, string Type, string Patch, string Filename, string? Forge)
    {
        public string Id => Filename;
        public bool IsPreview => Patch.ToLowerInvariant().StartsWith("pre");
        public string Display => Type.Replace("_", " ") + " " + Patch;
    }

    /// 某个游戏版本可用的 OptiFine（正式版在前，较新的在前）
    public static async Task<IReadOnlyList<Version>> Versions(string game)
    {
        var j = await Net.json(new[] { $"https://bmclapi2.bangbang93.com/optifine/{game}" });
        var list = new List<Version>();
        foreach (var v in j.Array)
        {
            var type = v["type"].String;
            var patch = v["patch"].String;
            var file = v["filename"].String;
            if (type == null || patch == null || file == null) continue;
            list.Add(new Version(v["mcversion"].String ?? game, type, patch, file, v["forge"].String));
        }
        return list
            .OrderBy(v => v.IsPreview ? 1 : 0)
            .ThenByDescending(v => v.Patch, Comparer<string>.Create(Updater.NumericCompare))
            .ToList();
    }

    /// 下载到 mods 文件夹：优先官网（optifine.net），失败时使用 BMCLAPI
    public static async Task Install(Version v, string mods, Job? job = null)
    {
        var urls = new List<string>();
        try
        {
            var page = await Net.data(new[] { $"https://optifine.net/adloadx?f={v.Filename}" }, timeout: 12);
            var html = System.Text.Encoding.UTF8.GetString(page);
            var m = Regex.Match(html, @"downloadx\?f=[^'""\s]+");
            if (m.Success) urls.Add("https://optifine.net/" + m.Value.Replace("&amp;", "&"));
        }
        catch { }
        urls.Add($"https://bmclapi2.bangbang93.com/optifine/{v.Game}/{v.Type}/{v.Patch}");
        var item = new DownloadItem(urls[0], Path.Combine(mods, v.Filename)) { Urls = urls };
        await Downloader.Run(new[] { item }, job);
    }
}

public static class LiteLoader
{
    public sealed record Version(string Game, string VersionString, string File, string? Md5)
    {
        public string Id => VersionString;
    }

    /// 某个游戏版本可用的 LiteLoader；不支持的版本返回空数组
    public static async Task<IReadOnlyList<Version>> Versions(string game)
    {
        var data = await Net.data(new[] { $"https://bmclapi2.bangbang93.com/liteloader/list?mcversion={game}" });
        if (data.Length == 0) return Array.Empty<Version>();
        var j = Json.TryParse(data) ?? Json.Null;
        var items = j.Array.Count > 0 ? j.Array : j.Object.Count > 0 ? new[] { j } : Array.Empty<Json>();
        var result = new List<Version>();
        foreach (var v in items)
        {
            if (v["version"].String is not { } version) continue;
            result.Add(new Version(v["mcversion"].String ?? game, version,
                v["build"]["file"].String ?? $"liteloader-{version}.jar", v["hash"].String));
        }
        return result;
    }

    /// 下载到 mods 文件夹（官方站点已停止服务，使用 BMCLAPI）
    public static async Task Install(Version v, string mods, Job? job = null)
    {
        var item = new DownloadItem($"https://bmclapi2.bangbang93.com/liteloader/download?version={v.VersionString}",
            Path.Combine(mods, v.File));
        await Downloader.Run(new[] { item }, job);
    }
}
