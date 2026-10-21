using Kiln.App.Pages;
using Kiln.Core;
using Microsoft.UI.Xaml;

namespace Kiln.App;

/// All entry points share account overrides, task state, process tracking and window behavior.
internal static class LaunchCoordinator
{
    public static async Task Launch(McInstance instance, LaunchOptions options)
    {
        if (MainWindow.LaunchJob != null)
            throw new LauncherException(Localization.T("已有版本正在准备启动，请等待或取消当前启动任务。", "An instance is already preparing to launch. Wait or cancel that task."));
        var account = AccountStore.Shared.AccountFor(instance.Setup)
            ?? throw new LauncherException(Localization.T("请先添加账号。", "Add an account first."));
        var window = MainWindow.Current!;
        var job = MainWindow.NewJob(options.ExportScript == null
            ? Localization.T($"启动 {instance.Name}", $"Play {instance.Name}")
            : Localization.T($"导出 {instance.Name} 启动脚本", $"Export launch script for {instance.Name}"), silent: true);
        MainWindow.LaunchJob = job;
        try
        {
            var game = await Launcher.Launch(instance, account, options, job, new LaunchCallbacks
            {
                OnWindow = process => window.DispatcherQueue.TryEnqueue(() =>
                {
                    if (process.State == GameProcess.GameState.Running) ApplyVisibility();
                }),
                OnExit = process => window.DispatcherQueue.TryEnqueue(async () =>
                {
                    RestoreVisibility();
                    if (process.State != GameProcess.GameState.Crashed) return;
                    SystemIntegration.Notify("Kiln", Localization.T($"{instance.Name} 崩溃了", $"{instance.Name} crashed"), "log");
                    var finding = CrashAnalyzer.Analyze(process.Lines, instance.GameDir);
                    await Ui.ShowError(window.Content.XamlRoot,
                        (finding?.Reason ?? Localization.T("游戏崩溃了。", "The game crashed.")) + (finding?.Detail is { Length: > 0 } detail ? "\n" + detail : ""));
                }),
            });
            if (game != null) MainWindow.Games.Insert(0, game);
            job.State = Job.JobState.Success;
        }
        catch (OperationCanceledException) { job.State = Job.JobState.Cancelled; throw; }
        catch (Exception e) { job.Fail(e.Friendly()); throw; }
        finally { MainWindow.LaunchJob = null; }
    }

    private static void ApplyVisibility()
    {
        var window = MainWindow.Current!;
        switch (LauncherSettings.Shared.Visibility)
        {
            case VisibilityMode.CloseOnStart: Application.Current.Exit(); break;
            case VisibilityMode.HideAndClose or VisibilityMode.HideAndReopen: window.AppWindow.Hide(); break;
            case VisibilityMode.Minimize: (window.AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter)?.Minimize(); break;
        }
    }

    private static void RestoreVisibility()
    {
        if (LauncherSettings.Shared.Visibility == VisibilityMode.HideAndClose) Application.Current.Exit();
        else if (LauncherSettings.Shared.Visibility is VisibilityMode.HideAndReopen or VisibilityMode.Minimize) MainWindow.Current!.AppWindow.Show();
    }
}
