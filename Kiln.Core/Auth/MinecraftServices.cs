using System.Globalization;

namespace Kiln.Core;

/// 微软正版账号的 Minecraft 服务接口（api.minecraftservices.com）：改名、拥有的游戏、联机与聊天权限、封禁状态
public static class MinecraftServices
{
    private const string Root = "https://api.minecraftservices.com";

    private static Task<(int Code, Json Body)> Get(string path, string token) =>
        Net.request(Root + path, method: "GET", headers: new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" });

    private static DateTime? Date(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.UtcDateTime;
        return null;
    }

    // MARK: 改名

    public sealed record NameChange(DateTime? ChangedAt, bool Allowed)
    {
        /// 改名间隔 30 天，到这个时间才能再改
        public DateTime? NextChange => ChangedAt?.AddDays(30);
    }

    public static async Task<NameChange> GetNameChange(string token)
    {
        var (code, j) = await Get("/minecraft/profile/namechange", token);
        if (code != 200)
            throw new LauncherException(T.Invoke($"无法读取改名信息（HTTP {code}）", $"Couldn't read name change info (HTTP {code})"));
        return new NameChange(Date(j["changedAt"].String) ?? Date(j["createdAt"].String),
            j["nameChangeAllowed"].Bool ?? false);
    }

    public enum NameStatus { Available, Taken, NotAllowed }

    public static async Task<NameStatus> NameStatusOf(string name, string token)
    {
        var (code, j) = await Get($"/minecraft/profile/name/{name}/available", token);
        if (code != 200) throw new LauncherException($"HTTP {code}");
        return j["status"].String switch
        {
            "AVAILABLE" => NameStatus.Available,
            "DUPLICATE" => NameStatus.Taken,
            _ => NameStatus.NotAllowed,
        };
    }

    /// 合法的 Java 版玩家名：3～16 个字母、数字或下划线
    public static bool IsValidName(string name) =>
        name.Length is >= 3 and <= 16 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// 改名，返回新的名称
    public static async Task<string> ChangeName(string name, string token)
    {
        var (code, j) = await Net.request(Root + $"/minecraft/profile/name/{name}", method: "PUT",
            headers: new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" });
        switch (code)
        {
            case 200: return j["name"].String ?? name;
            case 400: throw new LauncherException(T.Invoke("名称不合法：只能用 3～16 个字母、数字或下划线。",
                "Invalid name: use 3–16 letters, digits or underscores."));
            case 403: throw new LauncherException(T.Invoke("现在还不能改名：距离上次改名需要满 30 天。",
                "You can't change your name yet: it's been less than 30 days since the last change."));
            case 409: throw new LauncherException(T.Invoke("这个名称已经被别人用了。", "That name is already taken."));
            case 429: throw new LauncherException(T.Invoke("请求过于频繁，请稍后再试。", "Too many requests. Try again in a little while."));
            default:
                var detail = j["errorMessage"].String ?? j["details"]["status"].String ?? "";
                throw new LauncherException(T.Invoke($"改名失败（HTTP {code}）", $"Couldn't change the name (HTTP {code})")
                    + (detail.Length == 0 ? "" : $"：{detail}"));
        }
    }

    // MARK: 拥有的游戏

    public sealed record Ownership(bool JavaPurchased, bool Bedrock, bool Dungeons, bool Legends);

    public static async Task<Ownership> GetOwnership(string token)
    {
        var (code, j) = await Get("/entitlements/mcstore", token);
        if (code != 200)
            throw new LauncherException(T.Invoke($"无法读取拥有的游戏（HTTP {code}）", $"Couldn't read owned games (HTTP {code})"));
        var names = j["items"].Array.Select(i => i["name"].String ?? "").ToHashSet();
        return new Ownership(
            names.Contains("game_minecraft") || names.Contains("product_minecraft"),
            names.Contains("game_minecraft_bedrock") || names.Contains("product_minecraft_bedrock"),
            names.Contains("game_dungeons") || names.Contains("product_dungeons"),
            names.Contains("game_legends") || names.Contains("product_legends"));
    }

    // MARK: 权限与封禁

    public sealed record Ban(DateTime? Expires, string? Reason);

    public sealed record Attributes(bool Multiplayer, bool Realms, bool Chat, Ban? MultiplayerBan);

    public static async Task<Attributes> GetAttributes(string token)
    {
        var (code, j) = await Get("/player/attributes", token);
        if (code != 200)
            throw new LauncherException(T.Invoke($"无法读取账号状态（HTTP {code}）", $"Couldn't read account status (HTTP {code})"));
        var p = j["privileges"];
        Ban? ban = null;
        var scope = j["banStatus"]["bannedScopes"]["MULTIPLAYER"];
        if (scope.Object.Count > 0)
        {
            DateTime? expires = scope["expires"].Double is { } ms
                ? DateTime.UnixEpoch.AddMilliseconds(ms) : null;
            ban = new Ban(expires, scope["reasonMessage"].String ?? scope["reason"].String);
        }
        return new Attributes(
            p["multiplayerServer"]["enabled"].Bool ?? true,
            p["multiplayerRealms"]["enabled"].Bool ?? true,
            p["onlineChat"]["enabled"].Bool ?? true,
            ban);
    }
}
