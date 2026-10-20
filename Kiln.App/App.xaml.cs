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
        SystemIntegration.InitializeAppIdentity();
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
            LauncherSettings.Shared.SelectedInstance = inst.Name;
            MainAppWindow!.DispatcherQueue.TryEnqueue(() => ((MainWindow)MainAppWindow).ReloadCurrentPage());
            await LaunchCoordinator.Launch(inst, new LaunchOptions());
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            await ShowInfo(MainAppWindow!, e.Friendly());
        }
    }

    public static async Task Pcl2TakeOverDialog(MainWindow window, bool userInitiated = false)
    {
        try
        {
            // Older self-tests wrote Pcl2TakenOver without ever showing the dialog.
            // Only an explicit UI confirmation counts as having seen this prompt.
            if (!userInitiated && LauncherSettings.Shared.Pcl2PromptConfirmed) return;
            var pcl = await Task.Run(() => Pcl2.Detect());
            if (pcl == null)
            {
                if (userInitiated) await ShowInfo(window, Localization.T("未自动检测到 PCL2，请在导入中手动选择文件夹。", "PCL2 wasn't found automatically. Choose its folder in Import."));
                return;
            }
            var dialog = new KilnDialog
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
                LauncherSettings.Shared.Pcl2PromptConfirmed = true;
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
        var dialog = new KilnDialog
        {
            XamlRoot = window.Content.XamlRoot,
            Title = title ?? AppInfo.ShortName,
            Content = message,
            CloseButtonText = Localization.T("好", "OK"),
        };
        await dialog.ShowAsync();
    }
}
