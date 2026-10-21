using Kiln.Core;
using Kiln.Core.DotScene;
using System.Diagnostics;

// Kiln Windows 版自检（对应原版 --self-test 与 --launch-test）：
// --launch-test <版本> <目录> [Fabric]：安装并启动游戏（会短暂打开游戏窗口）
// --scene-videos <输出目录> [ffmpeg路径]：把每个已移植的场景渲染成 MP4（8 秒 @30fps）
// 无参数：PCL2 检测/接管/验证 + Java 搜索

if (args.Length >= 2 && args[0] == "--java-command-fixture") return await JavaTests.Child(args[1], args.Length >= 3 ? args[2] : null);
if (args.Length >= 3 && args[0] == "-cp" && args[2] == "Kiln.ProcessorFixture") return await JavaTests.ProcessorChild(args[1], args.Skip(3).ToArray());
if (args.SequenceEqual(new[] { "-XshowSettings:properties", "-version" })) return Environment.GetEnvironmentVariable("KILN_JAVA_FIXTURE") == "properties" ? await JavaTests.Child("properties", null) : 64;
if (args.Length >= 2 && args[0] == "--game-smoke") return await GameSmoke.Start(args[1], args.Length >= 3 ? args[2] : "1.21.1", args.Length >= 4 ? args[3] : "Vanilla");
if (args.Length >= 4 && args[0] == "--game-smoke-resume") return await GameSmoke.Resume(args[1], args[2], args[3]);
if (args.Length >= 4 && args[0] == "--game-smoke-worker") return await GameSmoke.Worker(args[1], args[2], args[3]);
if (args.Length > 0 && args[0] == "--parity-test") return await ParityTests.Run();
if (args.Length > 0 && args[0] == "--network-smoke") return await NetworkSmoke.Run();
if (args.Length >= 2 && args[0] == "--updater-regression") return await UpdaterVerification.Regression(args[1]);
if (args.Length >= 3 && args[0] == "--updater-live") return await UpdaterVerification.Live(args[1], args[2]);
if (args.Length >= 2 && args[0] == "--updater-worker") return await UpdaterVerification.Worker(args[1]);
if (args.Length >= 2 && args[0] == "--updater-check") return await UpdaterVerification.CheckOnly(args[1]);

if (args.Length >= 3 && args[0] == "--launch-test")
{
    return await LaunchTest(args[1], args[2], args.Length > 3 ? args[3] : null);
}

if (args.Length >= 2 && args[0] == "--scene-videos")
{
    return SceneVideos(args[1], args.Length > 2 ? args[2] : @"D:\Kiln for windows\tools\ffmpeg-master-latest-win64-gpl\bin\ffmpeg.exe");
}

if (args.Length > 0) { Console.Error.WriteLine("Unknown diagnostic mode; no migration was performed."); return 64; }

int SceneVideos(string outDir, string ffmpeg)
{
    Directory.CreateDirectory(outDir);
    // 与主页一致：宽度 250 个点；渲染注册表中的每个场景各一次（按 Id 去重）
    const int cols = 250, rows = 88, cell = 4, fps = 30, seconds = 8;
    var width = cols * cell;
    var height = rows * cell;
    var versions = new[]
    {
        "1.0", "1.2", "1.3", "1.4", "1.5", "1.6", "1.7", "1.8", "1.9", "1.10", "1.11", "1.12", "1.13",
        "1.14", "1.15", "1.16", "1.17", "1.18", "1.19", "1.20", "1.21", "1.21.4", "1.21.5", "1.21.6",
        "1.21.9", "1.21.11", "26.1", "26.2", "26.3", "26.4", "unknown",
    };
    var scenes = versions
        .Select(v => (Version: v, Scene: DotScenes.SceneFor(v)))
        .GroupBy(x => x.Scene.Id)
        .Select(g => (Label: g.First().Version, Scene: g.First().Scene))
        .ToList();
    var ok = 0;
    foreach (var (label, scene) in scenes)
    {
        var file = Path.Combine(outDir, $"{scene.Id}.mp4");
        Console.WriteLine($"渲染 {label} → {file}");
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = $"-y -f rawvideo -pix_fmt bgr24 -s {width}x{height} -r {fps} -i - " +
                        $"-c:v libx264 -preset fast -crf 20 -pix_fmt yuv420p \"{file}\"",
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var ffmpegProc = Process.Start(psi)!;
        var stderrTask = ffmpegProc.StandardError.ReadToEndAsync();
        var staticBuf = new DotBuffer(cols, rows);
        scene.PaintStatic(staticBuf);
        for (var frame = 0; frame < fps * seconds; frame++)
        {
            var t = (double)frame / fps;
            var dyn = new DotBuffer(cols, rows);
            scene.PaintDynamic(dyn, t, new SceneCast());
            for (var i = 0; i < 14; i++)
                if (Math.Sin(t * 3 + i * 1.7) > 0.93)
                    dyn.Set((int)(DotMath.Noise(i, (int)(t / 2), 71) * cols), (int)(DotMath.Noise(i, (int)(t / 2), 72) * rows * 0.6), 0xFFFFFFu);
            var composed = new DotBuffer(cols, rows);
            Array.Copy(staticBuf.Px, composed.Px, composed.Px.Length);
            for (var i = 0; i < dyn.Px.Length; i++)
                if (dyn.Px[i] != 0) composed.Px[i] = dyn.Px[i];
            var bgr = DotRenderer.RenderBgr24(composed, cell, scene.Background);
            bgr = Glitch.Apply(bgr, width, rows, Glitch.Bands(t, rows));
            ffmpegProc.StandardInput.BaseStream.Write(bgr, 0, bgr.Length);
        }
        ffmpegProc.StandardInput.Close();
        ffmpegProc.WaitForExit(30000);
        if (ffmpegProc.ExitCode != 0)
            Console.WriteLine("  ffmpeg: " + stderrTask.Result[^Math.Min(600, stderrTask.Result.Length)..]);
        Console.WriteLine(File.Exists(file) ? $"  ✓ {file}（{new FileInfo(file).Length / 1024} KB）" : "  ✗ 渲染失败");
        if (File.Exists(file) && ffmpegProc.ExitCode == 0) ok++;
    }
    Console.WriteLine($"完成：{ok}/{scenes.Count}");
    return ok == scenes.Count ? 0 : 1;
}

var pass = 0;
var fail = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "[OK]" : "[FAIL]")} {name}{(detail.Length > 0 ? " — " + detail : "")}");
    if (ok) pass++; else fail++;
}

Console.WriteLine($"Kiln Windows 自检 {AppInfo.Version}");
Console.WriteLine($"数据目录：{Paths.Support}");
Console.WriteLine();

async Task<int> LaunchTest(string versionId, string dir, string? loader)
{
    Console.WriteLine($"Kiln 启动测试：{versionId}{(loader != null ? " " + loader : "")} → {dir}");
    var job = new Job("launch-test");
    var task = Task.Run(async () =>
    {
        try
        {
            var game = await Launcher.Launch(
                new McInstance(dir, versionId), OfflineAccount(),
                new LaunchOptions(), job,
                new LaunchCallbacks());
            var process = game!;
            var windowTcs = new TaskCompletionSource();
            process.StateChanged += s =>
            {
                Console.WriteLine($"  [game] {s}");
                if (s is GameProcess.GameState.Running or GameProcess.GameState.Crashed or GameProcess.GameState.Exited)
                    windowTcs.TrySetResult();
            };
            var timeout = Task.Delay(120_000);
            var finished = await Task.WhenAny(windowTcs.Task, timeout);
            if (finished == timeout)
            {
                Console.WriteLine("  [game] 120 秒内未检测到窗口，强制结束");
                process.Kill();
                return 1;
            }
            if (process.State == GameProcess.GameState.Running)
            {
                Console.WriteLine("  [game] 游戏窗口已出现，运行 10 秒后结束");
                await Task.Delay(10_000);
                process.Kill();
                await Task.Delay(3000);
                Console.WriteLine(process.State == GameProcess.GameState.Exited
                    ? "  [game] 已正常结束"
                    : "  [game] 已结束");
                return 0;
            }
            Console.WriteLine("  [game] 启动失败");
            foreach (var line in process.Lines.TakeLast(40)) Console.WriteLine("    " + line);
            return 1;
        }
        catch (Exception e)
        {
            Console.WriteLine($"  [game] 异常：{e.Friendly()}");
            return 1;
        }
    });
    // 进度显示
    var report = Task.Run(async () =>
    {
        while (!task.IsCompleted)
        {
            Console.WriteLine($"  {job.Stage} {(job.Fraction > 0 ? $"{job.Fraction * 100:0}% {job.FileText}" : "")}");
            await Task.Delay(2000);
        }
    });
    var code = await task;
    return code;

    Account OfflineAccount()
    {
        var name = "NelsonPence";
        return new Account { Kind = Account.AccountKind.Offline, Name = name, Uuid = Account.OfflineUuid(name) };
    }
}

// 1) 自动检测 PCL2（不传入任何路径，完全自动查找）
Console.WriteLine("== 1. 自动检测 PCL2 ==");
var detected = Pcl2.Detect();
if (detected != null)
{
    Console.WriteLine($"  根目录：{detected.Root}");
    Console.WriteLine($"  程序：{detected.ExePath ?? "（仅找到配置目录）"}");
    Console.WriteLine($"  游戏目录：{detected.GameFolder}");
    Console.WriteLine($"  版本（{detected.VersionNames.Count}）：{string.Join("、", detected.VersionNames)}");
    Check("自动检测到 PCL2", true, detected.Root);
    Check("检测到至少 1 个版本", detected.VersionNames.Count > 0, $"{detected.VersionNames.Count} 个");
}
else
{
    Check("自动检测到 PCL2", false, "未找到（本机应安装有 PCL2）");
    Console.WriteLine();
    Console.WriteLine($"结果：{pass} 项通过，{fail} 项失败");
    return fail == 0 ? 0 : 1;
}
Console.WriteLine();

// 2) 启动弹窗文案
Console.WriteLine("== 2. 启动弹窗文案 ==");
var dialog = Pcl2.DialogText(detected);
Console.WriteLine($"  「{dialog}」");
Check("弹窗文案包含 PCL2 与继承/Inherit", dialog.Contains("PCL2") && (dialog.Contains("继承") || dialog.Contains("Inherit")));
Console.WriteLine();

// 3) 执行接管
Console.WriteLine("== 3. 执行接管 ==");
var result = Pcl2.TakeOver(detected);
foreach (var a in result.Applied) Console.WriteLine($"  + {a}");
foreach (var s in result.Skipped) Console.WriteLine($"  - {s}");
Check("接管完成", result.VersionCount > 0, $"接管 {result.VersionCount} 个版本");
Console.WriteLine();

// 4) 验证设置与实例扫描
Console.WriteLine("== 4. 验证 ==");
var settings = LauncherSettings.Shared;
Check("游戏目录已接管", string.Equals(settings.MinecraftFolder, detected.GameFolder, StringComparison.OrdinalIgnoreCase),
    settings.MinecraftFolder);
var setupJson = Path.Combine(detected.GameFolder, "versions", detected.VersionNames[0], ".kiln", "setup.json");
Check("版本独立设置已写入（.kiln/setup.json）", File.Exists(setupJson), setupJson);
var instances = InstanceScanner.Scan(settings.MinecraftFolder);
Console.WriteLine($"  扫描到 {instances.Count} 个实例：");
foreach (var i in instances)
    Console.WriteLine($"    {i.Name} — {i.Description}（隔离：{(i.Isolated ? "是" : "否")}，Java {i.JavaMajor}）");
Check("实例扫描数量一致", instances.Count == detected.VersionNames.Count,
    $"{instances.Count} / {detected.VersionNames.Count}");
Check("加载器识别（Fabric）", instances.Any(i => i.Loaders.Contains("Fabric")));
Check("加载器识别（Forge）", instances.Any(i => i.Loaders.Contains("Forge")));
Console.WriteLine();

// 5) Java 搜索冒烟测试（PCL2 与官方启动器共用 Mojang 运行时）
Console.WriteLine("== 5. Java 搜索 ==");
var javas = await JavaManager.Search();
foreach (var j in javas) Console.WriteLine($"    {j.DisplayName} — {j.Path}");
Check("找到至少一个 Java", javas.Count > 0, $"{javas.Count} 个");
var need = instances.Select(i => i.JavaMajor).Distinct().ToList();
if (javas.Count > 0)
    Console.WriteLine($"  版本需求 Java：{string.Join("、", need)}");

Console.WriteLine();
Console.WriteLine($"结果：{pass} 项通过，{fail} 项失败");
return fail == 0 ? 0 : 1;


