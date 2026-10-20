using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App;

public partial class App : Application
{
    public static Window? MainAppWindow { get; private set; }

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
        // 启动时的接管提示（PCL2）：已检测到您当前环境有PCL2，是否继承其配置？
        _ = window.DispatcherQueue.TryEnqueue(async () => await Pcl2TakeOverDialog(window));
        // 后台刷新 Mod 中文名表
        _ = Task.Run(async () => await ModNames.Shared.Refresh());
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
