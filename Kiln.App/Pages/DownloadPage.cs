using Kiln.Core;
using Microsoft.UI.Xaml.Controls;

namespace Kiln.App.Pages;

public sealed class DownloadPage : Page
{
    public DownloadPage(Modrinth.ProjectType? selectedType = null)
    {
        var game = (Localization.T("游戏", "Game"), (Func<Microsoft.UI.Xaml.UIElement>)(() => new GameDownloadPage()));
        var mods = ("Mod", (Func<Microsoft.UI.Xaml.UIElement>)(() => new ResourceSearchPage(Modrinth.ProjectType.Mod)));
        var packs = (Localization.T("整合包", "Modpacks"), (Func<Microsoft.UI.Xaml.UIElement>)(() => new ResourceSearchPage(Modrinth.ProjectType.Modpack)));
        var resources = (Localization.T("资源包", "Resource packs"), (Func<Microsoft.UI.Xaml.UIElement>)(() => new ResourceSearchPage(Modrinth.ProjectType.ResourcePack)));
        var shaders = (Localization.T("光影", "Shaders"), (Func<Microsoft.UI.Xaml.UIElement>)(() => new ResourceSearchPage(Modrinth.ProjectType.Shader)));
        if (selectedType != null) Ui.SelectTab("download", selectedType switch { Modrinth.ProjectType.Mod => 1, Modrinth.ProjectType.Modpack => 2, Modrinth.ProjectType.ResourcePack => 3, _ => 4 });
        Content = Ui.Tabs("download", Localization.T("下载", "Download"), game, mods, packs, resources, shaders);
    }
}
