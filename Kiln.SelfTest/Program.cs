using Kiln.Core;

// Kiln Windows 版自检（对应原版 --self-test 与 --launch-test）：
// --launch-test <版本> <目录> [Fabric]：安装并启动游戏（会短暂打开游戏窗口）
// 无参数：PCL2 检测/接管/验证 + Java 搜索

if (args.Length >= 2 && args[0] == "--launch-test")
{
    return await LaunchTest(args[1], args[2], args.Length > 3 ? args[3] : null);
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
