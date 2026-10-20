using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Kiln.Core;

public enum DownloadSource { MirrorFirst = 0, OfficialSlowFallback = 1, OfficialFirst = 2 }

/// 新版本是否使用独立的游戏目录
public enum IsolationMode { None = 0, ModdedAndPreview = 1, All = 2 }

public enum WindowMode { Fullscreen = 0, DefaultSize = 1, Custom = 2 }

public enum VisibilityMode { CloseOnStart = 0, HideAndClose = 1, HideAndReopen = 2, Minimize = 3, Keep = 4 }

public enum GCMode { G1 = 0, Zgc = 1 }

/// 全局设置，存储在 %APPDATA%\Kiln\settings.json（键与默认值与原版 UserDefaults 一一对应）
public sealed class LauncherSettings
{
    public static readonly LauncherSettings Shared = new();

    private readonly object _gate = new();
    private readonly JsonObject _store;
    private readonly string _file = Path.Combine(Paths.Support, "settings.json");

    private static readonly JsonSerializerOptions FileOptions = new() { WriteIndented = true };

    private LauncherSettings()
    {
        JsonObject? loaded = null;
        try
        {
            if (File.Exists(_file))
                loaded = JsonNode.Parse(File.ReadAllText(_file)) as JsonObject;
        }
        catch { }
        _store = loaded ?? new JsonObject();

        _language = AppLanguageExtensions.Parse(GetString("language", null));
        _minecraftFolder = GetString("minecraftFolder", Paths.DefaultMinecraft);
        _extraFolders = GetStringList("extraFolders");
        _selectedInstance = GetString("selectedInstance", "");

        _javaPath = GetString("javaPath", "");
        _customJavas = GetStringList("customJavas");
        _memoryAuto = GetBool("memoryAuto", true);
        _memoryGB = GetDouble("memoryGB", 4.0);
        _windowMode = GetEnum<WindowMode>("windowMode", WindowMode.DefaultSize);
        _windowWidth = (int)GetInt("windowWidth", 854);
        _windowHeight = (int)GetInt("windowHeight", 480);
        _visibility = GetEnum<VisibilityMode>("visibility", VisibilityMode.Keep);
        _isolation = GetEnum<IsolationMode>("isolation", IsolationMode.ModdedAndPreview);
        _customInfo = GetString("customInfo", AppInfo.ShortName);
        _jvmArgs = GetString("jvmArgs", "");
        _gameArgs = GetString("gameArgs", "");
        _skipAssetCheck = GetBool("skipAssetCheck", false);

        // 性能（arm64Native 为 Apple 芯片专属，Windows 版不含）
        _gcMode = GetEnum<GCMode>("gcMode", GCMode.G1);
        _classDataSharing = GetBool("classDataSharing", true);
        _compactHeaders = GetBool("compactHeaders", true);
        _fastVerify = GetBool("fastVerify", true);

        // 一次性迁移：0.2.0 曾短暂默认使用镜像源，实测多数网络下官方源更快，统一改回官方源
        if (!GetBool("migratedToOfficialSource", false))
        {
            Remove("versionListSource");
            Remove("fileSource");
            if (GetInt("downloadThreads", 64) == 32) Remove("downloadThreads");
            Set("migratedToOfficialSource", true);
        }
        // 默认优先使用官方源，镜像源（BMCLAPI）作为备用
        _versionListSource = GetEnum<DownloadSource>("versionListSource", DownloadSource.OfficialFirst);
        _fileSource = GetEnum<DownloadSource>("fileSource", DownloadSource.OfficialFirst);
        _downloadThreads = Math.Max(1, Math.Min(256, (int)GetInt("downloadThreads", 64)));
        _gameLanguageAuto = GetBool("gameLanguageAuto", true);

        _msClientId = GetString("msClientId", "");
        _autoCheckUpdates = GetBool("autoCheckUpdates", true);
        _curseforgeKey = GetString("curseforgeKey", "");
        _lastUpdateCheck = GetDate("lastUpdateCheck");
        _debugLog = GetBool("debugLog", false) || Environment.GetEnvironmentVariable("KILN_DEBUG") != null;
        _systemNotifications = GetBool("systemNotifications", true);
        _pcl2TakenOver = GetBool("pcl2TakenOver", false);
        _pcl2Root = GetString("pcl2Root", "");

        Localizer.Shared.Language = Language;
    }

    // MARK: 存取

    private JsonNode? Raw(string key)
    {
        lock (_gate) return _store[key];
    }

    private string GetString(string key, string? def)
    {
        try { var v = Raw(key); return (string?)v ?? def ?? ""; }
        catch { return def ?? ""; }
    }

    private List<string> GetStringList(string key)
    {
        try
        {
            var v = Raw(key);
            if (v is JsonArray a) return a.Where(n => (string?)n != null).Select(n => (string)n!).ToList();
        }
        catch { }
        return new List<string>();
    }

    private long GetInt(string key, long def)
    {
        try { var v = Raw(key); if (v != null) return (long?)v ?? def; }
        catch { }
        return def;
    }

    private double GetDouble(string key, double def)
    {
        try { var v = Raw(key); if (v != null) return (double?)v ?? def; }
        catch { }
        return def;
    }

    private bool GetBool(string key, bool def)
    {
        try { var v = Raw(key); if (v != null) return (bool?)v ?? def; }
        catch { }
        return def;
    }

    private DateTime? GetDate(string key)
    {
        try
        {
            var s = (string?)Raw(key);
            if (s != null && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d))
                return d;
        }
        catch { }
        return null;
    }

    private T GetEnum<T>(string key, T def) where T : struct, Enum
    {
        var raw = (int)GetInt(key, (int)(object)def);
        return Enum.IsDefined(typeof(T), raw) ? (T)(object)raw : def;
    }

    private void Set(string key, JsonNode? value)
    {
        lock (_gate)
        {
            _store[key] = value;
            Save();
        }
    }

    private void Remove(string key)
    {
        lock (_gate)
        {
            if (_store.Remove(key)) Save();
        }
    }

    private void Save()
    {
        try
        {
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, _store.ToJsonString(FileOptions));
            File.Move(tmp, _file, overwrite: true);
        }
        catch (Exception e)
        {
            Log.log($"[Settings] 保存失败：{e.Friendly()}");
        }
    }

    // MARK: 属性（写入即持久化，与原版 didSet 行为一致）

    private AppLanguage _language;
    public AppLanguage Language
    {
        get => _language;
        set { _language = value; Set("language", value.RawValue()); Localizer.Shared.Language = value; }
    }

    private string _minecraftFolder;
    public string MinecraftFolder
    {
        get => _minecraftFolder;
        set { _minecraftFolder = value; Set("minecraftFolder", value); }
    }

    private List<string> _extraFolders;
    public List<string> ExtraFolders
    {
        get => _extraFolders;
        set { _extraFolders = value; Set("extraFolders", new JsonArray(value.Select(v => (JsonNode?)v).ToArray())); }
    }

    private string _selectedInstance;
    public string SelectedInstance
    {
        get => _selectedInstance;
        set { _selectedInstance = value; Set("selectedInstance", value); }
    }

    /// 空字符串表示自动选择
    private string _javaPath;
    public string JavaPath
    {
        get => _javaPath;
        set { _javaPath = value; Set("javaPath", value); }
    }

    private List<string> _customJavas;
    public List<string> CustomJavas
    {
        get => _customJavas;
        set { _customJavas = value; Set("customJavas", new JsonArray(value.Select(v => (JsonNode?)v).ToArray())); }
    }

    private bool _memoryAuto;
    public bool MemoryAuto
    {
        get => _memoryAuto;
        set { _memoryAuto = value; Set("memoryAuto", value); }
    }

    private double _memoryGB;
    public double MemoryGB
    {
        get => _memoryGB;
        set { _memoryGB = value; Set("memoryGB", value); }
    }

    private WindowMode _windowMode;
    public WindowMode WindowModeSetting
    {
        get => _windowMode;
        set { _windowMode = value; Set("windowMode", (int)value); }
    }

    private int _windowWidth;
    public int WindowWidth
    {
        get => _windowWidth;
        set { _windowWidth = value; Set("windowWidth", value); }
    }

    private int _windowHeight;
    public int WindowHeight
    {
        get => _windowHeight;
        set { _windowHeight = value; Set("windowHeight", value); }
    }

    private VisibilityMode _visibility;
    public VisibilityMode Visibility
    {
        get => _visibility;
        set { _visibility = value; Set("visibility", (int)value); }
    }

    private IsolationMode _isolation;
    public IsolationMode Isolation
    {
        get => _isolation;
        set { _isolation = value; Set("isolation", (int)value); }
    }

    private string _customInfo;
    public string CustomInfo
    {
        get => _customInfo;
        set { _customInfo = value; Set("customInfo", value); }
    }

    private string _jvmArgs;
    public string JvmArgs
    {
        get => _jvmArgs;
        set { _jvmArgs = value; Set("jvmArgs", value); }
    }

    private string _gameArgs;
    public string GameArgs
    {
        get => _gameArgs;
        set { _gameArgs = value; Set("gameArgs", value); }
    }

    private bool _skipAssetCheck;
    public bool SkipAssetCheck
    {
        get => _skipAssetCheck;
        set { _skipAssetCheck = value; Set("skipAssetCheck", value); }
    }

    private GCMode _gcMode;
    public GCMode GcMode
    {
        get => _gcMode;
        set { _gcMode = value; Set("gcMode", (int)value); }
    }

    private bool _classDataSharing;
    public bool ClassDataSharing
    {
        get => _classDataSharing;
        set { _classDataSharing = value; Set("classDataSharing", value); }
    }

    private bool _compactHeaders;
    public bool CompactHeaders
    {
        get => _compactHeaders;
        set { _compactHeaders = value; Set("compactHeaders", value); }
    }

    /// 启动时只校验大小与修改时间已变化的文件
    private bool _fastVerify;
    public bool FastVerify
    {
        get => _fastVerify;
        set { _fastVerify = value; Set("fastVerify", value); }
    }

    private DownloadSource _versionListSource;
    public DownloadSource VersionListSource
    {
        get => _versionListSource;
        set { _versionListSource = value; Set("versionListSource", (int)value); }
    }

    private DownloadSource _fileSource;
    public DownloadSource FileSource
    {
        get => _fileSource;
        set { _fileSource = value; Set("fileSource", (int)value); }
    }

    private int _downloadThreads;
    public int DownloadThreads
    {
        get => _downloadThreads;
        set { _downloadThreads = Math.Max(1, Math.Min(256, value)); Set("downloadThreads", _downloadThreads); }
    }

    private bool _gameLanguageAuto;
    public bool GameLanguageAuto
    {
        get => _gameLanguageAuto;
        set { _gameLanguageAuto = value; Set("gameLanguageAuto", value); }
    }

    private string _msClientId;
    public string MsClientId
    {
        get => _msClientId;
        set { _msClientId = value; Set("msClientId", value); }
    }

    private bool _autoCheckUpdates;
    public bool AutoCheckUpdates
    {
        get => _autoCheckUpdates;
        set { _autoCheckUpdates = value; Set("autoCheckUpdates", value); }
    }

    /// CurseForge 官方 API 密钥；为空时通过 MCIM 镜像访问
    private string _curseforgeKey;
    public string CurseforgeKey
    {
        get => _curseforgeKey;
        set { _curseforgeKey = value; Set("curseforgeKey", value); }
    }

    private DateTime? _lastUpdateCheck;
    public DateTime? LastUpdateCheck
    {
        get => _lastUpdateCheck;
        set { _lastUpdateCheck = value; Set("lastUpdateCheck", value?.ToString("O", CultureInfo.InvariantCulture)); }
    }

    private bool _debugLog;
    public bool DebugLog
    {
        get => _debugLog;
        set { _debugLog = value; Set("debugLog", value); }
    }

    /// 启动器在后台时发送系统通知
    private bool _systemNotifications;
    public bool SystemNotifications
    {
        get => _systemNotifications;
        set { _systemNotifications = value; Set("systemNotifications", value); }
    }

    /// PCL2 配置接管：是否已完成（弹窗只出现一次）
    private bool _pcl2TakenOver;
    public bool Pcl2TakenOver
    {
        get => _pcl2TakenOver;
        set { _pcl2TakenOver = value; Set("pcl2TakenOver", value); }
    }

    /// PCL2 程序目录（接管来源）
    private string _pcl2Root;
    public string Pcl2Root
    {
        get => _pcl2Root;
        set { _pcl2Root = value; Set("pcl2Root", value); }
    }

    // MARK: 派生值

    /// 微软登录的 Client ID：设置中手动填写 → 环境变量 → 启动器内置
    public string EffectiveClientId
    {
        get
        {
            var s = MsClientId.Trim();
            if (s.Length > 0) return s;
            if (Environment.GetEnvironmentVariable("KILN_MS_CLIENT_ID") is { Length: > 0 } env) return env;
            return EmbeddedClientID.Value;
        }
    }

    /// 启动器是否内置了 Client ID
    public bool HasEmbeddedClientId => EmbeddedClientID.Value.Length > 0;

    public string MinecraftPath => MinecraftFolder;

    // MARK: 版本独立设置（versions/<name>/.kiln/setup.json）

    /// 与原版格式逐字段一致（Swift Codable 的属性名与日期编码方式），数据可在 macOS / Windows 间互换
    public sealed class InstanceSetup
    {
        [JsonPropertyName("isolation")] public bool? Isolation { get; set; }
        [JsonPropertyName("javaPath")] public string? JavaPath { get; set; }
        [JsonPropertyName("memoryGB")] public double? MemoryGB { get; set; }
        [JsonPropertyName("jvmArgs")] public string? JvmArgs { get; set; }
        [JsonPropertyName("gameArgs")] public string? GameArgs { get; set; }
        [JsonPropertyName("serverEnter")] public string? ServerEnter { get; set; }
        [JsonPropertyName("customInfo")] public string? CustomInfo { get; set; }
        [JsonPropertyName("lastPlayed"), JsonConverter(typeof(SwiftDateConverter))] public DateTime? LastPlayed { get; set; }
        /// 累计游戏时长（秒）
        [JsonPropertyName("playTime")] public double? PlayTime { get; set; }
        /// 自定义图标（方块），为空时按版本类型自动选择
        [JsonPropertyName("icon")] public string? Icon { get; set; }
        /// 固定使用的账号（Account.id），为空时使用当前账号
        [JsonPropertyName("account")] public string? Account { get; set; }
        /// 从其他启动器导入时的来源目录
        [JsonPropertyName("importedFrom")] public string? ImportedFrom { get; set; }
    }

    /// Swift 的 JSONEncoder 默认把 Date 编码为自 2001-01-01 起的秒数
    public sealed class SwiftDateConverter : JsonConverter<DateTime>
    {
        private static readonly DateTime Reference = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Reference.AddSeconds(reader.GetDouble());

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteNumberValue((value.ToUniversalTime() - Reference).TotalSeconds);
    }

    private static readonly JsonSerializerOptions SetupOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public InstanceSetup InstanceSetupOf(string versionDir)
    {
        try
        {
            var file = Path.Combine(versionDir, ".kiln", "setup.json");
            if (File.Exists(file))
                return JsonSerializer.Deserialize<InstanceSetup>(File.ReadAllText(file), SetupOptions) ?? new();
        }
        catch { }
        return new();
    }

    public void SaveInstanceSetup(InstanceSetup setup, string versionDir)
    {
        try
        {
            var dir = Path.Combine(versionDir, ".kiln");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "setup.json"), JsonSerializer.Serialize(setup, SetupOptions));
        }
        catch (Exception e)
        {
            Log.log($"[Settings] 保存版本设置失败：{e.Friendly()}");
        }
    }
}
