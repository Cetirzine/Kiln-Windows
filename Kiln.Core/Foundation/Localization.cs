using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Kiln.Core;

/// 界面语言。繁体中文由简体中文通过系统的简繁转换自动生成。
public enum AppLanguage
{
    ZhHans,
    ZhHant,
    En,
}

public static class AppLanguageExtensions
{
    public static string RawValue(this AppLanguage language) => language switch
    {
        AppLanguage.ZhHans => "zh-Hans",
        AppLanguage.ZhHant => "zh-Hant",
        _ => "en",
    };

    public static string DisplayName(this AppLanguage language) => language switch
    {
        AppLanguage.ZhHans => "简体中文",
        AppLanguage.ZhHant => "繁體中文",
        _ => "English",
    };

    /// Minecraft 的游戏语言代码
    public static string GameLanguage(this AppLanguage language) => language switch
    {
        AppLanguage.ZhHans => "zh_cn",
        AppLanguage.ZhHant => RegionInfo.CurrentRegion is { TwoLetterISORegionName: "HK" } ? "zh_hk" : "zh_tw",
        _ => "en_us",
    };

    public static AppLanguage Parse(string? rawValue) => rawValue switch
    {
        "zh-Hans" => AppLanguage.ZhHans,
        "zh-Hant" => AppLanguage.ZhHant,
        "en" => AppLanguage.En,
        _ => System,
    };

    public static AppLanguage System
    {
        get
        {
            var name = CultureInfo.CurrentUICulture.Name;
            if (name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("zh-MO", StringComparison.OrdinalIgnoreCase)) return AppLanguage.ZhHant;
            if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return AppLanguage.ZhHans;
            return AppLanguage.En;
        }
    }
}

public sealed class Localizer
{
    public static readonly Localizer Shared = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _hantCache = new();
    private AppLanguage _language = AppLanguageExtensions.System;

    public AppLanguage Language
    {
        get { lock (_gate) return _language; }
        set { lock (_gate) _language = value; }
    }

    private static readonly (string From, string To)[] Variants =
    {
        ("啓", "啟"), ("爲", "為"), ("裏", "裡"), ("綫", "線"), ("衆", "眾"), ("僞", "偽"), ("峯", "峰"), ("脣", "唇"), ("羣", "群"),
        ("檔案夾", "資料夾"), ("文件夾", "資料夾"), ("默認", "預設"), ("設置", "設定"), ("信息", "資訊"), ("賬號", "帳號"), ("內存", "記憶體"),
        ("網絡", "網路"), ("緩存", "快取"), ("加載", "載入"), ("程序", "程式"), ("視頻", "視訊"), ("屏幕", "螢幕"), ("文件", "檔案"),
    };

    private const uint LCMAP_TRADITIONAL_CHINESE = 0x04000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int LCMapStringEx(string? lpLocaleName, uint dwMapFlags, string lpSrcStr, int cchSrc,
        StringBuilder lpDestStr, int cchDest, IntPtr lpVersionInformation, IntPtr lpReserved, IntPtr lParam);

    public string Traditional(string s)
    {
        lock (_gate)
        {
            if (_hantCache.TryGetValue(s, out var cached)) return cached;
            var sb = new StringBuilder(Math.Max(s.Length + 16, 64));
            // LCMapStringEx 的简→繁转换对应 macOS 的 Hans-Hant 变换
            var r = LCMapStringEx(null, LCMAP_TRADITIONAL_CHINESE, s, s.Length, sb, sb.Capacity,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            // 转换输出使用部分旧字形，替换为台湾常用写法
            var result = r > 0 ? sb.ToString() : s;
            foreach (var (from, to) in Variants) result = result.Replace(from, to);
            _hantCache[s] = result;
            return result;
        }
    }
}

/// 双语文案：T("中文", "English")
public static class Localization
{
    public static string T(string zh, string en) => Localizer.Shared.Language switch
    {
        AppLanguage.ZhHans => zh,
        AppLanguage.ZhHant => Localizer.Shared.Traditional(zh),
        _ => en,
    };
}

public static class T
{
    public static string Invoke(string zh, string en) => Localization.T(zh, en);
}

/// 应用信息
public static class AppInfo
{
    /// 应用名称。注意：按《PCL 存储库合理使用指南》，不得使用会与 PCL 混淆的名称。
    public const string ShortName = "Kiln";
    public const string Author = "NelsonPence";

    public static string Version =>
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) is { Length: > 0 } v ? v : "0.2.10";
}
