using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.International.Converters.PinYinConverter;

namespace Kiln.Core;

/// Mod 中文名表：显示中文名，并支持中文、拼音与拼音首字母搜索。
/// 数据为 Kiln 自己维护的 assets/mod-names-zh.json（键为 Modrinth slug），内置一份，并定期从仓库在线更新。
public sealed class ModNames
{
    public static readonly ModNames Shared = new();

    public sealed class Entry
    {
        public string Slug = "";
        public string Zh = "";
        public List<string> Aliases = new();
        /// 用于搜索的形式：中文名与别名（小写、简体）、完整拼音、拼音首字母
        public List<string> Terms = new();
        public string Pinyin = "";
        public string Initials = "";
    }

    private readonly object _gate = new();
    private Dictionary<string, Entry> _entries = new();
    private string _updated = "";

    private static readonly string[] RemoteUrls =
    {
        "https://cdn.jsdelivr.net/gh/Cetirzine/kiln-launcher@main/assets/mod-names-zh.json",
        "https://raw.githubusercontent.com/Cetirzine/kiln-launcher/main/assets/mod-names-zh.json",
    };

    private static string CacheFile => Path.Combine(Paths.Cache, "mod-names-zh.json");

    private ModNames()
    {
        Load(Encoding.UTF8.GetBytes(ModNamesEmbedded.Json));
        // 之前在线更新过的较新数据
        try
        {
            if (File.Exists(CacheFile)) Load(File.ReadAllBytes(CacheFile));
        }
        catch { }
    }

    /// 解析数据；只接受比当前更新的版本
    private bool Load(byte[] data)
    {
        Json j;
        try { j = Json.Parse(data); }
        catch { return false; }
        if (j["schema"].Int != 1 || j["updated"].String is not { } date) return false;
        lock (_gate)
        {
            if (string.CompareOrdinal(date, _updated) < 0) return false;
            var result = new Dictionary<string, Entry>();
            foreach (var (slug, v) in j["mods"].Object)
            {
                var zh = v["zh"].String;
                if (string.IsNullOrEmpty(zh)) continue;
                var aliases = v["aliases"].Array.Select(a => a.String ?? "").Where(s => s.Length > 0).ToList();
                var (py, ini) = Pinyin(zh);
                var terms = new[] { zh }.Concat(aliases).Select(Normalize).ToList();
                result[slug] = new Entry { Slug = slug, Zh = zh, Aliases = aliases, Terms = terms, Pinyin = py, Initials = ini };
            }
            if (result.Count == 0) return false;
            _entries = result;
            _updated = date;
            return true;
        }
    }

    /// 每天最多在线更新一次
    public async Task Refresh()
    {
        try
        {
            if (File.Exists(CacheFile)
                && (DateTime.UtcNow - File.GetLastWriteTimeUtc(CacheFile)).TotalSeconds < 24 * 3600) return;
        }
        catch { }
        byte[]? data = null;
        try { data = await Net.data(RemoteUrls, timeout: 10); }
        catch { }
        if (data == null) return;
        try
        {
            if (Load(data))
            {
                File.WriteAllBytes(CacheFile, data);
                Log.log($"[ModNames] 已更新中文名表：{_entries.Count} 条");
            }
            else
            {
                // 远端不比本地新，也记录检查时间
                File.WriteAllBytes(CacheFile, data);
            }
        }
        catch { }
    }

    /// Mod 的中文名
    public string? Chinese(string slug)
    {
        lock (_gate) return _entries.GetValueOrDefault(slug)?.Zh;
    }

    public int Count { get { lock (_gate) return _entries.Count; } }

    /// 按中文、别名、拼音或拼音首字母搜索，返回按匹配程度排序的 slug
    public IReadOnlyList<string> Search(string query)
    {
        var q = Normalize(query);
        if (q.Length == 0) return Array.Empty<string>();
        var cjk = ContainsCjk(q);
        var latin = q.Length > 0 && q.All(c => char.IsAsciiLetterOrDigit(c));
        List<Entry> all;
        lock (_gate) all = _entries.Values.ToList();
        var scored = new List<(string Slug, int Score)>();
        foreach (var e in all)
        {
            var score = 0;
            if (cjk)
            {
                foreach (var t in e.Terms)
                {
                    if (t == q) score = Math.Max(score, 100);
                    else if (t.StartsWith(q, StringComparison.Ordinal)) score = Math.Max(score, 80);
                    else if (t.Contains(q, StringComparison.Ordinal)) score = Math.Max(score, 60);
                }
            }
            else if (latin && q.Length >= 2)
            {
                if (e.Terms.Contains(q)) score = 100;                                          // 英文别名，如 ae2、dh
                else if (e.Initials == q) score = 90;                                          // 拼音首字母：jxdl
                else if (q.Length >= 3 && e.Initials.StartsWith(q, StringComparison.Ordinal)) score = 70;
                else if (q.Length >= 4 && e.Pinyin.StartsWith(q, StringComparison.Ordinal)) score = 75;   // 完整拼音：jixie
                else if (q.Length >= 4 && e.Pinyin.Contains(q, StringComparison.Ordinal)) score = 50;
            }
            if (score > 0) scored.Add((e.Slug, score));
        }
        return scored
            .OrderByDescending(s => s.Score).ThenBy(s => s.Slug, StringComparer.Ordinal)
            .Select(s => s.Slug).ToList();
    }

    // MARK: 工具

    public static bool ContainsCjk(string s) =>
        s.EnumerateRunes().Any(r => r.Value is >= 0x4E00 and <= 0x9FFF or >= 0x3400 and <= 0x4DBF);

    private const uint LCMAP_SIMPLIFIED_CHINESE = 0x02000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int LCMapStringEx(string? lpLocaleName, uint dwMapFlags, string lpSrcStr, int cchSrc,
        StringBuilder lpDestStr, int cchDest, IntPtr lpVersionInformation, IntPtr lpReserved, IntPtr lParam);

    /// 小写、去空白，繁体转简体
    public static string Normalize(string s)
    {
        var r = s.ToLowerInvariant().Trim();
        if (ContainsCjk(r))
        {
            var sb = new StringBuilder(Math.Max(r.Length + 16, 64));
            if (LCMapStringEx(null, LCMAP_SIMPLIFIED_CHINESE, r, r.Length, sb, sb.Capacity,
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) > 0)
                r = sb.ToString();
        }
        return r.Replace(" ", "");
    }

    /// 汉字转拼音，返回完整拼音与首字母，如（jixiedongli, jxdl）。
    /// 对应原版的 CFStringTransform(MandarinLatin + StripDiacritics)；Windows 使用微软拼音转换器。
    public static (string Pinyin, string Initials) Pinyin(string s)
    {
        var transformed = new StringBuilder();
        foreach (var c in s)
        {
            if (ContainsCjk(c.ToString()))
            {
                try
                {
                    var py = new ChineseChar(c).Pinyins.FirstOrDefault();
                    if (py != null) transformed.Append(StripTone(py));
                    else transformed.Append(c);
                }
                catch { transformed.Append(c); }
            }
            else
            {
                transformed.Append(c);
            }
        }
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var c in transformed.ToString().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) current.Append(c);
            else if (current.Length > 0) { words.Add(current.ToString()); current.Clear(); }
        }
        if (current.Length > 0) words.Add(current.ToString());
        return (string.Join("", words), new string(words.Select(w => w[0]).ToArray()));
    }

    private static string StripTone(string s) =>
        new string(s.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray()).Normalize(NormalizationForm.FormC);
}
