using Kiln.Core;

internal static class SettingsTests
{
    internal static void Run(Action<string, bool> check)
    {
        var language = Localizer.Shared.Language;
        var first = LauncherSettings.Ephemeral(); var second = LauncherSettings.Ephemeral();
        first.MemoryAuto = false; first.MemoryGB = 7.5; first.WindowModeSetting = WindowMode.Custom; first.WindowWidth = 1600;
        first.JavaPath = "fixture-java"; first.DownloadThreads = 37; first.Language = AppLanguage.En;
        check("In-memory settings retain edited values", !first.MemoryAuto && first.MemoryGB == 7.5 && first.WindowModeSetting == WindowMode.Custom && first.WindowWidth == 1600 && first.DownloadThreads == 37 && first.JavaPath == "fixture-java");
        check("In-memory settings are isolated from each other", second.MemoryAuto && second.MemoryGB == 4 && second.WindowModeSetting == WindowMode.DefaultSize && second.WindowWidth == 854 && second.JavaPath.Length == 0);
        check("In-memory language does not mutate global localization", first.Language == AppLanguage.En && Localizer.Shared.Language == language);
        first.LauncherWindowPreset = 99; check("Window presets reject arbitrary out-of-range selections", first.LauncherWindowPreset == 2);
        check("Physical memory reports the actual Windows capacity", Diagnostics.PhysicalMemoryGB > 0 && Diagnostics.TotalPhysicalGB == (long)Diagnostics.PhysicalMemoryGB);
    }
}
