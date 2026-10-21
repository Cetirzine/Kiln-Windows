using System.Runtime.InteropServices;
using System.Xml.Linq;
using Windows.UI.Notifications;

namespace Kiln.App;

// ToastGeneric protocol activation uses an AUMID shortcut and stub CLSID.
// No COM server, package identity or Windows App SDK singleton dependency is needed.
internal sealed class DesktopNotifications(string appId, string shortcut)
{
    internal static readonly Guid Activator = new("501846F7-BD44-4C22-93FC-DDE74F0D54A3");
    private bool _registered;
    internal string AppId => appId;
    internal string Shortcut => shortcut;

    internal void Register()
    {
        if (_registered) return;
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Launcher executable is unavailable.");
        // Windows' unpackaged notification platform resolves display assets through this app-owned registration.
        using (var identity = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + appId))
        {
            identity.SetValue("DisplayName", "Kiln");
            identity.SetValue("IconUri", Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png"));
            identity.SetValue("CustomActivator", Activator.ToString("B"));
        }
        if (File.Exists(shortcut))
        {
            try { var old = ShellShortcuts.Read(shortcut); if (string.Equals(old.Target, exe, StringComparison.OrdinalIgnoreCase) && old.AppId == appId && old.ToastActivator == Activator) { _registered = true; return; } }
            catch { /* Refresh an older launcher-owned shortcut. */ }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(shortcut)!);
        var staging = shortcut + "." + Guid.NewGuid().ToString("N") + ".lnk";
        try
        {
            ShellShortcuts.Write(staging, exe, "", Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"), "Kiln", appId, Activator);
            File.Move(staging, shortcut, overwrite: true);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
        SHChangeNotify(0x2000, 5, shortcut, IntPtr.Zero); _registered = true;
    }

    internal NotificationSetting Setting { get { Register(); return ToastNotificationManager.CreateToastNotifier(appId).Setting; } }
    internal ToastNotification? Show(string title, string message, string launchUri, bool suppressPopup = false)
    {
        Register(); var notifier = ToastNotificationManager.CreateToastNotifier(appId);
        try { if (notifier.Setting != NotificationSetting.Enabled) return null; }
        catch (COMException error) when (error.HResult == unchecked((int)0x80070490))
        {
            // A new unpackaged AUMID gets its notification-platform entry on its first send.
            // Windows still enforces notification settings when accepting the toast.
        }
        var doc = new Windows.Data.Xml.Dom.XmlDocument(); doc.LoadXml(Xml(title, message, launchUri));
        var toast = new ToastNotification(doc) { Tag = Guid.NewGuid().ToString("N")[..16], Group = "Kiln", SuppressPopup = suppressPopup };
        toast.Failed += (_, failure) => Kiln.Core.Log.log("[Notify] " + failure.ErrorCode.Message);
        notifier.Show(toast); return toast;
    }
    internal static string Xml(string title, string message, string launchUri)
    {
        var binding = new XElement("binding", new XAttribute("template", "ToastGeneric"), new XElement("text", title), new XElement("text", message),
            new XElement("image", new XAttribute("placement", "appLogoOverride"), new XAttribute("src", new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png")).AbsoluteUri)));
        return new XElement("toast", new XAttribute("activationType", "protocol"), new XAttribute("launch", launchUri), new XElement("visual", binding)).ToString(SaveOptions.DisableFormatting);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern void SHChangeNotify(uint change, uint flags, string path, IntPtr extra);
}
