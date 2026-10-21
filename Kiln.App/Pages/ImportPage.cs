using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

public static class ImportPage
{
    public enum InstallResult { Cancelled, Failed, Installed }
    private static string T(string zh, string en) => Localization.T(zh, en);
    private static bool _showing;
    public static async Task ShowDialog(XamlRoot root)
    {
        if (_showing) return; _showing = true;
        var ownsGuard = true;
        LauncherImportForm? form = null;
        IReadOnlyList<LauncherImport.Candidate> selected = Array.Empty<LauncherImport.Candidate>();
        try
        {
            form = new LauncherImportForm(); string? modpack = null;
            var dialog = new KilnDialog { XamlRoot = root, Title = T("从其他启动器导入", "Import from other launchers"), Content = form, LeadingContent = form.ChooseButton, PreferredWidth = 560, SheetLayout = true, PrimaryButtonText = T("导入", "Import"), CloseButtonText = T("取消", "Cancel"), IsPrimaryButtonEnabled = false, DefaultButton = ContentDialogButton.Primary };
            form.Changed += () => { var count = form.Selected.Count; dialog.IsPrimaryButtonEnabled = count > 0; dialog.PrimaryButtonText = count == 0 ? T("导入", "Import") : T($"导入（{count}）", $"Import ({count})"); };
            form.ModpackRequested += path => { modpack = path; dialog.Hide(); };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary) selected = form.Selected;
            await form.Stop(selected);
            _showing = false; ownsGuard = false;
            if (modpack != null) { await InstallModpack(root, modpack); return; }
            if (selected.Count == 0) return;
            var folder = LauncherSettings.Shared.MinecraftFolder;
            var plan = LauncherImport.Plan(selected, folder);
            MainWindow.Current!.NavigateTo("jobs");
            await Task.WhenAll(plan.Select(async item =>
            {
                var job = MainWindow.NewJob(T($"导入 {item.Name}", $"Import {item.Name}"));
                try { await Task.Run(() => LauncherImport.Run(item.Candidate, item.Name, folder, job)); job.State = Job.JobState.Success; }
                catch (OperationCanceledException) { job.State = Job.JobState.Cancelled; }
                catch (Exception e) { job.State = Job.JobState.Failed; job.Error = e.Friendly(); }
            }));
        }
        catch (Exception e) { await Ui.ShowError(root, e); }
        finally { if (form != null) await form.Stop(selected); if (ownsGuard) _showing = false; }
    }
    public static async Task ChooseModpack(XamlRoot root)
    {
        var picker = new KilnFileOpenPicker();  picker.FileTypeFilter.Add(".mrpack"); picker.FileTypeFilter.Add(".zip");
        var file = await picker.PickSingleFileAsync(); if (file != null) await InstallModpack(root, file.Path);
    }
    public static async Task<InstallResult> InstallModpack(XamlRoot root, string archive)
    {
        Job? job = null;
        try
        {
            var info = await Task.Run(() => Modpack.Inspect(archive));
            var form = new ModpackInstallForm(info, LauncherSettings.Shared.MinecraftFolder);
            var dialog = new KilnDialog { XamlRoot = root, Title = T("安装整合包", "Install modpack"), Content = form, PreferredWidth = 480, SheetLayout = true, InitialFocus = form.InitialFocus, PrimaryButtonText = T("安装", "Install"), CloseButtonText = T("取消", "Cancel"), IsPrimaryButtonEnabled = form.IsValid, DefaultButton = ContentDialogButton.Primary };
            form.ValidationChanged += () => dialog.IsPrimaryButtonEnabled = form.IsValid;
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return InstallResult.Cancelled;
            var name = form.InstanceName;
            job = MainWindow.NewJob(T($"安装 {info.Name}", $"Install {info.Name}"));
            MainWindow.Current!.NavigateTo("jobs");
            var folder = LauncherSettings.Shared.MinecraftFolder;
            var manual = await Task.Run(() => Modpack.Install(archive, name, folder, job));
            LauncherSettings.Shared.SelectedInstance = name;
            job.State = Job.JobState.Success;
            await ManualDownloads(root, manual);
            return InstallResult.Installed;
        }
        catch (OperationCanceledException) { if (job != null) job.State = Job.JobState.Cancelled; return InstallResult.Cancelled; }
        catch (Exception e) { if (job != null) { job.State = Job.JobState.Failed; job.Error = e.Friendly(); } await Ui.ShowError(root, e); return InstallResult.Failed; }
    }
    private static async Task ManualDownloads(XamlRoot root, IReadOnlyList<Modpack.ManualFile> files)
    {
        if (files.Count == 0) return;
        var remaining = await Task.Run(() => Modpack.CollectManualDownloads(files)); if (remaining.Count == 0) return;
        var form = new ManualDownloadsForm(remaining);
        var dialog = new KilnDialog { XamlRoot = root, Title = "", Content = form, LeadingContent = form.Actions, PreferredWidth = 520, SheetLayout = true, CloseButtonText = T("稍后再说", "Later") };
        form.Changed += () => dialog.CloseButtonText = form.RemainingCount == 0 ? T("完成", "Done") : T("稍后再说", "Later");
        dialog.Closing += form.Stop;
        var shown = dialog.ShowAsync();
        form.Start();
        try { await shown; } finally { form.Stop(); }
    }
}
