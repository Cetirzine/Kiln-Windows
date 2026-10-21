using Kiln.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class JavaTests
{
    internal static async Task Run(Action<string, bool> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "kiln-java-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            foreach (var (input, expected) in new[] { ("AMD64", "x86_64"), ("x64", "x86_64"), ("x86_64", "x86_64"), ("i386", "x86"), ("i686", "x86"), ("x86", "x86"), ("aarch64", "arm64"), ("unknown", "unknown") })
                check("Java architecture is identified accurately: " + input, JavaManager.NormalizeArch(input) == expected);
            check("32-bit Java is not labelled x64", new JavaInfo("", "1.8.0_431", 8, "x86", false).DisplayName.EndsWith("(x86)"));
            check("Managed runtime paths require a directory boundary", JavaManager.IsUnder(Path.Combine(root, "runtime", "bin", "java.exe"), Path.Combine(root, "runtime")) && !JavaManager.IsUnder(Path.Combine(root, "runtime-other", "bin", "java.exe"), Path.Combine(root, "runtime")));
            var releaseHome = Path.Combine(root, "release-fixture"); Directory.CreateDirectory(Path.Combine(releaseHome, "bin"));
            var releaseExe = Path.Combine(releaseHome, "bin", "java.exe"); File.WriteAllText(releaseExe, "Metadata fixture, never executed");
            File.WriteAllText(Path.Combine(releaseHome, "release"), "JAVA_VERSION=\"1.8.0_431\"\nOS_ARCH=\"i386\"\n");
            var release = JavaManager.Inspect(releaseExe);
            check("Java release metadata preserves x86 architecture and Java 8 version", release is { Major: 8, Arch: "x86" });
            Directory.CreateDirectory(Path.Combine(releaseHome, "bin", "server")); File.WriteAllText(Path.Combine(releaseHome, "bin", "server", "classes.jsa"), "Fixture archive");
            check("Windows base CDS archive is recognized in bin/server", release!.HasBaseCDS);
            var versionDir = Path.Combine(root, "versions", "Native variants"); Directory.CreateDirectory(versionDir);
            File.WriteAllText(Path.Combine(versionDir, "Native variants.json"), """
                {"id":"Native variants","mainClass":"fixture","libraries":[
                  {"name":"org.lwjgl:lwjgl:3.3.3","downloads":{"artifact":{"url":"https://example.invalid/common.jar"}}},
                  {"name":"org.lwjgl:lwjgl:3.3.3:natives-windows","downloads":{"artifact":{"url":"https://example.invalid/x64.jar"}}},
                  {"name":"org.lwjgl:lwjgl:3.3.3:natives-windows-x86","downloads":{"artifact":{"url":"https://example.invalid/x86.jar"}}},
                  {"name":"org.lwjgl:lwjgl:3.3.3:natives-windows-arm64","downloads":{"artifact":{"url":"https://example.invalid/arm.jar"}}},
                  {"name":"legacy:legacy:1","natives":{"windows":"natives-windows-${arch}"},"downloads":{"classifiers":{"natives-windows-32":{"url":"https://example.invalid/legacy32.jar"},"natives-windows-64":{"url":"https://example.invalid/legacy64.jar"}}}}
                ]}
                """);
            var nativeInstance = new McInstance(root, "Native variants");
            foreach (var architecture in new[] { "x86_64", "x86" })
            {
                var libraries = nativeInstance.Libraries(new RuleContext(architecture));
                check("Only the selected Windows native architecture is used: " + architecture, libraries.Count == 3 && libraries.Where(l => l.NativeArch != null).All(l => l.NativeArch == architecture));
                check("Legacy native classifier matches Java bitness: " + architecture, libraries.Single(l => l.IsNatives).Url!.Contains(architecture == "x86" ? "legacy32" : "legacy64"));
            }
            var host = Environment.ProcessPath!;
            string[] Arguments(string mode, string? receipt = null) => (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? new[] { typeof(JavaTests).Assembly.Location } : Array.Empty<string>())
                .Concat(new[] { "--java-command-fixture", mode }).Concat(receipt == null ? Array.Empty<string>() : new[] { receipt }).ToArray();
            var windowFixture = new GameProcess("Window notification fixture"); var notifications = 0;
            Parallel.For(0, 20, _ => windowFixture.WindowReady(_ => Interlocked.Increment(ref notifications)));
            check("Concurrent window observations notify exactly once", notifications == 1 && windowFixture.State == GameProcess.GameState.Running);
            windowFixture.State = GameProcess.GameState.Exited;
            check("A late window observation cannot resurrect an exited game", !windowFixture.WindowReady(_ => notifications++) && notifications == 1);
            var callbackFixture = new GameProcess("Window callback lock fixture"); var outsideLock = false;
            callbackFixture.WindowReady(p => outsideLock = Task.Run(() => p.State).Wait(TimeSpan.FromSeconds(1)));
            check("Window callbacks run outside the game state lock", outsideLock);
            var exit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var falseWindows = 0;
            var windowless = Launcher.Run(nativeInstance, new JavaInfo(host, "21", 21, "x86_64", false), Arguments("windowless"), new LoginResult("Fixture", "fixture", "fixture", "legacy"),
                new LaunchCallbacks { OnWindow = _ => Interlocked.Increment(ref falseWindows), OnExit = _ => exit.TrySetResult() });
            try
            {
                await exit.Task.WaitAsync(TimeSpan.FromSeconds(30));
                check("Startup logs and twenty elapsed seconds do not fabricate a game window", falseWindows == 0 && windowless.State == GameProcess.GameState.Crashed && windowless.ExitCode == 7);
                check("Windowless game startup retains its actual diagnostic output", windowless.Lines.Any(l => l.Contains("Setting user:")) && windowless.Lines.Any(l => l.Contains("Sound engine started")));
            }
            finally
            {
                if (windowless.Process is { HasExited: false }) { windowless.Kill(); await windowless.Process.WaitForExitAsync(); }
                windowless.Process?.Dispose();
                if (windowless.LogFilePath is { } ownLog && JavaManager.IsUnder(ownLog, Paths.Logs) && Path.GetFileName(ownLog).Contains(windowless.Id.ToString("N"))) File.Delete(ownLog);
            }
            var pipes = await JavaCommand.Run(host, Arguments("properties"), TimeSpan.FromSeconds(8));
            check("Java process drains large stdout and stderr concurrently", pipes.ExitCode == 0 && pipes.Output.Contains("java.version = 21.0.8") && pipes.Output.Contains("os.arch = amd64") && pipes.Output.Length < 270000);
            var nonzero = await JavaCommand.Run(host, Arguments("nonzero"), TimeSpan.FromSeconds(5));
            check("Java command records a nonzero exit without reporting success", nonzero.ExitCode == 7);
            var timedReceipt = Path.Combine(root, "timeout.pid"); var elapsed = Stopwatch.StartNew(); var timedOut = false;
            try { await JavaCommand.Run(host, Arguments("hang", timedReceipt), TimeSpan.FromSeconds(1)); } catch (TimeoutException) { timedOut = true; }
            check("Hung Java probes time out and their process is terminated", timedOut && elapsed.Elapsed < TimeSpan.FromSeconds(6) && await Dead(timedReceipt));
            var cancelReceipt = Path.Combine(root, "cancel.pid"); using var cancellation = new CancellationTokenSource();
            var pending = JavaCommand.Run(host, Arguments("hang", cancelReceipt), TimeSpan.FromSeconds(10), cancellation.Token);
            elapsed.Restart(); while (!File.Exists(cancelReceipt) && elapsed.Elapsed < TimeSpan.FromSeconds(4)) await Task.Delay(20);
            cancellation.Cancel(); var cancelled = false; try { await pending; } catch (OperationCanceledException) { cancelled = true; }
            check("Cancelling Java preparation terminates its owned process", cancelled && await Dead(cancelReceipt));

            // Exercise Inspect's executable path using an apphost copy, not a shell script or the user's Java.
            var bin = Path.Combine(root, "中文 [probe] O'Brien", "bin"); Directory.CreateDirectory(bin);
            foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory)) File.Copy(file, Path.Combine(bin, Path.GetFileName(file)));
            var fixtureExe = Path.Combine(bin, "java.exe"); File.Copy(Path.Combine(AppContext.BaseDirectory, "Kiln.SelfTest.exe"), fixtureExe);
            var priorFixture = Environment.GetEnvironmentVariable("KILN_JAVA_FIXTURE"); var priorDotnet = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            try
            {
                Environment.SetEnvironmentVariable("KILN_JAVA_FIXTURE", "properties");
                Environment.SetEnvironmentVariable("DOTNET_ROOT", Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..")));
                var inspected = await JavaManager.InspectAsync(fixtureExe);
                check("Actual executable inspection supports spaces, brackets, Unicode and both pipes", inspected is { Major: 21, Version: "21.0.8", Arch: "x86_64" } && inspected.Path == fixtureExe);
                var processorReceipt = Path.Combine(root, "processor-arguments.json");
                var processorArgs = new[] { "argument with spaces", "中文 [验收] O'Brien", "literal\"quote", "trailing slash\\", "" };
                var processorClasspath = Path.Combine(bin, "first.jar") + ";" + Path.Combine(bin, "second.jar");
                await ForgeInstaller.RunProcessor(fixtureExe, processorClasspath, "Kiln.ProcessorFixture", new[] { "arguments", processorReceipt }.Concat(processorArgs).ToArray(), bin, default);
                using (var received = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(processorReceipt)))
                    check("Forge processors drain both pipes and preserve exact arguments and working directory",
                        received.RootElement.GetProperty("classpath").GetString() == processorClasspath && received.RootElement.GetProperty("cwd").GetString() == bin && received.RootElement.GetProperty("arguments").EnumerateArray().Select(v => v.GetString()).SequenceEqual(processorArgs));
                var processorFailed = false;
                try { await ForgeInstaller.RunProcessor(fixtureExe, processorClasspath, "Kiln.ProcessorFixture", new[] { "failure" }, bin, default); }
                catch (LauncherException error) { processorFailed = error.Message.Contains("Broken Java fixture"); }
                check("Forge processor failure reports its actual diagnostic output", processorFailed);
                var processorPid = Path.Combine(root, "processor-cancel.pid"); using var processorStop = new CancellationTokenSource();
                var processorTask = ForgeInstaller.RunProcessor(fixtureExe, processorClasspath, "Kiln.ProcessorFixture", new[] { "cancel", processorPid }, bin, processorStop.Token);
                elapsed.Restart(); while (!File.Exists(processorPid) && elapsed.Elapsed < TimeSpan.FromSeconds(4)) await Task.Delay(20);
                processorStop.Cancel(); var processorCancelled = false;
                try { await processorTask; } catch (OperationCanceledException) { processorCancelled = true; }
                check("Cancelling a Forge processor ends its owned Java process", processorCancelled && await Dead(processorPid));
            }
            finally { Environment.SetEnvironmentVariable("KILN_JAVA_FIXTURE", priorFixture); Environment.SetEnvironmentVariable("DOTNET_ROOT", priorDotnet); }
        }
        finally
        {
            if (!Path.GetFileName(root).StartsWith("kiln-java-", StringComparison.Ordinal) || !Path.GetFullPath(root).StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid Java fixture cleanup path");
            Directory.Delete(root, true);
        }
        static async Task<bool> Dead(string receipt)
        {
            if (!File.Exists(receipt)) return false;
            var pid = int.Parse(await File.ReadAllTextAsync(receipt));
            try { using var process = Process.GetProcessById(pid); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); return process.HasExited; } catch (ArgumentException) { return true; }
        }
    }
    internal static async Task<int> Child(string mode, string? receipt)
    {
        if (mode == "hang") { if (receipt != null) await File.WriteAllTextAsync(receipt, Environment.ProcessId.ToString()); await Task.Delay(Timeout.InfiniteTimeSpan); return 0; }
        if (mode == "nonzero") { Console.Error.WriteLine("Broken Java fixture"); return 7; }
        if (mode == "windowless")
        {
            Console.WriteLine("Setting user: WindowlessFixture"); Console.Error.WriteLine("LWJGL Version: fixture");
            Console.WriteLine("Sound engine started"); await Task.Delay(TimeSpan.FromSeconds(21)); return 7;
        }
        if (mode != "properties") return 64;
        var block = new string('x', 1023) + "\n";
        async Task Flood(TextWriter writer) { for (var i = 0; i < 512; i++) await writer.WriteAsync(block); await writer.FlushAsync(); }
        await Task.WhenAll(Flood(Console.Out), Flood(Console.Error));
        Console.Out.WriteLine("java.version = 21.0.8"); Console.Error.WriteLine("os.arch = amd64"); return 0;
    }
    internal static async Task<int> ProcessorChild(string classpath, string[] args)
    {
        if (args.Length == 0) return 64;
        if (args[0] == "failure") return await Child("nonzero", null);
        if (args.Length < 2) return 64;
        if (args[0] == "cancel") return await Child("hang", args[1]);
        if (args[0] != "arguments") return 64;
        await File.WriteAllTextAsync(args[1], System.Text.Json.JsonSerializer.Serialize(new { classpath, cwd = Environment.CurrentDirectory, arguments = args.Skip(2) }));
        return await Child("properties", null);
    }
}
