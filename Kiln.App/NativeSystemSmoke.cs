using Kiln.Core;
using Microsoft.Win32;
using System.Text.Json;
using Windows.UI.Notifications;

namespace Kiln.App;

// Isolated OS verification: temporary shortcut/protocol, no production registration or account access.
internal static class NativeSystemSmoke
{
    internal static async Task Run(MainWindow window, string report, string appId, bool waitForClick)
    {
        var results = new List<object>();
        void Write() => File.WriteAllText(report, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        void Check(string name, bool pass, object? detail = null) { results.Add(new { name, passed = pass, detail }); Write(); if (!pass) throw new InvalidOperationException(name); }
        var id = Guid.NewGuid().ToString("N"); var protocol = "kiln-check-" + id;
        var keyPath = @"Software\Classes\" + protocol;
        var shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Kiln-check-" + id + ".lnk");
        var fixtureRoot = Path.Combine(Path.GetTempPath(), "kiln-os-" + id); Directory.CreateDirectory(fixtureRoot);
        using var owner = new InstanceChannel("Kiln.Activation.Test." + id);
        DesktopNotifications? notifications = null;
        try
        {
            await SystemUiChecks.Run(window, fixtureRoot, (name, pass) => Check(name, pass));
            if (!owner.TryOwn()) throw new InvalidOperationException("OS test activation channel is occupied.");
            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.Listen(message => { window.DispatcherQueue.TryEnqueue(() => { if (LaunchLink.Parse(message)?.Action == LauncherAction.Tasks) window.NavigateTo("jobs"); received.TrySetResult(message); }); });
            using (var registration = Registry.CurrentUser.CreateSubKey(keyPath))
            {
                registration.SetValue("", "URL:Kiln local check"); registration.SetValue("URL Protocol", "");
                using var command = registration.CreateSubKey(@"shell\open\command");
                command.SetValue("", $"\"{Environment.ProcessPath}\" --activation-test-client \"Kiln.Activation.Test.{id}\" \"kiln://tasks\"");
            }
            notifications = new DesktopNotifications(appId, shortcut); notifications.Register();
            var toast = notifications.Show("任务已完成", "Kiln 系统检查：点击返回任务页", protocol + "://tasks", suppressPopup: !waitForClick);
            Check("Windows accepts the native toast", toast != null);
            Check("Windows notification permission is enabled for the isolated identity", notifications.Setting == NotificationSetting.Enabled, notifications.Setting.ToString());
            var wait = System.Diagnostics.Stopwatch.StartNew();
            bool stored = false;
            while (!stored && wait.Elapsed < TimeSpan.FromSeconds(5))
            {
                stored = ToastNotificationManager.History.GetHistory(appId).Any(t => t.Tag == toast!.Tag && t.Group == toast.Group);
                if (!stored) await Task.Delay(100);
            }
            Check("Notification remains in the Windows notification history", stored);
            if (waitForClick)
            {
                results.Add(new { name = "Waiting for the visible notification to be clicked", passed = true, detail = protocol }); Write();
                var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(30));
                Check("Clicking the Windows notification reaches the existing task window", message == "kiln://tasks");
            }
            else
            {
                var document = toast!.Content; var uri = document.DocumentElement.GetAttribute("launch");
                Check("Accepted toast protocol activates the existing task window", await Windows.System.Launcher.LaunchUriAsync(new Uri(uri)) && await received.Task.WaitAsync(TimeSpan.FromSeconds(5)) == "kiln://tasks");
            }
            Check("No unhandled UI exceptions during OS verification", App.DiagnosticErrors.Count == 0, App.DiagnosticErrors);
        }
        catch (Exception error) { results.Add(new { name = "Native system verification error", passed = false, detail = error.ToString() }); Write(); }
        finally
        {
            try { if (notifications != null) ToastNotificationManager.History.Clear(appId); } catch { }
            try { if (File.Exists(shortcut)) File.Delete(shortcut); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false); } catch { }
            try { if (appId.StartsWith("Cetirzine.Kiln.UI.", StringComparison.Ordinal)) Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\AppUserModelId\" + appId, throwOnMissingSubKey: false); } catch { }
            try { if (Path.GetFullPath(fixtureRoot).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Directory.Exists(fixtureRoot)) Directory.Delete(fixtureRoot, recursive: true); } catch { }
            window.SetDiagnosticsRunning(false);
        }
    }
}
