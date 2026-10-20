using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kiln.Core;

/// 皮肤：离线账号的本地皮肤与“同名正版皮肤”，以及微软账号的皮肤上传与披风切换
public static class Skins
{
    public static string Folder => Path.Combine(Paths.Support, "skins");

    public static string SkinPath(string name) => Path.Combine(Folder, name);

    /// 检查是否为有效的皮肤图片（64×64 或旧版 64×32 的 PNG），返回是否为旧格式
    public static bool Validate(byte[] data)
    {
        // PNG 文件头 + IHDR 块：宽高分别位于第 16～20 与 20～24 字节（大端）
        if (data.Length < 24 || data[0] != 0x89 || data[1] != 0x50 || data[2] != 0x4E || data[3] != 0x47)
            throw new LauncherException(T.Invoke("皮肤需要是 PNG 图片", "A skin must be a PNG image"));
        var w = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
        var h = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
        if (w != 64 || (h != 64 && h != 32))
            throw new LauncherException(T.Invoke($"皮肤尺寸需要是 64×64 或 64×32（当前 {w}×{h}）",
                $"A skin must be 64×64 or 64×32 (this one is {w}×{h})"));
        return h == 32;
    }

    /// 导入皮肤文件，返回保存后的文件名（按内容命名，相同皮肤只保存一份）
    public static string ImportSkin(string url) => ImportSkin(File.ReadAllBytes(url));

    public static string ImportSkin(byte[] data)
    {
        Validate(data);
        var name = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()[..16] + ".png";
        Directory.CreateDirectory(Folder);
        var dest = SkinPath(name);
        File.WriteAllBytes(dest, data);
        return name;
    }

    /// 同名正版玩家的皮肤（缓存一天）
    public static async Task<(string Url, bool Slim)> Premium(string name)
    {
        var cache = Path.Combine(Paths.Cache, "skins");
        var png = Path.Combine(cache, $"premium-{name.ToLowerInvariant()}.png");
        var meta = Path.Combine(cache, $"premium-{name.ToLowerInvariant()}.slim");
        if (File.Exists(png) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(png)).TotalSeconds < 24 * 3600)
            return (png, File.Exists(meta));
        var (code, user) = await Net.request($"https://api.mojang.com/users/profiles/minecraft/{name}", method: "GET");
        if (code != 200 || user["id"].String is not { } id)
            throw new LauncherException(T.Invoke($"没有名为 {name} 的正版玩家", $"There's no Minecraft player named {name}"));
        var profile = await Net.json(new[] { $"https://sessionserver.mojang.com/session/minecraft/profile/{id}" });
        var texturesProp = profile["properties"].Array.FirstOrDefault(p => p["name"].String == "textures");
        Json textures;
        try
        {
            if (texturesProp["value"].String is not { } value) throw new FormatException();
            textures = Json.Parse(Convert.FromBase64String(value));
        }
        catch
        {
            throw new LauncherException(T.Invoke($"{name} 使用的是默认皮肤", $"{name} uses a default skin"));
        }
        var url = textures["textures"]["SKIN"]["url"].String;
        if (url == null)
            throw new LauncherException(T.Invoke($"{name} 使用的是默认皮肤", $"{name} uses a default skin"));
        Directory.CreateDirectory(cache);
        var data = await Net.data(new[] { url.Replace("http://", "https://") });
        File.WriteAllBytes(png, data);
        var slim = textures["textures"]["SKIN"]["metadata"]["model"].String == "slim";
        try
        {
            if (slim) File.WriteAllBytes(meta, Array.Empty<byte>());
            else if (File.Exists(meta)) File.Delete(meta);
        }
        catch { }
        return (png, slim);
    }

    // MARK: 皮肤库

    /// 用过的皮肤：上传、导入或换下来的皮肤都留一份，之后一键换回去
    public sealed class SavedSkin
    {
        [JsonPropertyName("file")] public string File { get; set; } = "";
        [JsonPropertyName("slim")] public bool Slim { get; set; }
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        [JsonPropertyName("added"), JsonConverter(typeof(LauncherSettings.SwiftDateConverter))]
        public DateTime Added { get; set; }
        [JsonIgnore] public string Id => File;
        [JsonIgnore] public string Url => Skins.SkinPath(File);
    }

    private static string LibraryFile => Path.Combine(Folder, "library.json");

    private static readonly JsonSerializerOptions LibraryOpts = new() { WriteIndented = true };

    /// 皮肤库，最近加入的在前
    public static List<SavedSkin> Library()
    {
        try
        {
            if (System.IO.File.Exists(LibraryFile))
                return JsonSerializer.Deserialize<List<SavedSkin>>(System.IO.File.ReadAllText(LibraryFile), LibraryOpts)
                    ?.Where(s => System.IO.File.Exists(s.Url)).ToList() ?? new();
        }
        catch { }
        return new();
    }

    private static void WriteLibrary(List<SavedSkin> list)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(LibraryFile, JsonSerializer.Serialize(list, LibraryOpts));
        }
        catch { }
    }

    /// 存进皮肤库；同一张皮肤已经在库里时移到最前面，名称和模型以这次为准。最多留 60 张
    public static SavedSkin AddToLibrary(byte[] data, bool slim, string label)
    {
        var file = ImportSkin(data);
        var list = Library().Where(s => s.File != file).ToList();
        var entry = new SavedSkin { File = file, Slim = slim, Label = label, Added = DateTime.UtcNow };
        list.Insert(0, entry);
        WriteLibrary(list.Take(60).ToList());
        return entry;
    }

    /// 从皮肤库移除（图片文件留着，离线账号可能还在用）
    public static void RemoveFromLibrary(SavedSkin skin)
    {
        WriteLibrary(Library().Where(s => s.File != skin.File).ToList());
    }

    // MARK: 微软账号

    public sealed record Cape(string Id, string Alias, string Url, bool Active);

    public sealed record MinecraftProfile(string? SkinUrl, bool Slim, IReadOnlyList<Cape> Capes);

    private const string Services = "https://api.minecraftservices.com/minecraft/profile";

    public static async Task<MinecraftProfile> Profile(string token)
    {
        var (code, j) = await Net.request(Services, method: "GET",
            headers: new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" });
        if (code != 200)
            throw new LauncherException(T.Invoke($"无法读取玩家档案（HTTP {code}）", $"Couldn't read the player profile (HTTP {code})"));
        var skin = j["skins"].Array.FirstOrDefault(s => s["state"].String == "ACTIVE");
        var capes = new List<Cape>();
        foreach (var c in j["capes"].Array)
            if (c["id"].String is { } id)
                capes.Add(new Cape(id, c["alias"].String ?? id, c["url"].String ?? "", c["state"].String == "ACTIVE"));
        return new MinecraftProfile(skin["url"].String,
            skin["variant"].String?.ToUpperInvariant() == "SLIM", capes);
    }

    /// 上传皮肤
    public static async Task Upload(string token, string skin, bool slim)
    {
        var data = await File.ReadAllBytesAsync(skin);
        Validate(data);
        var boundary = "kiln-" + Guid.NewGuid().ToString("N");
        using var body = new MemoryStream();
        void Field(string s) => body.Write(System.Text.Encoding.UTF8.GetBytes(s));
        Field($"--{boundary}\r\nContent-Disposition: form-data; name=\"variant\"\r\n\r\n{(slim ? "slim" : "classic")}\r\n");
        Field($"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"skin.png\"\r\nContent-Type: image/png\r\n\r\n");
        body.Write(data);
        Field($"\r\n--{boundary}--\r\n");
        var (code, j) = await Net.request(Services + "/skins", body: body.ToArray(),
            contentType: $"multipart/form-data; boundary={boundary}",
            headers: new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" });
        if (code != 200)
            throw new LauncherException(T.Invoke("上传皮肤失败：", "Couldn't upload the skin: ")
                + (j["errorMessage"].String ?? $"HTTP {code}"));
    }

    /// 恢复默认皮肤
    public static async Task Reset(string token)
    {
        var (code, _) = await Net.request(Services + "/skins/active", method: "DELETE",
            headers: new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" });
        if (code != 200 && code != 204) throw new LauncherException($"HTTP {code}");
    }

    /// 选择披风；null 为不显示
    public static async Task SetCape(string token, string? id)
    {
        int code;
        if (id != null)
        {
            var (c, _) = await Net.request(Services + "/capes/active", method: "PUT",
                body: Json.From(new Dictionary<string, Json> { ["capeId"] = Json.Of(id) }).Data(false),
                contentType: "application/json",
                headers: new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" });
            code = c;
        }
        else
        {
            var (c, _) = await Net.request(Services + "/capes/active", method: "DELETE",
                headers: new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" });
            code = c;
        }
        if (code != 200 && code != 204) throw new LauncherException($"HTTP {code}");
    }
}

