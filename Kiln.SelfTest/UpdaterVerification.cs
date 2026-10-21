using Kiln.Core;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

internal static class UpdaterVerification
{
    internal static async Task<int> CheckOnly(string previousVersion)
    {
        var release = await Updater.Check(previousVersion);
        var current = await Updater.Check();
        var website = await Updater.CheckWebsite(previousVersion);
        var passed = release?.Version == AppInfo.Version && website?.Version == AppInfo.Version && release.Sha256?.Length == 64 && release.Sha256 == website.Sha256 && current == null;
        Console.WriteLine(JsonSerializer.Serialize(new { passed, current = AppInfo.Version, from = previousVersion, offered = release?.Version, digest = release?.Sha256, bytes = release?.Size, notesLength = release?.Notes.Length, currentVersionUpToDate = current == null, officialWebsite = website?.Version }));
        return passed ? 0 : 1;
    }

    internal static async Task<int> Regression(string output)
    {
        var root = Path.Combine(Path.GetFullPath(output), "update-regression-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var results = new List<object>(); var failures = 0;
        void Check(string name, bool passed, object? detail = null)
        {
            results.Add(new { name, passed, detail }); if (!passed) failures++;
            File.WriteAllText(Path.Combine(root, "report.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"[{(passed ? "OK" : "FAIL")}] {name}");
        }
        // This PID was verified absent; the script must not wait for this controller.
        const int absentPid = int.MaxValue;
        try { using var unexpected = Process.GetProcessById(absentPid); throw new InvalidOperationException("Diagnostic PID is occupied."); } catch (ArgumentException) { }
        var parent = Path.Combine(root, "missing-stage"); Directory.CreateDirectory(parent);
        var app = Path.Combine(parent, "Kiln"); var staged = Path.Combine(parent, ".Kiln-update-missing"); Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "old-marker.txt"), "Preserve the old launcher on failure.");
        var script = Updater.ReplaceScript(staged, app, absentPid, relaunch: false, logPath: Path.Combine(parent, "update.log"));
        // The generated script's recursive operations are confined to this new fixture.
        foreach (var target in new[] { app, staged, Path.Combine(parent, ".Kiln-previous") })
            if (!Path.GetFullPath(target).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Fixture escaped its root.");
        var run = await Execute(script, parent);
        Check("Failed replacement preserves the existing launcher", run.ExitCode != 0 && File.Exists(Path.Combine(app, "old-marker.txt")), new { run.ExitCode, run.Output });
        foreach (var (label, locked, badLaunch) in new[] { ("中文 [备份] O'Brien", false, false), ("locked-stage", true, false), ("invalid-executable", false, true) })
        {
            parent = Path.Combine(root, label); app = Path.Combine(parent, "Kiln [app]"); staged = Path.Combine(parent, ".Kiln-update-fixture");
            Directory.CreateDirectory(app); Directory.CreateDirectory(staged);
            File.WriteAllText(Path.Combine(app, "old-marker.txt"), "Old launcher"); File.WriteAllText(Path.Combine(app, "Kiln.exe"), "Old fixture");
            File.WriteAllText(Path.Combine(staged, "new-marker.txt"), "New launcher"); File.WriteAllText(Path.Combine(staged, "Kiln.exe"), "New fixture");
            var unrelated = Path.Combine(parent, ".Kiln-previous"); Directory.CreateDirectory(unrelated); File.WriteAllText(Path.Combine(unrelated, "keep.txt"), "Unrelated backup");
            using var held = locked ? File.Open(Path.Combine(staged, "Kiln.exe"), FileMode.Open, FileAccess.Read, FileShare.None) : null;
            run = await Execute(Updater.ReplaceScript(staged, app, absentPid, relaunch: badLaunch, logPath: Path.Combine(parent, "update.log")), parent);
            var good = locked || badLaunch ? run.ExitCode != 0 && File.Exists(Path.Combine(app, "old-marker.txt")) : run.ExitCode == 0 && File.Exists(Path.Combine(app, "new-marker.txt")) && !File.Exists(Path.Combine(app, "old-marker.txt"));
            Check(locked ? "Locked staged files roll back to the old launcher" : badLaunch ? "Failed executable startup rolls back to the old launcher" : "Replacement supports Chinese, apostrophes and literal brackets", good, new { run.ExitCode, run.Output });
            Check("Replacement preserves an unrelated existing backup: " + label, File.Exists(Path.Combine(unrelated, "keep.txt")));
        }
        try { Updater.ReplaceScript(Path.Combine(root, "not-an-update"), app, absentPid, false); Check("Replacement rejects non-owned staged paths", false); }
        catch (ArgumentException) { Check("Replacement rejects non-owned staged paths", true); }
        var integrity = Path.Combine(root, "integrity"); Directory.CreateDirectory(integrity); var integrityApp = Path.Combine(integrity, "Kiln"); Directory.CreateDirectory(integrityApp);
        File.WriteAllText(Path.Combine(integrityApp, "old-marker.txt"), "Old launcher");
        using (var server = new PayloadServer(Encoding.UTF8.GetBytes("This is a damaged update archive.")))
        {
            try { await Updater.Prepare(new("0.2.14w", "", "", server.Url, "payload.zip", new string('0', 64), server.Length), integrityApp); Check("Damaged update is rejected before replacing any old files", false); }
            catch (LauncherException error) { Check("Damaged update is rejected before replacing any old files", error.Message.Contains("SHA-256", StringComparison.Ordinal) && File.Exists(Path.Combine(integrityApp, "old-marker.txt")) && !Directory.EnumerateDirectories(integrity, ".Kiln-update-*").Any()); }
        }
        using (var server = new PayloadServer(new byte[1024 * 1024], slow: true))
        {
            var job = new Job("Cancelled update fixture"); var preparing = Updater.Prepare(new("0.2.14w", "", "", server.Url, "payload.zip", new string('a', 64), server.Length), integrityApp, job);
            await Task.Delay(100); job.Cancel();
            try { await preparing; Check("Cancelling an update preserves the current installation", false); }
            catch (OperationCanceledException) { Check("Cancelling an update preserves the current installation", File.Exists(Path.Combine(integrityApp, "old-marker.txt")) && !Directory.EnumerateDirectories(integrity, ".Kiln-update-*").Any()); }
        }
        using (var server = new PayloadServer(new byte[128], pauseBeforeBody: TimeSpan.FromSeconds(10)))
        {
            var counter = new DownloadCounter();
            try { await DownloadEngine.Shared.Download(new Uri(server.Url), counter, default, idleTimeout: TimeSpan.FromMilliseconds(150)); Check("Stalled response body times out and permits download retry", false); }
            catch (TimeoutException) { Check("Stalled response body times out and permits download retry", counter.Bytes == 0); }
        }
        Console.WriteLine("Report: " + Path.Combine(root, "report.json"));
        return failures == 0 ? 0 : 1;
    }

    private sealed class PayloadServer : IDisposable
    {
        private readonly System.Net.Sockets.TcpListener _listener = new(System.Net.IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        internal string Url { get; }
        internal long Length { get; }
        internal PayloadServer(byte[] body, bool slow = false, TimeSpan? pauseBeforeBody = null)
        {
            Length = body.Length; _listener.Start(); Url = "http://127.0.0.1:" + ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port + "/update.zip";
            _ = Serve();
            async Task Serve()
            {
                try
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token); await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                    var lines = 0;
                    while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 }) if (++lines > 100) throw new IOException("Unexpected fixture request.");
                    await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"), _stop.Token);
                    if (pauseBeforeBody is { } pause) await Task.Delay(pause, _stop.Token);
                    for (var i = 0; i < body.Length; i += 4096) { await stream.WriteAsync(body.AsMemory(i, Math.Min(4096, body.Length - i)), _stop.Token); if (slow) await Task.Delay(20, _stop.Token); }
                }
                catch (Exception e) when (e is OperationCanceledException or IOException or System.Net.Sockets.SocketException) { }
            }
        }
        public void Dispose() { _stop.Cancel(); _listener.Stop(); _stop.Dispose(); }
    }

    internal sealed record Manifest(string Root, string App, string Staged, string Report, string Log, string Receipt);
    internal static async Task<int> Worker(string file)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(file))!;
        if (!Path.GetFileName(manifest.Root).StartsWith("update-live-", StringComparison.Ordinal) || !new[] { manifest.App, manifest.Staged, manifest.Report, manifest.Log, manifest.Receipt }.All(p => Path.GetFullPath(p).StartsWith(Path.GetFullPath(manifest.Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Expected an isolated update fixture.");
        using var lockedOldExe = File.Open(Path.Combine(manifest.App, "Kiln.exe"), FileMode.Open, FileAccess.Read, FileShare.Read);
        using var helper = Updater.LaunchReplacement(manifest.Staged, manifest.App, arguments: new[] { "--ui-smoke-test", manifest.Report, "--exit-after-test" }, logPath: manifest.Log);
        await File.WriteAllTextAsync(manifest.Receipt, JsonSerializer.Serialize(new { pid = helper.Id, owner = Environment.ProcessId }));
        await Task.Delay(1000); return 0;
    }

    internal static async Task<int> Live(string output, string oldSource)
    {
        var root = Path.Combine(Path.GetFullPath(output), "update-live-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var results = new List<object>();
        void Check(string name, bool passed, object? detail = null)
        {
            results.Add(new { name, passed, detail }); File.WriteAllText(Path.Combine(root, "report.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"[{(passed ? "OK" : "FAIL")}] {name}"); if (!passed) throw new InvalidOperationException(name);
        }
        try
        {
            var release = await Updater.Check("0.2.12w");
            Check("Live GitHub latest release is discovered from 0.2.12w", release is { Sha256.Length: 64, Size: > 0 }, release?.Version);
            Check("Current version does not offer a downgrade or repeat update", await Updater.Check() == null);
            var app = Path.Combine(root, "启动器 [验收] O'Brien", "Kiln"); Directory.CreateDirectory(app);
            foreach (var source in Directory.EnumerateFiles(oldSource, "*", SearchOption.AllDirectories))
            { var destination = Path.Combine(app, Path.GetRelativePath(oldSource, source)); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination); }
            File.WriteAllText(Path.Combine(app, "old-marker.txt"), "Old launcher copy"); File.WriteAllText(Path.Combine(app, "unins000.dat"), "Keep this installer record");
            Updater.CheckReplaceable(app);
            var job = new Job("Live isolated update"); var preparing = Updater.Prepare(release!, app, job); var previous = "";
            while (!preparing.IsCompleted) { if (job.Stage != previous) { previous = job.Stage; Console.WriteLine("Stage: " + previous); } await Task.Delay(200); }
            var staged = await preparing;
            Check("Official ZIP downloads, verifies and stages the released version", FileVersionInfo.GetVersionInfo(Path.Combine(staged, "Kiln.exe")).ProductVersion == release!.Version);
            Check("ZIP updates preserve the installed uninstaller record", File.ReadAllText(Path.Combine(staged, "unins000.dat")) == "Keep this installer record");
            var manifest = new Manifest(root, app, staged, Path.Combine(root, "relaunched-ui.json"), Path.Combine(root, "replacement.log"), Path.Combine(root, "helper.json"));
            var manifestFile = Path.Combine(root, "manifest.json"); await File.WriteAllTextAsync(manifestFile, JsonSerializer.Serialize(manifest));
            var host = Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
            var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { typeof(UpdaterVerification).Assembly.Location, "--updater-worker", manifestFile }) start.ArgumentList.Add(arg);
            using var owner = Process.Start(start)!; var wait = Stopwatch.StartNew();
            while (!File.Exists(manifest.Receipt) && !owner.HasExited && wait.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(30);
            var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(manifest.Receipt));
            using var helper = Process.GetProcessById(receipt.RootElement.GetProperty("pid").GetInt32());
            Check("Detached update helper waits while the old executable is locked", !helper.HasExited && File.Exists(Path.Combine(app, "old-marker.txt")));
            await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Check("Replacement succeeds after the owner exits", owner.ExitCode == 0 && helper.ExitCode == 0 && !File.Exists(Path.Combine(app, "old-marker.txt")), await File.ReadAllTextAsync(manifest.Log));
            Check("Installed executable has the published version", FileVersionInfo.GetVersionInfo(Path.Combine(app, "Kiln.exe")).ProductVersion == release!.Version);
            wait.Restart(); JsonElement[] checks = Array.Empty<JsonElement>();
            while (wait.Elapsed < TimeSpan.FromSeconds(90))
            {
                if (File.Exists(manifest.Report))
                {
                    try
                    {
                        await using var reportStream = new FileStream(manifest.Report, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var reportReader = new StreamReader(reportStream);
                        checks = JsonSerializer.Deserialize<JsonElement[]>(await reportReader.ReadToEndAsync())!;
                    }
                    catch (JsonException) { }
                    if (checks.Any(c => c.GetProperty("passed").GetBoolean() == false) || checks.LastOrDefault().ValueKind != JsonValueKind.Undefined && checks[^1].GetProperty("name").GetString() == "No unhandled UI exceptions during native regression") break;
                }
                await Task.Delay(200);
            }
            Check("New executable relaunches and completes its native UI checks", checks.Length >= 89 && checks.All(c => c.GetProperty("passed").GetBoolean()), new { count = checks.Length });
            Console.WriteLine("Report: " + Path.Combine(root, "report.json")); return 0;
        }
        catch (Exception error)
        {
            results.Add(new { name = "Live update verification error", passed = false, detail = error.ToString() });
            File.WriteAllText(Path.Combine(root, "report.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(error); Console.WriteLine("Report: " + Path.Combine(root, "report.json")); return 1;
        }
    }

    internal static async Task<(int ExitCode, string Output)> Execute(string script, string parent)
    {
        var file = Path.Combine(parent, "replace-test.ps1"); File.WriteAllText(file, script, new UTF8Encoding(true));
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", file }) start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!;
        var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
        catch { if (!child.HasExited) child.Kill(entireProcessTree: true); throw; }
        return (child.ExitCode, await stdout + await stderr);
    }
}
