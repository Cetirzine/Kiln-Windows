using System.Text.RegularExpressions;

namespace Kiln.Core;

/// 崩溃分析：在游戏输出与崩溃报告中查找常见原因（思路参考 HMCL 的崩溃分析规则）
public static class CrashAnalyzer
{
    public sealed record Finding(string Reason, string? Detail);

    private sealed class Rule
    {
        public string[] Patterns = Array.Empty<string>();
        public Func<string> Reason = () => "";
        /// 用于提取细节（如 Mod 名称）的正则，取第一个匹配到的捕获组
        public Regex? Detail;
    }

    private static readonly Rule[] Rules =
    {
        new()
        {
            Patterns = new[] { "Manually triggered debug crash" },
            Reason = () => T.Invoke("这是手动触发的调试崩溃（按住了 F3 + C），不是游戏出错。",
                "This crash was triggered on purpose by holding F3 + C; nothing is wrong with the game."),
        },
        // Forge / NeoForge 1.13+ 与 Fabric / Quilt 对 Minecraft 版本的依赖检查
        new()
        {
            Patterns = new[] { "Mod ID: 'minecraft', Requested by", "of 'Minecraft' (minecraft), but only the wrong version is present",
                "requires minecraft ", "which requires Minecraft " },
            Reason = () => T.Invoke("有 Mod 不适用于这个 Minecraft 版本。请换用对应版本的 Mod，或删除它。",
                "A mod is made for a different Minecraft version. Get the matching version of the mod or remove it."),
            Detail = new Regex(@"(?:Requested by: '([^']+)'|Mod '([^']+)'[^\n]*of 'Minecraft')"),
        },
        new()
        {
            Patterns = new[] { "' (java), but only the wrong version is present", "Mod ID: 'java', Requested by" },
            Reason = () => T.Invoke("有 Mod 需要更新的 Java。请在版本设置中将 Java 改为自动选择，或手动选择更新的 Java。",
                "A mod needs a newer Java. Set Java to automatic in the instance settings, or pick a newer Java."),
            Detail = new Regex(@"(?:Requested by: '([^']+)'|Mod '([^']+)'[^\n]*\(java\))"),
        },
        // Forge 1.12.2 及以前只能运行在 Java 8 上
        new()
        {
            Patterns = new[] { "cannot be cast to class java.net.URLClassLoader", "ClassLoaders$AppClassLoader cannot be cast to" },
            Reason = () => T.Invoke("这个版本只能使用 Java 8。请在版本设置中将 Java 改为自动选择。",
                "This version only runs on Java 8. Set Java to automatic in the instance settings."),
        },
        new()
        {
            Patterns = new[] { "UnsupportedClassVersionError", "has been compiled by a more recent version of the Java Runtime" },
            Reason = () => T.Invoke("Java 版本过低。请在版本设置中将 Java 改为自动选择。",
                "Java is too old. Set Java to automatic in the instance settings."),
        },
        new()
        {
            Patterns = new[] { "Could not reserve enough space", "Invalid maximum heap size", "There is insufficient memory for the Java Runtime" },
            Reason = () => T.Invoke("无法分配所需内存。请减少分配的内存。",
                "Couldn't allocate the requested memory. Lower the memory allocation."),
        },
        new()
        {
            Patterns = new[] { "java.lang.OutOfMemoryError", "GC overhead limit exceeded" },
            Reason = () => T.Invoke("内存不足。请增加分配的内存，或减少 Mod 与资源包。",
                "Out of memory. Allocate more memory or use fewer mods and resource packs."),
        },
        new()
        {
            Patterns = new[] { "Pixel format not accelerated", "GLFW error 65543", "GLFW error 65542", "Failed to create a GLFW window", "No OpenGL context" },
            Reason = () => T.Invoke("无法创建 OpenGL 窗口。请更新显卡驱动，或关闭光影与渲染类 Mod 后重试。",
                "Couldn't create an OpenGL window. Update your graphics driver, or disable shaders and rendering mods."),
        },
        new()
        {
            Patterns = new[] { "UnsatisfiedLinkError", "no lwjgl in java.library.path", "Failed to locate library: lwjgl", "Failed to locate library: lwjgl.dll" },
            Reason = () => T.Invoke("本地库与 Java 架构不匹配。请在设置中切换 Java 后重试。",
                "Native libraries don't match the Java architecture. Switch Java and try again."),
        },
        new()
        {
            Patterns = new[] { "DuplicateModsFoundException", "Found duplicate mods", "duplicate mod ID" },
            Reason = () => T.Invoke("安装了重复的 Mod。", "Duplicate mods are installed."),
            Detail = new Regex(@"(?:Mod ID|mod ID)[: ]+'?([\w\-]+)"),
        },
        new()
        {
            Patterns = new[] { "Incompatible mods found", "Mod resolution failed", "requires any version of", "requires version" },
            Reason = () => T.Invoke("Mod 缺少前置或版本不兼容。", "A mod is missing a dependency or is incompatible."),
            Detail = new Regex(@"Mod '([^']+)'"),
        },
        new()
        {
            Patterns = new[] { "MissingModsException", "Missing or unsupported mandatory dependencies" },
            Reason = () => T.Invoke("缺少前置 Mod。", "A required dependency mod is missing."),
            Detail = new Regex(@"Mod ID: '([^']+)'"),
        },
        new()
        {
            Patterns = new[] { "Mixin apply failed", "MixinApplyError", "InvalidInjectionException" },
            Reason = () => T.Invoke("某个 Mod 的 Mixin 注入失败，可能与当前版本或其他 Mod 不兼容。",
                "A mod's Mixin failed to apply; it may be incompatible with this version or another mod."),
            Detail = new Regex(@"from mod ([\w\-]+)"),
        },
        new()
        {
            Patterns = new[] { "java.lang.NoSuchMethodError", "java.lang.NoClassDefFoundError", "java.lang.ClassNotFoundException" },
            Reason = () => T.Invoke("缺少类或方法，通常是 Mod 与游戏或加载器版本不匹配。",
                "A class or method is missing; usually a mod doesn't match the game or loader version."),
            Detail = new Regex(@"(?:NoSuchMethodError|NoClassDefFoundError|ClassNotFoundException): ([\w./$]+)"),
        },
        new()
        {
            Patterns = new[] { "Unable to read jar", "ZipException", "Invalid or corrupt jarfile", "zip END header not found" },
            Reason = () => T.Invoke("有文件损坏。请在版本设置中“补全文件”，或重新下载 Mod。",
                "A file is corrupted. Use \"Repair files\" or re-download the mod."),
        },
        new()
        {
            Patterns = new[] { "No space left on device" },
            Reason = () => T.Invoke("磁盘空间不足。请清理磁盘后重试。",
                "The disk is full. Free up some space and try again."),
        },
        new()
        {
            Patterns = new[] { "A fatal error has been detected by the Java Runtime Environment" },
            Reason = () => T.Invoke("Java 虚拟机崩溃，通常与光影、渲染类 Mod 或本地库有关。可以导出诊断包查看 hs_err 报告。",
                "The Java VM crashed, usually because of shaders, rendering mods or native libraries. Export a diagnostics bundle to see the hs_err report."),
            Detail = new Regex(@"Problematic frame:\s*#\s*(.+)"),
        },
        new()
        {
            Patterns = new[] { "The directory is locked", "Failed to load level", "Exception reading", "level.dat" },
            Reason = () => T.Invoke("存档读取失败，可能是存档损坏或被占用。",
                "Failed to read a world; it may be corrupted or in use."),
        },
    };

    public static Finding? Analyze(IReadOnlyList<string> lines, string? gameDir = null)
    {
        var text = string.Join("\n", lines.Skip(Math.Max(0, lines.Count - 1500)));
        // 附加最新的崩溃报告
        try
        {
            var dir = Path.Combine(gameDir ?? "", "crash-reports");
            if (gameDir != null && Directory.Exists(dir))
            {
                var latest = Directory.EnumerateFiles(dir, "*.txt")
                    .OrderByDescending(f => File.GetLastWriteTimeUtc(f)).FirstOrDefault();
                if (latest != null
                    && (DateTime.UtcNow - File.GetLastWriteTimeUtc(latest)).TotalSeconds < 600)
                    text += "\n" + File.ReadAllText(latest);
            }
        }
        catch { }
        foreach (var rule in Rules)
        {
            if (!rule.Patterns.Any(text.Contains)) continue;
            string? detail = null;
            if (rule.Detail is { } re)
            {
                var m = re.Match(text);
                if (m.Success)
                    for (var g = 1; g < m.Groups.Count; g++)
                        if (m.Groups[g].Success)
                        {
                            detail = m.Groups[g].Value.Trim();
                            break;
                        }
            }
            return new Finding(rule.Reason(), detail);
        }
        return null;
    }
}
