using Kiln.Core;
using System.Xml.Linq;

namespace Kiln.App;

internal static class SystemUiChecks
{
    internal static async Task Run(MainWindow window, string fixtureRoot, Action<string, bool> check)
    {
        var wait = System.Diagnostics.Stopwatch.StartNew();
        while (window.Taskbar?.Ready != true && wait.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(30);
        check("Taskbar button creation is observed before native progress calls", window.Taskbar?.Ready == true);
        var completed = new List<(Job Job, JobFeedback? Feedback)>();
        void Observe(Job job, JobFeedback? feedback) => completed.Add((job, feedback));
        window.JobCompleted += Observe;
        var first = MainWindow.NewJob("Synthetic task A"); var second = MainWindow.NewJob("Synthetic task B");
        try
        {
            first.FileFraction = .2; second.FileFraction = .8; window.RefreshJobIndicator();
            check("Windows taskbar accepts combined progress and task count badge", window.Taskbar is { LastError: null, Applied: { Count: 2, Fraction: .5 } });
            first.State = Job.JobState.Success; first.State = Job.JobState.Success; second.Fail("Synthetic failure detail");
            await Task.Delay(60);
            check("Task completion feedback fires once and retains failure details", completed.Count == 2 && completed.Single(c => c.Job == first).Feedback?.Kind == HintKind.Success && completed.Single(c => c.Job == second).Feedback?.Body == "Synthetic failure detail");
            check("Windows taskbar clears progress and badge after completion", window.Taskbar is { LastError: null, Applied: { Count: 0 } });
            var silent = MainWindow.NewJob("Silent launch fixture", silent: true); var cancel = MainWindow.NewJob("Cancelled fixture");
            silent.State = Job.JobState.Success; cancel.State = Job.JobState.Cancelled; await Task.Delay(60);
            check("Silent and cancelled tasks finish without notification feedback", completed.Count == 4 && completed.Where(c => c.Job == silent || c.Job == cancel).All(c => c.Feedback == null));
        }
        finally { first.State = second.State = Job.JobState.Cancelled; window.JobCompleted -= Observe; MainWindow.ClearFinishedJobs(); window.RefreshJobIndicator(); }
        var xml = XDocument.Parse(DesktopNotifications.Xml("<Title> & 中文", "Details & <body>\n下一行", "kiln://log"));
        check("Toast content preserves text and has a protocol action", xml.Root?.Attribute("activationType")?.Value == "protocol" && xml.Root.Attribute("launch")?.Value == "kiln://log" && xml.Descendants("text").Select(t => t.Value).SequenceEqual(new[] { "<Title> & 中文", "Details & <body>\n下一行" }));
        var shortcut = Path.Combine(fixtureRoot, "Notification registration.lnk");
        ShellShortcuts.Write(shortcut, Environment.ProcessPath!, "", Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"), "Fixture", "Kiln.Notification.Fixture", DesktopNotifications.Activator);
        var registration = ShellShortcuts.Read(shortcut);
        check("Native shortcut preserves notification identity and stub activator CLSID", registration.AppId == "Kiln.Notification.Fixture" && registration.ToastActivator == DesktopNotifications.Activator);
        foreach (var state in new[] { GameProcess.GameState.Starting, GameProcess.GameState.Running })
        {
            try { AppUpdates.EnsureIdle(new[] { new GameProcess("Busy update fixture") { State = state } }); check("Updater refuses " + state + " games", false); }
            catch (LauncherException) { check("Updater refuses " + state + " games", true); }
        }
        AppUpdates.EnsureIdle(new[] { new GameProcess("Exited update fixture") { State = GameProcess.GameState.Exited } });
        check("Update notes follow the original plain-text limit", AppUpdates.PlainNotes("# 标题\n**修复** [链接](https://example.invalid) `code`\n---\nEnglish") == "标题\n修复 链接 code" && AppUpdates.PlainNotes(new string('中', 701)).Length == 701);
    }
}
