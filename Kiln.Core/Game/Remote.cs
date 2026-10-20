using System.Globalization;
using System.Text.RegularExpressions;

namespace Kiln.Core;

/// 远程版本列表中的一项
public sealed record RemoteVersion(string Id, string Type, string Url, string? Sha1, DateTime ReleaseTime)
{
    public McVersionKind Kind =>
        McVersionKinds.IsAprilFool(Id) ? McVersionKind.AprilFool
        : Type switch
        {
            "release" => McVersionKind.Release,
            "snapshot" or "pending" => McVersionKind.Snapshot,
            _ => McVersionKind.Old,
        };
}

public enum McVersionKind { Release, Snapshot, Old, AprilFool }

public static class McVersionKindExtensions
{
    public static string Title(this McVersionKind kind) => kind switch
    {
        McVersionKind.Release => T.Invoke("正式版", "Release"),
        McVersionKind.Snapshot => T.Invoke("快照", "Snapshot"),
        McVersionKind.Old => T.Invoke("远古版", "Alpha/Beta"),
        _ => T.Invoke("愚人节版", "April Fools"),
    };

    public static string Icon(this McVersionKind kind) => kind switch
    {
        McVersionKind.Release => "Grass",
        McVersionKind.Snapshot => "CommandBlock",
        McVersionKind.Old => "CobbleStone",
        _ => "GoldBlock",
    };
}

public static class McVersionKinds
{
    /// 已知的愚人节版本
    public static readonly HashSet<string> AprilFoolIds = new()
    {
        "15w14a", "1.RV-Pre1", "3D Shareware v1.34", "20w14infinite", "20w14∞", "22w13oneblockatatime",
        "23w13a_or_b", "24w14potato", "25w14craftmine", "2.0",
    };

    public static bool IsAprilFool(string id) =>
        AprilFoolIds.Contains(id) || id.ToLowerInvariant().Contains("infinite") && id.StartsWith("20w14");
}

public static class VersionManifest
{
    public const string Url = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    public sealed record Result(string? LatestRelease, string? LatestSnapshot, IReadOnlyList<RemoteVersion> Versions);

    private static Result? _cached;
    private static readonly object Gate = new();

    public static async Task<Result> Fetch(bool force = false)
    {
        lock (Gate)
        {
            if (!force && _cached != null) return _cached;
        }
        var j = await Net.json(Url, Net.Kind.VersionList);
        var versions = new List<RemoteVersion>();
        foreach (var v in j["versions"].Array)
        {
            var id = v["id"].String;
            var u = v["url"].String;
            if (id == null || u == null) continue;
            versions.Add(new RemoteVersion(id, v["type"].String ?? "release", u, v["sha1"].String,
                ParseDate(v["releaseTime"].String) ?? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        }
        var r = new Result(j["latest"]["release"].String, j["latest"]["snapshot"].String, versions);
        lock (Gate) _cached = r;
        return r;
    }

    public static async Task<RemoteVersion> Find(string id)
    {
        var list = await Fetch();
        if (list.Versions.FirstOrDefault(v => v.Id == id) is { } v1) return v1;
        var fresh = await Fetch(force: true);
        if (fresh.Versions.FirstOrDefault(v => v.Id == id) is { } v2) return v2;
        throw new LauncherException(T.Invoke($"在版本列表中找不到 {id}", $"Version {id} was not found in the version list"));
    }

    internal static DateTime? ParseDate(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.UtcDateTime;
        return null;
    }
}

/// Fabric / Quilt 加载器
public enum ModLoaderKind { Fabric, Quilt }

public static class ModLoaderKindExtensions
{
    public static string Id(this ModLoaderKind kind) => kind switch
    {
        ModLoaderKind.Fabric => "Fabric",
        _ => "Quilt",
    };

    public static string MetaBase(this ModLoaderKind kind) => kind switch
    {
        ModLoaderKind.Fabric => "https://meta.fabricmc.net/v2",
        _ => "https://meta.quiltmc.org/v3",
    };

    public sealed record LoaderVersion(string Version, bool Stable)
    {
        public string Id => Version;
    }

    /// 某个 MC 版本可用的加载器版本
    public static async Task<IReadOnlyList<LoaderVersion>> Versions(this ModLoaderKind kind, string game)
    {
        var j = await Net.json($"{kind.MetaBase()}/versions/loader/{game}", Net.Kind.VersionList);
        var result = new List<LoaderVersion>();
        foreach (var item in j.Array)
        {
            var loader = item["loader"];
            var v = loader["version"].String;
            if (v == null) continue;
            // Quilt 没有 stable 字段，用 beta 判定
            var stable = loader["stable"].Bool ?? !(v.Contains("beta") || v.Contains("pre"));
            result.Add(new LoaderVersion(v, stable));
        }
        return result;
    }

    public static string ProfileUrl(this ModLoaderKind kind, string game, string loader) =>
        $"{kind.MetaBase()}/versions/loader/{game}/{loader}/profile/json";
}
