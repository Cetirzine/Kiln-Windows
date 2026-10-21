using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App;

public partial class App : Application
{
    public static Window? MainAppWindow { get; private set; }
    private InstanceChannel? _activationChannel;
    private ActivationDispatcher? _activationDispatcher;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.log($"[UI] 未处理异常：{e.Exception.Friendly()}");
            e.Handled = true;
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs();
        if (arguments.Length == 4 && arguments[1] == "--activation-test-client")
        {
            try
            {
                if (!arguments[2].StartsWith("Kiln.Activation.Test.", StringComparison.Ordinal)) throw new ArgumentException("Expected an isolated diagnostic channel.");
                using var probe = await SystemIntegration.ConnectActivation(arguments[2], arguments[3]);
                if (probe != null) Environment.ExitCode = 1;
            }
            catch { Environment.ExitCode = 1; }
            Exit(); return;
        }
        SystemIntegration.InitializeAppIdentity();
        if (arguments.Length >= 3 && arguments[1] == "--ui-smoke-test")
        {
            var diagnosticWindow = new MainWindow(diagnostics: true);
            MainAppWindow = diagnosticWindow;
            diagnosticWindow.Activate();
            diagnosticWindow.DispatcherQueue.TryEnqueue(async () => { await UiSmokeTest.Run(diagnosticWindow, arguments[2]); if (arguments.Contains("--exit-after-test")) diagnosticWindow.Close(); });
            return;
        }
        var message = arguments.Skip(1).FirstOrDefault(a => LaunchLink.Parse(a) != null) ?? "";
        InstanceChannel? channel;
        try
        {
            channel = await SystemIntegration.ConnectActivation(SystemIntegration.ActivationChannelName, message);
        }
        catch (Exception e)
        {
            Log.log("[Activation] " + e.Friendly());
            var errorWindow = new MainWindow(diagnostics: true); MainAppWindow = errorWindow;
            errorWindow.SetDiagnosticsRunning(false); errorWindow.Activate();
            errorWindow.DispatcherQueue.TryEnqueue(async () => { await ShowInfo(errorWindow, Localization.T("Kiln 已在运行，但未能将请求交给已有窗口。请切换到 Kiln 后重试。", "Kiln is already running, but the request could not be delivered. Switch to Kiln and retry.")); Exit(); });
            return;
        }
        if (channel == null) { Exit(); return; }
        _activationChannel = channel;
        var window = new MainWindow(); MainAppWindow = window;
        _activationDispatcher = new ActivationDispatcher(window);
        channel.Listen(received => { if (!window.DispatcherQueue.TryEnqueue(() => _activationDispatcher?.Receive(received))) throw new InvalidOperationException("The launcher window has closed."); });
        window.Closed += (_, _) => { _activationDispatcher.Dispose(); channel.Dispose(); };
        window.Activate(); _activationDispatcher.Receive(message);
        // 注册 kiln:// 协议 + 自动检查更新
        SystemIntegration.RegisterProtocol();
        _ = window.DispatcherQueue.TryEnqueue(async () =>
        {
            await Pcl2TakeOverDialog(window);
            _activationDispatcher.Ready();
            await SystemIntegration.AutoUpdateCheck(window);
        });
        // 后台刷新 Mod 中文名表
        _ = Task.Run(async () => await ModNames.Shared.Refresh());
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
