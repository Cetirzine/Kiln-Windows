using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kiln.Core;

public sealed class Account
{
    public enum AccountKind { Offline, Microsoft, Yggdrasil }

    [JsonPropertyName("kind")] public AccountKind Kind { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("uuid")] public string Uuid { get; set; } = "";

    /// 同一个 UUID 可能出现在不同的皮肤站上
    [JsonIgnore] public string Id => Kind == AccountKind.Yggdrasil
        ? $"yggdrasil:{Uuid}@{(Uri.TryCreate(Server ?? "", UriKind.Absolute, out var u) ? u.Host : "")}"
        : $"{Kind.ToString().ToLowerInvariant()}:{Uuid}";

    /// 登录已失效（刷新令牌被撤销或过期），需要重新登录
    [JsonPropertyName("needsLogin")] public bool? NeedsLogin { get; set; }

    /// 外置登录：认证服务器 API 地址与名称
    [JsonPropertyName("server")] public string? Server { get; set; }
    [JsonPropertyName("serverName")] public string? ServerName { get; set; }

    /// 外置登录：登录时使用的账号（邮箱或用户名），方便重新登录时填写；不保存密码
    [JsonPropertyName("loginName")] public string? LoginName { get; set; }

    /// 离线账号的皮肤
    [JsonPropertyName("skin")] public OfflineSkin? Skin { get; set; }

    public sealed class OfflineSkin
    {
        /// 导入的皮肤文件名（保存在 Kiln 的 skins 文件夹）
        [JsonPropertyName("file")] public string? File { get; set; }
        [JsonPropertyName("slim")] public bool Slim { get; set; }
        /// 使用同名正版玩家的皮肤
        [JsonPropertyName("premium")] public string? Premium { get; set; }

        [JsonIgnore] public bool IsEmpty => File == null && Premium == null;
    }

    [JsonIgnore] public string TypeText => Kind switch
    {
        AccountKind.Microsoft => T.Invoke("微软账号", "Microsoft"),
        AccountKind.Offline => T.Invoke("离线账号", "Offline"),
        _ => ServerName ?? T.Invoke("外置登录", "Third-party"),
    };

    /// 与原版服务端一致的离线 UUID（UUID v3，"OfflinePlayer:" + 玩家名）
    public static string OfflineUuid(string name)
    {
        var bytes = Hash.Md5(System.Text.Encoding.UTF8.GetBytes($"OfflinePlayer:{name}"));
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

/// 启动时实际使用的登录结果
public sealed class LoginResult
{
    public string Name = "";
    public string Uuid = "";
    public string AccessToken = "";
    public string UserType = "";
    public string Xuid = "0";
    /// 额外的 JVM 参数（authlib-injector）
    public List<string> JvmArgs = new();

    public LoginResult(string name, string uuid, string accessToken, string userType, string xuid = "0")
    {
        Name = name; Uuid = uuid; AccessToken = accessToken; UserType = userType; Xuid = xuid;
    }
}

/// 账号存储（Accounts.json），对应原版的 @MainActor AccountStore；此处改为线程安全
public sealed class AccountStore
{
    public static readonly AccountStore Shared = new();

    private readonly object _gate = new();

    public List<Account> Accounts { get; private set; } = new();
    private string? _selectedId;

    public string? SelectedId
    {
        get { lock (_gate) return _selectedId; }
        set { lock (_gate) { _selectedId = value; SaveLocked(); } }
    }

    private readonly Dictionary<string, (string Token, DateTime Expires)> _tokenCache = new();

    private static string File => Path.Combine(Paths.Support, "Accounts.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private sealed class Stored
    {
        [JsonPropertyName("accounts")] public List<Account> Accounts { get; set; } = new();
        [JsonPropertyName("selected")] public string? Selected { get; set; }
    }

    private AccountStore()
    {
        try
        {
            if (System.IO.File.Exists(File))
            {
                var s = JsonSerializer.Deserialize<Stored>(System.IO.File.ReadAllText(File), JsonOpts);
                if (s != null)
                {
                    Accounts = s.Accounts;
                    _selectedId = s.Selected;
                }
            }
        }
        catch { }
    }

    public Account? Selected
    {
        get { lock (_gate) return Accounts.FirstOrDefault(a => a.Id == _selectedId) ?? Accounts.FirstOrDefault(); }
    }

    public Account? AccountById(string? id)
    {
        if (id == null) return null;
        lock (_gate) return Accounts.FirstOrDefault(a => a.Id == id);
    }

    /// 某个版本启动时使用的账号：版本指定了且仍存在时用指定的，否则用当前账号
    public Account? AccountFor(LauncherSettings.InstanceSetup setup) =>
        AccountById(setup.Account) ?? Selected;

    public void Select(Account acc) => SelectedId = acc.Id;

    /// 按列表位置切换（菜单栏 ⌃⌘1～9）
    public void SelectAt(int index)
    {
        lock (_gate)
        {
            if (index < 0 || index >= Accounts.Count) return;
            _selectedId = Accounts[index].Id;
            SaveLocked();
        }
    }

    /// 调整顺序
    public void Move(Account acc, int offset)
    {
        lock (_gate)
        {
            var i = Accounts.FindIndex(a => a.Id == acc.Id);
            if (i < 0) return;
            var j = Math.Clamp(i + offset, 0, Accounts.Count - 1);
            if (i == j) return;
            var item = Accounts[i];
            Accounts.RemoveAt(i);
            Accounts.Insert(j, item);
            SaveLocked();
        }
    }

    /// 修改离线账号名称；UUID 随名称重新计算（与服务端离线模式一致）。返回新账号，名称已被占用时返回 null
    public Account? RenameOffline(Account acc, string name)
    {
        lock (_gate)
        {
            if (acc.Kind != Account.AccountKind.Offline) return null;
            var i = Accounts.FindIndex(a => a.Id == acc.Id);
            if (i < 0) return null;
            var renamed = new Account { Kind = Account.AccountKind.Offline, Name = name, Uuid = Account.OfflineUuid(name), Skin = acc.Skin };
            if (renamed.Id != acc.Id && Accounts.Any(a => a.Id == renamed.Id)) return null;
            var wasSelected = Selected?.Id == acc.Id;
            Accounts[i] = renamed;
            if (wasSelected) _selectedId = renamed.Id;
            SaveLocked();
            return renamed;
        }
    }

    private void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(
                new Stored { Accounts = Accounts, Selected = _selectedId }, JsonOpts));
        }
        catch (Exception e)
        {
            Log.log($"[Accounts] 保存失败：{e.Friendly()}");
        }
    }

    /// 校验离线玩家名（对应原版 McLoginAble）
    public static string? ValidateOffline(string name)
    {
        if (name.Trim().Length == 0) return T.Invoke("请输入名称", "Enter a name");
        if (name.Contains('"')) return T.Invoke("名称不能包含引号", "The name can't contain quotes");
        return null;
    }

    public void AddOffline(string name)
    {
        lock (_gate)
        {
            var acc = new Account { Kind = Account.AccountKind.Offline, Name = name, Uuid = Account.OfflineUuid(name) };
            Accounts.RemoveAll(a => a.Id == acc.Id);
            Accounts.Insert(0, acc);
            _selectedId = acc.Id;
            SaveLocked();
        }
    }

    public void Remove(Account acc)
    {
        lock (_gate)
        {
            Accounts.RemoveAll(a => a.Id == acc.Id);
            if (acc.Kind == Account.AccountKind.Microsoft) TokenStore.Delete(acc.Uuid);
            if (acc.Kind == Account.AccountKind.Yggdrasil) TokenStore.Delete(YggKey(acc));
            if (_selectedId == acc.Id) _selectedId = Accounts.FirstOrDefault()?.Id;
            SaveLocked();
        }
    }

    public void AddMicrosoft((string Name, string Uuid) profile, string refreshToken, string accessToken, DateTime expires)
    {
        lock (_gate)
        {
            var acc = new Account { Kind = Account.AccountKind.Microsoft, Name = profile.Name, Uuid = profile.Uuid };
            // 重新登录已有账号时保持原来的位置
            var i = Accounts.FindIndex(a => a.Id == acc.Id);
            if (i >= 0) Accounts[i] = acc;
            else Accounts.Insert(0, acc);
            TokenStore.Set(refreshToken, acc.Uuid);
            _tokenCache[acc.Uuid] = (accessToken, expires);
            _selectedId = acc.Id;
            SaveLocked();
        }
    }

    private void SetNeedsLogin(Account acc, bool value)
    {
        lock (_gate)
        {
            var i = Accounts.FindIndex(a => a.Id == acc.Id);
            if (i < 0) return;
            Accounts[i].NeedsLogin = value ? true : null;
            SaveLocked();
        }
    }

    /// 正版账号改名后同步本地保存的名称
    public void UpdateName(Account acc, string name)
    {
        lock (_gate)
        {
            var i = Accounts.FindIndex(a => a.Id == acc.Id);
            if (i < 0) return;
            Accounts[i].Name = name;
            SaveLocked();
        }
    }

    // MARK: 外置登录

    internal static string YggKey(Account acc) => $"yggdrasil|{acc.Id}";

    private (string Access, string Client)? YggTokens(Account acc)
    {
        var s = TokenStore.Get(YggKey(acc));
        if (s == null) return null;
        try
        {
            var j = Json.Parse(s);
            var a = j["accessToken"].String;
            var c = j["clientToken"].String;
            if (a == null || c == null) return null;
            return (a, c);
        }
        catch { return null; }
    }

    private void SaveYggTokens(Account acc, string access, string client)
    {
        var j = Json.From(new Dictionary<string, Json>
        {
            ["accessToken"] = Json.Of(access),
            ["clientToken"] = Json.Of(client),
        });
        TokenStore.Set(System.Text.Encoding.UTF8.GetString(j.Data(false)), YggKey(acc));
    }

    /// 添加（或重新登录）外置登录账号
    public void AddYggdrasil(string root, string serverName, string loginName, Yggdrasil.Profile profile,
        string accessToken, string clientToken)
    {
        lock (_gate)
        {
            var acc = new Account
            {
                Kind = Account.AccountKind.Yggdrasil, Name = profile.Name, Uuid = profile.Id,
                Server = root, ServerName = serverName, LoginName = loginName,
            };
            var i = Accounts.FindIndex(a => a.Id == acc.Id);
            if (i >= 0) Accounts[i] = acc;
            else Accounts.Insert(0, acc);
            SaveYggTokens(acc, accessToken, clientToken);
            _selectedId = acc.Id;
            SaveLocked();
        }
    }

    /// 设置离线账号的皮肤
    public void SetSkin(Account acc, Account.OfflineSkin? skin)
    {
        lock (_gate)
        {
            var i = Accounts.FindIndex(a => a.Id == acc.Id);
            if (i < 0) return;
            Accounts[i].Skin = skin?.IsEmpty != false ? null : skin;
            SaveLocked();
        }
    }

    /// 启动前登录
    public async Task<LoginResult> Login(Account acc)
    {
        switch (acc.Kind)
        {
            case Account.AccountKind.Offline:
            {
                var r = new LoginResult(acc.Name, acc.Uuid, acc.Uuid, "legacy");
                if (acc.Skin is { } skin && !skin.IsEmpty)
                {
                    // 自定义皮肤：本地皮肤服务 + authlib-injector；失败时不影响启动，只是不显示皮肤
                    try
                    {
                        var file = skin.File is { } f ? Skins.SkinPath(f) : null;
                        var slim = skin.Slim;
                        if (skin.Premium is { } name)
                        {
                            var (premiumFile, premiumSlim) = await Skins.Premium(name);
                            file = premiumFile;
                            slim = premiumSlim;
                        }
                        var root = SkinServer.Shared.Start();
                        SkinServer.Shared.Register(acc.Uuid, acc.Name, file, slim);
                        r.JvmArgs = await AuthlibInjector.JvmArgs(root, SkinServer.Shared.Metadata);
                    }
                    catch (Exception e)
                    {
                        Log.log($"[Skin] 无法启用离线皮肤：{e.Friendly()}");
                    }
                }
                return r;
            }
            case Account.AccountKind.Yggdrasil:
            {
                string root;
                (string Access, string Client) tokens;
                lock (_gate)
                {
                    if (acc.Server == null || YggTokens(acc) is not { } t)
                    {
                        SetNeedsLogin(acc, true);
                        throw new LauncherException(T.Invoke("外置登录已失效，请重新登录。",
                            "Your third-party login expired. Please sign in again."));
                    }
                    root = acc.Server;
                    tokens = t;
                }
                if (!await Yggdrasil.Validate(root, tokens.Access, tokens.Client))
                {
                    try
                    {
                        var s = await Yggdrasil.Refresh(root, tokens.Access, tokens.Client);
                        tokens = (s.AccessToken, s.ClientToken);
                        SaveYggTokens(acc, s.AccessToken, s.ClientToken);
                        if (s.Selected is { Name: { Length: > 0 } } sel && sel.Name != acc.Name)
                        {
                            lock (_gate)
                            {
                                var i = Accounts.FindIndex(a => a.Id == acc.Id);
                                if (i >= 0)
                                {
                                    Accounts[i].Name = sel.Name;
                                    SaveLocked();
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        if (!Net.IsTransient(e)) SetNeedsLogin(acc, true);
                        throw new LauncherException(T.Invoke("外置登录已失效，请重新输入密码登录。",
                            "Your third-party login expired. Sign in again with your password.")
                            + "\n" + e.Friendly());
                    }
                }
                if (acc.NeedsLogin == true) SetNeedsLogin(acc, false);
                Json? meta = null;
                try { meta = await Net.json(new[] { root }); } catch { }
                var r = new LoginResult(AccountById(acc.Id)?.Name ?? acc.Name, acc.Uuid, tokens.Access, "mojang");
                r.JvmArgs = await AuthlibInjector.JvmArgs(root, meta);
                return r;
            }
            default:
            {
                DateTime expires;
                string cachedToken;
                string refresh;
                lock (_gate)
                {
                    if (_tokenCache.TryGetValue(acc.Uuid, out var c)
                        && c.Expires > DateTime.UtcNow.AddSeconds(300))
                    {
                        return new LoginResult(acc.Name, acc.Uuid, c.Token, "msa");
                    }
                    refresh = TokenStore.Get(acc.Uuid)
                        ?? throw Fail(acc, T.Invoke("正版登录已过期，请重新登录。",
                            "Your Microsoft login expired. Please sign in again."));
                    expires = default;
                    cachedToken = "";
                }
                MicrosoftAuth.Result r;
                try
                {
                    r = await MicrosoftAuth.Refresh(refresh);
                }
                catch (Exception e)
                {
                    // 网络问题不代表登录失效
                    if (!Net.IsTransient(e)) SetNeedsLogin(acc, true);
                    throw;
                }
                lock (_gate)
                {
                    TokenStore.Set(r.RefreshToken, acc.Uuid);
                    _tokenCache[acc.Uuid] = (r.AccessToken, r.Expires);
                    var i = Accounts.FindIndex(a => a.Id == acc.Id);
                    if (i >= 0 && (Accounts[i].Name != r.Name || Accounts[i].NeedsLogin == true))
                    {
                        Accounts[i].Name = r.Name;
                        Accounts[i].NeedsLogin = null;
                        SaveLocked();
                    }
                }
                return new LoginResult(r.Name, r.Uuid, r.AccessToken, "msa");
            }
        }
    }

    private LauncherException Fail(Account acc, string message)
    {
        SetNeedsLogin(acc, true);
        return new LauncherException(message);
    }
}

// MARK: - 微软正版登录（设备代码流）

public static class MicrosoftAuth
{
    public sealed record DeviceCode(string UserCode, string DeviceCodeValue, string VerificationUri, int Interval, int ExpiresIn);

    public sealed record Result(string Name, string Uuid, string AccessToken, string RefreshToken, DateTime Expires);

    public const string Scope = "XboxLive.signin offline_access";

    public static string ClientId()
    {
        var id = LauncherSettings.Shared.EffectiveClientId;
        if (id.Length == 0)
            throw new LauncherException(T.Invoke(
                "尚未设置微软登录所需的 Client ID。\n请在 设置 → 其他 中填写 Azure 应用 ID。",
                "The Client ID for Microsoft login is not set.\nEnter your Azure application ID in Settings → Other."));
        return id;
    }

    public static async Task<DeviceCode> RequestDeviceCode()
    {
        var (code, j) = await Net.request(
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode",
            body: Net.formBody(new Dictionary<string, string> { ["client_id"] = ClientId(), ["scope"] = Scope }),
            contentType: "application/x-www-form-urlencoded");
        if (code != 200 || j["user_code"].String is not { } user || j["device_code"].String is not { } device)
            throw new LauncherException(j["error_description"].String ?? $"HTTP {code}");
        return new DeviceCode(user, device,
            j["verification_uri"].String ?? "https://www.microsoft.com/link",
            (int)(j["interval"].Int ?? 5), (int)(j["expires_in"].Int ?? 900));
    }

    /// 轮询，直到用户在浏览器中完成登录
    public static async Task<(string Access, string Refresh)> Poll(DeviceCode dc)
    {
        var interval = Math.Max(dc.Interval, 2);
        var deadline = DateTime.UtcNow.AddSeconds(dc.ExpiresIn);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval));
            Json j;
            try
            {
                (_, j) = await Net.request(
                    "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
                    body: Net.formBody(new Dictionary<string, string>
                    {
                        ["client_id"] = ClientId(),
                        ["device_code"] = dc.DeviceCodeValue,
                        ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    }),
                    contentType: "application/x-www-form-urlencoded");
            }
            catch (Exception e) when (Net.IsTransient(e))
            {
                // 网络暂时中断时继续等待，下一轮再查询
                continue;
            }
            if (j["access_token"].String is { } a && j["refresh_token"].String is { } r) return (a, r);
            switch (j["error"].String)
            {
                case "authorization_pending": continue;
                case "slow_down": interval += 5; break;
                case "authorization_declined": throw new LauncherException(T.Invoke("登录已取消", "Sign-in cancelled"));
                case "expired_token": throw new LauncherException(T.Invoke("登录代码已过期，请重试。", "The login code expired. Please try again."));
                default: throw new LauncherException(j["error_description"].String ?? T.Invoke("登录失败", "Sign-in failed"));
            }
        }
        throw new LauncherException(T.Invoke("登录代码已过期，请重试。", "The login code expired. Please try again."));
    }

    public static async Task<Result> Refresh(string refreshToken)
    {
        var (_, j) = await Net.request(
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
            body: Net.formBody(new Dictionary<string, string>
            {
                ["client_id"] = ClientId(),
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token",
                ["scope"] = Scope,
            }),
            contentType: "application/x-www-form-urlencoded");
        if (j["access_token"].String is not { } a)
            throw new LauncherException(T.Invoke("正版登录已过期，请重新登录。",
                "Your Microsoft login expired. Please sign in again.") + "\n" + (j["error_description"].String ?? ""));
        return await Minecraft(msAccess: a, refresh: j["refresh_token"].String ?? refreshToken);
    }

    /// Microsoft → Xbox Live → XSTS → Minecraft
    public static async Task<Result> Minecraft(string msAccess, string refresh)
    {
        const string json = "application/json";
        // XBL
        var xblBody = Json.From(new Dictionary<string, Json>
        {
            ["Properties"] = Json.From(new Dictionary<string, Json>
            {
                ["AuthMethod"] = Json.Of("RPS"),
                ["SiteName"] = Json.Of("user.auth.xboxlive.com"),
                ["RpsTicket"] = Json.Of($"d={msAccess}"),
            }),
            ["RelyingParty"] = Json.Of("http://auth.xboxlive.com"),
            ["TokenType"] = Json.Of("JWT"),
        });
        var (_, xbl) = await Net.request("https://user.auth.xboxlive.com/user/authenticate",
            body: xblBody.Data(false), contentType: json);
        if (xbl["Token"].String is not { } xblToken || xbl["DisplayClaims"]["xui"][0]["uhs"].String is not { } uhs)
            throw new LauncherException(T.Invoke("登录失败", "Sign-in failed") + " (XBL)");
        // XSTS
        var xstsBody = Json.From(new Dictionary<string, Json>
        {
            ["Properties"] = Json.From(new Dictionary<string, Json>
            {
                ["SandboxId"] = Json.Of("RETAIL"),
                ["UserTokens"] = Json.From(new[] { Json.Of(xblToken) }),
            }),
            ["RelyingParty"] = Json.Of("rp://api.minecraftservices.com/"),
            ["TokenType"] = Json.Of("JWT"),
        });
        var (xstsCode, xsts) = await Net.request("https://xsts.auth.xboxlive.com/xsts/authorize",
            body: xstsBody.Data(false), contentType: json);
        if (xstsCode == 401)
        {
            switch ((long)xsts["XErr"].Double)
            {
                case 2148916233:
                    throw new LauncherException(T.Invoke("该微软账号还没有 Xbox 档案，请先在 xbox.com 登录一次。",
                        "This Microsoft account has no Xbox profile yet. Sign in at xbox.com once first."));
                case 2148916235:
                    throw new LauncherException(T.Invoke("你所在的地区无法使用 Xbox Live，请更换网络后重试。",
                        "Xbox Live isn't available in your region. Try another network."));
                case 2148916236 or 2148916237 or 2148916238:
                    throw new LauncherException(T.Invoke("该账号为未成年账号，需要家长在 Xbox 家庭设置中允许后才能登录。",
                        "This is a child account; a parent must allow it in Xbox family settings."));
            }
        }
        if (xsts["Token"].String is not { } xstsToken)
            throw new LauncherException(T.Invoke("登录失败", "Sign-in failed") + $" (XSTS {xstsCode})");
        // Minecraft
        var mcBody = Json.From(new Dictionary<string, Json> { ["identityToken"] = Json.Of($"XBL3.0 x={uhs};{xstsToken}") });
        var (mcCode, mc) = await Net.request("https://api.minecraftservices.com/authentication/login_with_xbox",
            body: mcBody.Data(false), contentType: json);
        if (mc["access_token"].String is not { } mcToken)
        {
            var detail = mc["errorMessage"].String ?? mc["error"].String ?? "";
            Log.log($"[Auth] login_with_xbox 失败：HTTP {mcCode} {detail}");
            if (mcCode == 403 || detail.Contains("Invalid app registration"))
                throw new LauncherException(T.Invoke(
                    "微软账号已验证，但这个启动器的 Client ID 还没有获准使用 Minecraft 接口。需要用该 ID 向微软申请 Minecraft API 访问权限（详见 https://aka.ms/AppRegInfo），批准后即可登录。",
                    "Your Microsoft account was verified, but this launcher's Client ID isn't approved for the Minecraft API yet. Request Minecraft API access for this ID (see https://aka.ms/AppRegInfo); sign-in will work once it's approved."));
            if (mcCode == 429)
                throw new LauncherException(T.Invoke("请求过于频繁，请稍后再试。", "Too many requests. Try again in a little while."));
            throw new LauncherException(T.Invoke($"登录失败（Minecraft，HTTP {mcCode}）", $"Sign-in failed (Minecraft, HTTP {mcCode})")
                + (detail.Length == 0 ? "" : $"：{detail}"));
        }
        var expires = DateTime.UtcNow.AddSeconds(mc["expires_in"].Int ?? 86400);
        // 档案
        var (pCode, profile) = await Net.request("https://api.minecraftservices.com/minecraft/profile", method: "GET",
            headers: new Dictionary<string, string> { ["Authorization"] = $"Bearer {mcToken}" });
        if (pCode == 404)
            throw new LauncherException(T.Invoke("该账号没有 Minecraft: Java Edition，或 Game Pass 已过期。",
                "This account doesn't own Minecraft: Java Edition, or Game Pass has expired."));
        if (profile["id"].String is not { } id || profile["name"].String is not { } name)
            throw new LauncherException(T.Invoke("请先在 minecraft.net 创建玩家档案。", "Create a player profile at minecraft.net first."));
        return new Result(name, id, mcToken, refresh, expires);
    }
}

// MARK: - 令牌

/// 登录令牌（微软的刷新令牌、外置登录的令牌）存在本机的 Tokens.json 里。
/// 原版在 macOS 上从钥匙串改为文件以避免更新后反复弹窗；Windows 版同样使用文件。
public static class TokenStore
{
    private static readonly object Gate = new();
    private static string File => Path.Combine(Paths.Support, "Tokens.json");

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (System.IO.File.Exists(File))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(File))
                    ?? new();
        }
        catch { }
        return new();
    }

    private static void Write(Dictionary<string, string> d)
    {
        try
        {
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(d));
        }
        catch (Exception e)
        {
            Log.log($"[TokenStore] 保存失败：{e.Friendly()}");
        }
    }

    public static void Set(string value, string account)
    {
        lock (Gate)
        {
            var d = Load();
            d[account] = value;
            Write(d);
        }
    }

    public static string? Get(string account)
    {
        lock (Gate)
        {
            var d = Load();
            return d.GetValueOrDefault(account);
        }
    }

    public static void Delete(string account)
    {
        lock (Gate)
        {
            var d = Load();
            d.Remove(account);
            Write(d);
        }
    }
}

