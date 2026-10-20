using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Kiln.Core;

/// 离线账号的皮肤：在本机运行一个最小的 Yggdrasil 服务（只监听 127.0.0.1），
/// 游戏通过 authlib-injector 向它查询皮肤。材质属性用本次运行生成的 RSA 密钥签名。
public sealed class SkinServer
{
    public static readonly SkinServer Shared = new();

    private readonly object _gate = new();
    private TcpListener? _listener;
    private int _port;
    /// 玩家：无连字符 UUID → (名称, 皮肤文件, 是否纤细模型)
    private readonly Dictionary<string, (string Name, string? Skin, bool Slim)> _players = new(StringComparer.OrdinalIgnoreCase);
    private readonly RSA _privateKey = RSA.Create(2048);
    private readonly string _publicKeyPem;

    private SkinServer()
    {
        _publicKeyPem = _privateKey.ExportSubjectPublicKeyInfoPem().Trim();
    }

    /// 服务器根地址（首次调用时启动）
    public string Start()
    {
        lock (_gate)
        {
            if (_listener == null)
            {
                var l = new TcpListener(IPAddress.Loopback, 0);
                l.Start(4);
                _port = ((IPEndPoint)l.LocalEndpoint).Port;
                _listener = l;
                _ = AcceptLoop(l);
                Log.log($"[Skin] 本地皮肤服务：127.0.0.1:{_port}");
            }
            return $"http://127.0.0.1:{_port}";
        }
    }

    public void Register(string uuid, string name, string? skin, bool slim)
    {
        lock (_gate) _players[uuid.Replace("-", "").ToLowerInvariant()] = (name, skin, slim);
    }

    /// 元数据（启动时预先传给 authlib-injector）
    public Json Metadata => Json.From(new Dictionary<string, Json>
    {
        ["meta"] = Json.From(new Dictionary<string, Json>
        {
            ["serverName"] = Json.Of(AppInfo.ShortName),
            ["implementationName"] = Json.Of("Kiln offline skins"),
            ["implementationVersion"] = Json.Of(AppInfo.Version),
            ["feature.no_mojang_namespace"] = Json.Of(true),
        }),
        ["skinDomains"] = Json.From(new[] { Json.Of("127.0.0.1"), Json.Of("localhost") }),
        ["signaturePublickey"] = Json.Of(_publicKeyPem),
    });

    // MARK: HTTP

    private async Task AcceptLoop(TcpListener listener)
    {
        while (true)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(); }
            catch { break; }
            _ = Task.Run(() => Handle(client));
        }
    }

    private async Task Handle(TcpClient client)
    {
        try
        {
            client.ReceiveTimeout = 10000;
            using var stream = client.GetStream();
            var buffer = new List<byte>();
            var chunk = new byte[8192];
            var headEnd = -1;
            while (buffer.Count < (1 << 20))
            {
                var n = await stream.ReadAsync(chunk);
                if (n <= 0) break;
                buffer.AddRange(chunk.AsSpan(0, n).ToArray());
                headEnd = IndexOf(buffer, "\r\n\r\n"u8);
                if (headEnd >= 0) break;
            }
            if (headEnd < 0) return;
            var head = System.Text.Encoding.UTF8.GetString(buffer.Take(headEnd).ToArray());
            var lines = head.Split("\r\n");
            var parts = lines.Length > 0 ? lines[0].Split(' ') : Array.Empty<string>();
            var length = 0;
            foreach (var line in lines)
            {
                var i = line.IndexOf(':');
                if (i > 0 && line[..i].Trim().ToLowerInvariant() == "content-length"
                    && int.TryParse(line[(i + 1)..].Trim(), out var len)) { length = len; break; }
            }
            var bodyStart = headEnd + 4;
            while (buffer.Count < bodyStart + length)
            {
                var n = await stream.ReadAsync(chunk);
                if (n <= 0) break;
                buffer.AddRange(chunk.AsSpan(0, n).ToArray());
            }
            if (parts.Length < 2) return;
            var body = buffer.Skip(bodyStart).Take(length).ToArray();
            var (status, type, payload) = Route(parts[0], parts[1], body);
            var reason = status switch { 200 => "OK", 204 => "No Content", 404 => "Not Found", _ => "OK" };
            var head2 = $"HTTP/1.1 {status} {reason}\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n";
            if (payload.Length > 0) head2 += $"Content-Type: {type}\r\n";
            head2 += "\r\n";
            var response = System.Text.Encoding.UTF8.GetBytes(head2).Concat(payload).ToArray();
            await stream.WriteAsync(response);
        }
        catch { }
        finally
        {
            try { client.Close(); } catch { }
        }
    }

    private static int IndexOf(List<byte> data, ReadOnlySpan<byte> pattern)
    {
        for (var i = 0; i <= data.Count - pattern.Length; i++)
        {
            var ok = true;
            for (var j = 0; j < pattern.Length; j++)
                if (data[i + j] != pattern[j]) { ok = false; break; }
            if (ok) return i;
        }
        return -1;
    }

    private (int Status, string Type, byte[] Body) Route(string method, string target, byte[] body)
    {
        var path = target.Split('?')[0];
        const string json = "application/json; charset=utf-8";
        Dictionary<string, (string Name, string? Skin, bool Slim)> all;
        lock (_gate) all = new Dictionary<string, (string, string?, bool)>(_players);
        if (path != "/" && path.Length > 0) Log.log($"[Skin] {method} {path}");

        if (path == "/" || path.Length == 0) return (200, json, Metadata.Data(false));
        // 按 UUID 查询档案（含签名的材质属性）
        if (path.StartsWith("/sessionserver/session/minecraft/profile/", StringComparison.Ordinal))
        {
            var id = path["/sessionserver/session/minecraft/profile/".Length..].Replace("-", "").ToLowerInvariant();
            if (!all.TryGetValue(id, out var p)) return (204, json, Array.Empty<byte>());
            return (200, json, Profile(id, p).Data(false));
        }
        // 按名称批量查询 UUID
        if (path == "/api/profiles/minecraft" && method == "POST")
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var n in Json.Parse(body).Array)
                    if (n.String is { } s) names.Add(s.ToLowerInvariant());
            }
            catch { }
            var found = all.Where(kv => names.Contains(kv.Value.Name.ToLowerInvariant()))
                .Select(kv => Json.From(new Dictionary<string, Json> { ["id"] = Json.Of(kv.Key), ["name"] = Json.Of(kv.Value.Name) }))
                .ToList();
            return (200, json, Json.From(found).Data(false));
        }
        // 局域网联机：加入与验证
        if (path == "/sessionserver/session/minecraft/join") return (204, json, Array.Empty<byte>());
        if (path == "/sessionserver/session/minecraft/hasJoined")
        {
            var name = QueryValue(target, "username")?.ToLowerInvariant() ?? "";
            var hit = all.FirstOrDefault(kv => kv.Value.Name.ToLowerInvariant() == name);
            if (hit.Key == null) return (204, json, Array.Empty<byte>());
            return (200, json, Profile(hit.Key, hit.Value).Data(false));
        }
        // 材质
        if (path.StartsWith("/textures/", StringComparison.Ordinal))
        {
            var hash = path["/textures/".Length..];
            var skin = all.Values.Select(p => p.Skin).FirstOrDefault(s => s != null && TextureHash(s) == hash);
            if (skin != null)
            {
                try { return (200, "image/png", File.ReadAllBytes(skin)); } catch { }
            }
            return (404, json, Array.Empty<byte>());
        }
        // 1.19+ 的玩家属性：允许聊天与多人游戏
        if (path == "/minecraftservices/player/attributes")
        {
            var attrs = Json.From(new Dictionary<string, Json>
            {
                ["privileges"] = Json.From(new Dictionary<string, Json>
                {
                    ["onlineChat"] = Json.From(new Dictionary<string, Json> { ["enabled"] = Json.Of(true) }),
                    ["multiplayerServer"] = Json.From(new Dictionary<string, Json> { ["enabled"] = Json.Of(true) }),
                    ["multiplayerRealms"] = Json.From(new Dictionary<string, Json> { ["enabled"] = Json.Of(false) }),
                    ["telemetry"] = Json.From(new Dictionary<string, Json> { ["enabled"] = Json.Of(false) }),
                }),
                ["profanityFilterPreferences"] = Json.From(new Dictionary<string, Json> { ["profanityFilterOn"] = Json.Of(false) }),
            });
            return (200, json, attrs.Data(false));
        }
        if (path.StartsWith("/minecraftservices/", StringComparison.Ordinal)) return (204, json, Array.Empty<byte>());
        return (404, json, Array.Empty<byte>());
    }

    private static string? QueryValue(string target, string key)
    {
        var i = target.IndexOf('?');
        if (i < 0) return null;
        foreach (var pair in target[(i + 1)..].Split('&'))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0] == key) return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }

    private Json Profile(string id, (string Name, string? Skin, bool Slim) p)
    {
        var textures = new Dictionary<string, Json>();
        if (p.Skin is { } skin && TextureHash(skin) is { } hash)
        {
            var s = new Dictionary<string, Json> { ["url"] = Json.Of($"http://127.0.0.1:{_port}/textures/{hash}") };
            if (p.Slim) s["metadata"] = Json.From(new Dictionary<string, Json> { ["model"] = Json.Of("slim") });
            textures["SKIN"] = Json.From(s);
        }
        var value = Convert.ToBase64String(Json.From(new Dictionary<string, Json>
        {
            ["timestamp"] = Json.Of(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            ["profileId"] = Json.Of(id),
            ["profileName"] = Json.Of(p.Name),
            ["textures"] = Json.From(textures),
        }).Data(false));
        var property = new Dictionary<string, Json> { ["name"] = Json.Of("textures"), ["value"] = Json.Of(value) };
        var sig = Sign(value);
        if (sig != null) property["signature"] = Json.Of(sig);
        return Json.From(new Dictionary<string, Json>
        {
            ["id"] = Json.Of(id),
            ["name"] = Json.Of(p.Name),
            ["properties"] = Json.From(new[] { Json.From(property) }),
        });
    }

    /// SHA1withRSA 签名（authlib 校验材质属性所用的算法）
    private string? Sign(string value)
    {
        try
        {
            var sig = _privateKey.SignData(System.Text.Encoding.UTF8.GetBytes(value),
                HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
            return Convert.ToBase64String(sig);
        }
        catch { return null; }
    }

    /// 材质地址中的哈希：文件内容的 SHA-256
    public static string? TextureHash(string url)
    {
        try
        {
            using var fs = new FileStream(url, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        }
        catch { return null; }
    }
}
