using Kiln.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

public sealed class ImportPage : Page
{
    private readonly StackPanel _list = new() { Spacing = 8 };
    private readonly TextBlock _status = Ui.Text("", 12);
    private bool _alive;
    private static string T(string zh, string en) => Localization.T(zh, en);
    public ImportPage()
    {
        Content = Ui.Scroll(Ui.Header(T("从其他启动器导入", "Import from other launchers")),
            Ui.Card(T("选择来源", "Choose a source"), Ui.Row(
                Ui.Button(T("选择实例文件夹…", "Choose instance folder…"), async (_, _) => await ChooseFolder()),
                Ui.Button(T("选择实例压缩包…", "Choose instance archive…"), async (_, _) => await ChooseArchive()),
                Ui.Button(T("安装整合包文件…", "Install modpack file…"), async (_, _) => await ChooseModpack(XamlRoot)),
                Ui.Button(T("从 PCL2 继承配置…", "Inherit PCL2 settings…"), async (_, _) => await App.Pcl2TakeOverDialog(MainWindow.Current!, true)))),
            _status, Ui.Card(T("自动发现", "Discovered instances"), _list));
        Loaded += async (_, _) => { _alive = true; await Discover(); }; Unloaded += (_, _) => _alive = false;
    }
    private async Task Discover()
    {
        _status.Text = T("正在查找其他启动器…", "Looking for other launchers…");
        try
        {
            var candidates = await Task.Run(() => LauncherImport.Discover()); if (!_alive) return;
            _list.Children.Clear();
            foreach (var candidate in candidates)
            {
                var summary = Ui.Col(Ui.Text(candidate.Name + " · " + candidate.Source.DisplayName(), 13, FontWeights.SemiBold), Ui.Text(candidate.Unsupported ?? candidate.Summary + (candidate.Warning == null ? "" : "\n" + candidate.Warning), 11.5));
                var import = Ui.Button(T("导入…", "Import…"), async (_, _) => await Import(candidate)); import.IsEnabled = candidate.Unsupported == null;
                _list.Children.Add(Ui.Row(summary, import));
            }
            _status.Text = candidates.Count == 0 ? T("没有自动发现实例，也可以手动选择文件夹或压缩包。", "No instances discovered. Choose a folder or archive manually.") : "";
        }
        catch (Exception e) { if (_alive) _status.Text = e.Friendly(); }
    }
    private async Task ChooseFolder()
    {
        var picker = new KilnFolderPicker();  picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync(); if (folder == null) return;
        try { var candidate = LauncherImport.Inspect(folder.Path) ?? throw new LauncherException(T("无法识别这个实例文件夹", "This instance folder wasn't recognized")); await Import(candidate); }
        catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
    }
    private async Task ChooseArchive()
    {
        var picker = new KilnFileOpenPicker();  picker.FileTypeFilter.Add(".zip");
        var file = await picker.PickSingleFileAsync(); if (file == null) return;
        try { await Import(LauncherImport.InspectArchive(file.Path)); } catch (Exception e) { await Ui.ShowError(XamlRoot, e); }
    }
    private async Task Import(LauncherImport.Candidate candidate)
    {
        if (candidate.Unsupported != null) { await Ui.ShowError(XamlRoot, candidate.Unsupported); return; }
        var (ok, name) = await Ui.Prompt(XamlRoot, T("导入版本", "Import instance"), T("版本名称", "Instance name"), LauncherImport.AvailableName(candidate.Name, LauncherSettings.Shared.MinecraftFolder));
        if (!ok) return;
        var job = MainWindow.NewJob(T($"导入 {name}", $"Import {name}"));
        try { await LauncherImport.Run(candidate, name, LauncherSettings.Shared.MinecraftFolder, job); job.State = Job.JobState.Success; _status.Text = T("导入完成", "Imported"); }
        catch (OperationCanceledException) { job.State = Job.JobState.Cancelled; }
        catch (Exception e) { job.State = Job.JobState.Failed; job.Error = e.Friendly(); await Ui.ShowError(XamlRoot, e); }
    }
    public static async Task ChooseModpack(XamlRoot root)
    {
        var picker = new KilnFileOpenPicker();  picker.FileTypeFilter.Add(".mrpack"); picker.FileTypeFilter.Add(".zip");
        var file = await picker.PickSingleFileAsync(); if (file != null) await InstallModpack(root, file.Path);
    }
    public static async Task InstallModpack(XamlRoot root, string archive)
    {
        Job? job = null;
        try
        {
            var info = Modpack.Inspect(archive);
            var (ok, name) = await Ui.Prompt(root, T($"安装整合包 {info.Name}", $"Install modpack {info.Name}"), T("版本名称", "Instance name"), LauncherImport.AvailableName(info.Name, LauncherSettings.Shared.MinecraftFolder)); if (!ok) return;
            job = MainWindow.NewJob(T($"安装 {info.Name}", $"Install {info.Name}"));
            var manual = await Modpack.Install(archive, name, LauncherSettings.Shared.MinecraftFolder, job);
            job.State = Job.JobState.Success;
            await ManualDownloads(root, manual);
        }
        catch (OperationCanceledException) { if (job != null) job.State = Job.JobState.Cancelled; }
        catch (Exception e) { if (job != null) { job.State = Job.JobState.Failed; job.Error = e.Friendly(); } await Ui.ShowError(root, e); }
    }
    private static async Task ManualDownloads(XamlRoot root, IReadOnlyList<Modpack.ManualFile> files)
    {
        var remaining = Modpack.CollectManualDownloads(files);
        while (remaining.Count > 0)
        {
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(Ui.Text(T("作者禁止第三方下载的文件需手动获取，下载后点击重新检查。", "These files need manual downloads. Download them, then check again."), 12));
            foreach (var file in remaining)
            {
                var button = Ui.Button(file.Title, async (_, _) => { if (file.PageUrl is { } page) await Windows.System.Launcher.LaunchUriAsync(new Uri(page)); }); panel.Children.Add(button);
            }
            var dialog = new KilnDialog { XamlRoot = root, Title = T("需要手动下载", "Manual downloads required"), Content = new ScrollViewer { Content = panel, MaxHeight = 340 }, PrimaryButtonText = T("重新检查下载文件夹", "Check Downloads again"), CloseButtonText = T("稍后处理", "Later") };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            remaining = Modpack.CollectManualDownloads(remaining);
        }
    }
}
