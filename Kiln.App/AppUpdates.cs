using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.RegularExpressions;

namespace Kiln.App;

internal static class AppUpdates
{
    private static bool _checking;
    internal static async Task Check(MainWindow window, bool userInitiated)
    {
        var settings = LauncherSettings.Shared;
        if (!userInitiated && (!settings.AutoCheckUpdates || settings.LastUpdateCheck is { } last && (DateTime.UtcNow - last).TotalHours < 6)) return;
        if (_checking) return;
        _checking = true;
        try
        {
            var release = await Updater.Check(); settings.LastUpdateCheck = DateTime.UtcNow;
            if (release == null) { if (userInitiated) Hint.Show(Localization.T("已是最新版本", "You're up to date"), HintKind.Success); return; }
            var notes = PlainNotes(release.Notes);
            var dialog = new KilnDialog
            {
                XamlRoot = window.Content.XamlRoot,
                Title = Localization.T($"发现新版本 {release.Version}", $"Kiln {release.Version} is available"),
                Content = Localization.T($"当前版本 {AppInfo.Version}。", $"You have {AppInfo.Version}.") + (notes.Length == 0 ? "" : "\n\n" + notes),
                PrimaryButtonText = Localization.T("立即更新", "Update now"), SecondaryButtonText = Localization.T("查看发布页", "View release"), CloseButtonText = Localization.T("稍后", "Later"),
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary) await Install(release);
            else if (result == ContentDialogResult.Secondary) await Windows.System.Launcher.LaunchUriAsync(new Uri(release.PageUrl));
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Log.log("[Update] " + error.Friendly()); if (userInitiated) Hint.Show(error.Friendly(), HintKind.Error); }
        finally { _checking = false; }
    }

    internal static string PlainNotes(string markdown)
    {
        var text = markdown.Split("\n---", 2)[0];
        text = Regex.Replace(text, @"\*\*([^*]+)\*\*", "$1"); text = Regex.Replace(text, @"(?m)^#+\s*", ""); text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", "$1"); text = text.Replace("`", "").Trim();
        var elements = System.Globalization.StringInfo.ParseCombiningCharacters(text);
        return elements.Length > 700 ? text[..elements[700]] + "…" : text;
    }
    internal static void EnsureIdle(IEnumerable<GameProcess> games)
    {
        if (games.Any(g => g.State is GameProcess.GameState.Starting or GameProcess.GameState.Running))
            throw new LauncherException(Localization.T("请先退出正在运行的游戏，再更新启动器。", "Quit the running game before updating the launcher."));
    }
    private static void EnsureReady()
    {
        EnsureIdle(MainWindow.Games);
        if (MainWindow.LaunchJob?.State == Job.JobState.Running) throw new LauncherException(Localization.T("请先等待游戏启动完成，再更新启动器。", "Wait for the game to finish launching before updating."));
    }
    internal static async Task Install(Updater.Release release)
    {
        EnsureReady();
        var directory = Updater.CurrentApp ?? throw new LauncherException(Localization.T("无法定位程序目录。", "Couldn't locate the app directory."));
        Updater.CheckReplaceable(directory);
        var job = MainWindow.NewJob(Localization.T($"更新到 Kiln {release.Version}", $"Update to Kiln {release.Version}"));
        try
        {
            var staged = await Updater.Prepare(release, directory, job);
            EnsureReady();
            Updater.LaunchReplacement(staged, directory); job.State = Job.JobState.Success;
            Application.Current.Exit();
        }
        catch (OperationCanceledException) { job.State = Job.JobState.Cancelled; throw; }
        catch (Exception error) { job.Fail(error.Friendly()); throw; }
    }
}
