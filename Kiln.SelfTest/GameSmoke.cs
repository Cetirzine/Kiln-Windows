using Kiln.Core;
using System.Diagnostics;
using System.Text.Json;

internal static class GameSmoke
{
    internal static async Task<int> Start(string output, string version, string kind)
    {
        var root = Path.Combine(Path.GetFullPath(output), "kiln-game-smoke-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, ".kiln-game-smoke"), "Owned isolated installation, account, runtime and game test data.");
        return await Run(root, version, kind);
    }
    internal static Task<int> Resume(string root, string version, string kind)
    {
        root = Path.GetFullPath(root);
        if (!File.Exists(Path.Combine(root, ".kiln-game-smoke")) || !Path.GetFileName(root).StartsWith("kiln-game-smoke-", StringComparison.Ordinal)) throw new ArgumentException("Expected an existing owned game fixture");
        return Run(root, version, kind);
    }
    private static async Task<int> Run(string root, string version, string kind)
    {
        var modes = kind.Equals("all", StringComparison.OrdinalIgnoreCase) ? new[] { "Vanilla", "Fabric", "Quilt", "Forge", "NeoForge" } : new[] { kind };
        var failures = 0;
        foreach (var mode in modes)
        {
            if (!new[] { "Vanilla", "Fabric", "Quilt", "Forge", "NeoForge" }.Contains(mode, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Unknown loader: " + mode);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(GameSmoke).Assembly.Location);
            foreach (var arg in new[] { "--game-smoke-worker", root, version, mode }) start.ArgumentList.Add(arg);
            start.Environment["KILN_DIAGNOSTIC_ROOT"] = root;
            using var child = Process.Start(start) ?? throw new IOException("Cannot start isolated game verification");
            async Task Pump(StreamReader reader, TextWriter writer) { while (await reader.ReadLineAsync() is { } line) await writer.WriteLineAsync(line); }
            var stdout = Pump(child.StandardOutput, Console.Out); var stderr = Pump(child.StandardError, Console.Error);
            await child.WaitForExitAsync(); await Task.WhenAll(stdout, stderr); if (child.ExitCode != 0) failures++;
        }
        Console.WriteLine("Game verification root: " + root); return failures == 0 ? 0 : 1;
    }
    internal static async Task<int> Worker(string root, string versionId, string mode)
    {
        root = Path.GetFullPath(root);
        mode = new[] { "Vanilla", "Fabric", "Quilt", "Forge", "NeoForge" }.Single(m => m.Equals(mode, StringComparison.OrdinalIgnoreCase));
        if (!new[] { Paths.Support, Paths.Logs, Paths.Cache, Paths.Runtime, Paths.DefaultMinecraft, Paths.Downloads }.All(p => Path.GetFullPath(p).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Game diagnostics are not isolated");
        var results = new List<object>(); var report = Path.Combine(root, mode + "-report.json"); var job = new Job("Install and launch fixture");
        void Check(string name, bool passed, object? detail = null)
        {
            results.Add(new { name, passed, detail }); File.WriteAllText(report, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"[{(passed ? "OK" : "FAIL")}] {mode}: {name}"); if (!passed) throw new InvalidOperationException(name);
        }
        GameProcess? game = null;
        using var monitorStop = new CancellationTokenSource();
        var monitor = Task.Run(async () =>
        {
            var last = "";
            while (!monitorStop.IsCancellationRequested)
            {
                var next = $"{job.Stage} {(double.IsFinite(job.Fraction) ? job.Fraction : 0):P0} {job.FileText}";
                if (next != last) { last = next; Console.WriteLine(next); }
                File.WriteAllText(Path.Combine(root, mode + "-status.json"), JsonSerializer.Serialize(new { job.Stage, job.Fraction, job.FileText, job.Speed, gamePid = game?.Process?.Id }));
                try { await Task.Delay(2000, monitorStop.Token); } catch (OperationCanceledException) { }
            }
        });
        try
        {
            var settings = LauncherSettings.Shared;
            settings.MinecraftFolder = Path.Combine(root, "游戏 [验收] O'Brien", ".minecraft"); settings.ExtraFolders = new(); settings.Language = AppLanguage.En;
            settings.Visibility = VisibilityMode.Keep; settings.WindowModeSetting = WindowMode.DefaultSize; settings.MemoryAuto = false; settings.MemoryGB = 2; settings.SystemNotifications = false;
            Check("All launcher data paths are inside the owned fixture", true, new { Paths.Support, Paths.Runtime, Paths.Logs, Paths.Cache, settings.MinecraftFolder });
            var manifest = await VersionManifest.Fetch(); var version = manifest.Versions.Single(v => v.Id == versionId);
            var name = "Kiln-Smoke-" + versionId + "-" + mode; string? loader = null;
            if (File.Exists(Path.Combine(settings.MinecraftFolder, "versions", name, name + ".json")))
                await Installer.CompleteFiles(new McInstance(settings.MinecraftFolder, name), job);
            else if (mode.Equals("Fabric", StringComparison.OrdinalIgnoreCase) || mode.Equals("Quilt", StringComparison.OrdinalIgnoreCase))
            {
                var kind = mode.Equals("Fabric", StringComparison.OrdinalIgnoreCase) ? ModLoaderKind.Fabric : ModLoaderKind.Quilt;
                var available = await kind.Versions(versionId); loader = available.FirstOrDefault(v => v.Stable)?.Version ?? available.First().Version;
                await Installer.Install(version, name, (kind, loader), settings.MinecraftFolder, job);
            }
            else if (mode.Equals("Forge", StringComparison.OrdinalIgnoreCase) || mode.Equals("NeoForge", StringComparison.OrdinalIgnoreCase))
            {
                var kind = mode.Equals("Forge", StringComparison.OrdinalIgnoreCase) ? ForgeKind.Forge : ForgeKind.NeoForge;
                var available = await ForgeInstaller.Versions(kind, versionId); loader = available.FirstOrDefault(v => !v.IsBeta)?.Id ?? available.First().Id;
                await ForgeInstaller.Install(kind, loader, version, name, settings.MinecraftFolder, job);
            }
            else await Installer.Install(version, name, null, settings.MinecraftFolder, job);
            var instance = new McInstance(settings.MinecraftFolder, name);
            Check("Actual game installation completes with valid instance metadata", instance.Error == null, new { version = versionId, loader, instance.Dir, mainClass = instance.Json["mainClass"].String });
            var libraries = instance.Libraries(new RuleContext(instance.PreferredArch));
            Check("Launch library list excludes other Windows architectures", libraries.Where(l => l.NativeArch != null).All(l => l.NativeArch == instance.PreferredArch), new { count = libraries.Count });
            var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var windowCallbacks = 0; var callbackHadWindow = false;
            game = await Launcher.Launch(instance, new Account { Kind = Account.AccountKind.Offline, Name = "KilnSmoke", Uuid = Account.OfflineUuid("KilnSmoke") }, new LaunchOptions(), job,
                new LaunchCallbacks { OnExit = _ => exited.TrySetResult(), OnWindow = p => { p.Process!.Refresh(); callbackHadWindow = p.Process.MainWindowHandle != IntPtr.Zero; Interlocked.Increment(ref windowCallbacks); } });
            if (game?.Process == null) throw new InvalidOperationException("Launch did not start a game process");
            Check("Launch creates a Java process and keeps game data isolated", game.Process.Id != Environment.ProcessId && JavaManager.IsUnder(instance.GameDir, root), new { game.Process.Id, game.Java, game.LogFilePath });
            var wait = Stopwatch.StartNew(); DateTime? readyAt = null; long handle = 0;
            // The first Windows JDK 21 dynamic dump may spend minutes parsing the signed client manifest.
            while (wait.Elapsed < TimeSpan.FromSeconds(settings.ClassDataSharing ? 480 : 120))
            {
                if (game.Process.HasExited) throw new InvalidOperationException("Game exited before startup completed: " + game.ExitCode + "\n" + string.Join("\n", game.Lines.TakeLast(50)));
                game.Process.Refresh(); handle = game.Process.MainWindowHandle.ToInt64();
                if (handle != 0 && game.Lines.Any(l => l.Contains("Created:", StringComparison.Ordinal) && l.Contains("atlas", StringComparison.OrdinalIgnoreCase)))
                {
                    if (readyAt == null) { readyAt = DateTime.UtcNow; Console.WriteLine($"CLIENT_READY mode={mode} pid={game.Process.Id} hwnd={handle}"); }
                    if (DateTime.UtcNow - readyAt > TimeSpan.FromSeconds(Environment.GetEnvironmentVariable("KILN_GAME_VISUAL_HOLD") == "1" ? 60 : 20)) break;
                }
                await Task.Delay(200);
            }
            Check("Minecraft creates an actual window, loads texture atlases and stays alive", readyAt != null && handle != 0 && !game.Process.HasExited, new { handle, elapsed = wait.Elapsed.TotalSeconds, lines = game.Lines.TakeLast(12) });
            Check("Launcher reports running exactly once after a real Windows window exists", windowCallbacks == 1 && callbackHadWindow && game.State == GameProcess.GameState.Running);
            Check("Owned Minecraft window accepts a normal close request", game.Process.CloseMainWindow());
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(settings.ClassDataSharing ? 300 : 30));
            Check("Game exits normally without force termination", !game.Killed && game.ExitCode == 0 && game.State == GameProcess.GameState.Exited);
            if (settings.ClassDataSharing && game.Java?.HasBaseCDS == true && game.Java.Major is >= 19 and < 25)
                Check("Windows dynamic CDS archive is created on normal exit", Directory.EnumerateFiles(Path.Combine(instance.Dir, ".kiln"), "cds-*.jsa").Any());
            await Task.Delay(100);
            Check("Launch timestamps and play time are saved inside the fixture", instance.Setup.LastPlayed != null && instance.Setup.PlayTime > 0);
            Console.WriteLine("Report: " + report); return 0;
        }
        catch (Exception error)
        {
            results.Add(new { name = "Game verification failure", passed = false, detail = error.ToString() });
            File.WriteAllText(report, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine(error); Console.WriteLine("Report: " + report); return 1;
        }
        finally
        {
            if (game?.Process is { HasExited: false }) game.Kill(); monitorStop.Cancel(); await monitor;
        }
    }
}
