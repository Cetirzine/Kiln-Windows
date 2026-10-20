using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App;

public partial class App : Application
{
    public static Window? MainAppWindow { get; private set; }
    /// kiln://launch/<版本名> 激活时要启动的版本
    public static string? PendingLaunchVersion { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.log($"[UI] 未处理异常：{e.Exception.Friendly()}");
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();
        MainAppWindow = window;
        window.Activate();
        // kiln:// 深链接：kiln://launch/<版本名> 直接启动
        var launchVersion = SystemIntegration.ParseLaunchUri(Environment.GetCommandLineArgs().Skip(1).ToArray());
        if (launchVersion != null)
        {
            PendingLaunchVersion = launchVersion;
            Log.log($"[Launch] 通过 kiln:// 链接启动：{launchVersion}");
        }
        // 注册 kiln:// 协议 + 自动检查更新
        SystemIntegration.RegisterProtocol();
        _ = window.DispatcherQueue.TryEnqueue(async () =>
        {
            await Pcl2TakeOverDialog(window);
            await SystemIntegration.AutoUpdateCheck(window);
            await HandlePendingLaunch();
        });
        // 后台刷新 Mod 中文名表
        _ = Task.Run(async () => await ModNames.Shared.Refresh());
    }

    /// 处理 kiln:// 启动请求
    public static async Task HandlePendingLaunch()
    {
        var version = PendingLaunchVersion;
        if (version == null) return;
        PendingLaunchVersion = null;
        try
        {
            var instances = InstanceScanner.Scan(LauncherSettings.Shared.MinecraftFolder);
            var inst = instances.FirstOrDefault(i => string.Equals(i.Name, version, StringComparison.OrdinalIgnoreCase));
            if (inst == null) throw new LauncherException(Localization.T($"找不到版本 {version}", $"Instance {version} wasn't found"));
            var account = AccountStore.Shared.Selected ?? AccountStore.Shared.AccountFor(inst.Setup);
            if (account == null) throw new LauncherException(Localization.T("请先在启动器中登录账号。", "Sign in inside Kiln first."));
            LauncherSettings.Shared.SelectedInstance = inst.Name;
            MainAppWindow!.DispatcherQueue.TryEnqueue(() => ((MainWindow)MainAppWindow).ReloadCurrentPage());
            var job = MainWindow.NewJob(Localization.T($"启动 {inst.Name}", $"Play {inst.Name}"));
            await Launcher.Launch(inst, account, new LaunchOptions(), job, new LaunchCallbacks
            {
                OnExit = g => MainAppWindow!.DispatcherQueue.TryEnqueue(() =>
                    SystemIntegration.Notify("Kiln",
                        g.State == GameProcess.GameState.Crashed
                            ? Localization.T($"{inst.Name} 崩溃了，点击启动器查看。", $"{inst.Name} crashed.")
                            : "")),
            });
        }
        catch (Exception e)
        {
            await ShowInfo(MainAppWindow!, e.Friendly());
        }
    }

    private static async Task Pcl2TakeOverDialog(MainWindow window)
    {
        try
        {
            if (LauncherSettings.Shared.Pcl2TakenOver) return;
            var pcl = await Task.Run(() => Pcl2.Detect());
            if (pcl == null) return;
            var dialog = new ContentDialog
            {
                XamlRoot = window.Content.XamlRoot,
                Title = "PCL2",
                Content = Pcl2.DialogText(pcl),
                PrimaryButtonText = Localization.T("继承配置", "Inherit settings"),
                CloseButtonText = Localization.T("暂不", "Not now"),
                DefaultButton = ContentDialogButton.Primary,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var r = Pcl2.TakeOver(pcl);
                Log.log($"[PCL2] 接管完成：{string.Join("；", r.Applied)}");
                window.ReloadCurrentPage();
                await ShowInfo(window, Localization.T(
                    $"已继承 PCL2 的配置：{r.VersionCount} 个版本位于 {r.GameFolder}。账号需要在 Kiln 中重新登录。",
                    $"Inherited PCL2 settings: {r.VersionCount} versions at {r.GameFolder}. Sign in again inside Kiln."));
            }
        }
        catch (Exception e)
        {
            Log.log($"[PCL2] 接管询问失败：{e.Friendly()}");
        }
    }

    public static async Task ShowInfo(Window window, string message, string? title = null)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = window.Content.XamlRoot,
            Title = title ?? AppInfo.ShortName,
            Content = message,
            CloseButtonText = Localization.T("好", "OK"),
        };
        await dialog.ShowAsync();
    }
}
