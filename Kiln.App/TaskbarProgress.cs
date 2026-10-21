using Kiln.Core;
using System.Runtime.InteropServices;

namespace Kiln.App;

internal sealed class TaskbarProgress : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly SubclassProc _callback;
    private readonly uint _created = RegisterWindowMessage("TaskbarButtonCreated");
    private ITaskbarList3? _taskbar;
    private JobProgress _pending;
    private int _badge = -1;
    private bool _disposed;
    private long _retryAt;
    internal bool Ready { get; private set; }
    internal JobProgress? Applied { get; private set; }
    internal int? LastError { get; private set; }

    internal TaskbarProgress(IntPtr hwnd)
    {
        _hwnd = hwnd; _callback = WindowProc;
        if (_created == 0 || !SetWindowSubclass(hwnd, _callback, 0x4B696C6E, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    private IntPtr WindowProc(IntPtr hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == _created)
        {
            Ready = true; Release(); _badge = -1; _retryAt = 0;
            // Explorer also sends this after restarting. Reapply the latest snapshot.
            Update(_pending, force: true);
        }
        else if (message == 0x82) { RemoveWindowSubclass(hwnd, _callback, id); _disposed = true; Release(); }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }
    internal void Update(JobProgress progress, bool force = false)
    {
        _pending = progress;
        if (_disposed || !Ready || Environment.TickCount64 < _retryAt || (!force && Applied == progress && _taskbar != null)) return;
        try
        {
            if (_taskbar == null)
            {
                _taskbar = (ITaskbarList3)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11D0-958A-006097C9A090"))!)!;
                _taskbar.HrInit();
            }
            if (progress.Count == 0) _taskbar.SetProgressState(_hwnd, 0);
            else { _taskbar.SetProgressState(_hwnd, 2); _taskbar.SetProgressValue(_hwnd, (ulong)(Math.Max(.03, progress.Fraction) * 10000), 10000); }
            var badge = progress.Count > 1 ? progress.Count : 0;
            if (_badge != badge)
            {
                var icon = badge == 0 ? IntPtr.Zero : NativeBadge.Create(badge);
                try { _taskbar.SetOverlayIcon(_hwnd, icon, badge == 0 ? "" : Localization.T($"{badge} 个任务", $"{badge} tasks")); }
                finally { if (icon != IntPtr.Zero) NativeBadge.Destroy(icon); }
                _badge = badge;
            }
            Applied = progress; LastError = null;
        }
        catch (Exception e) { if (LastError != e.HResult) Log.log("[Taskbar] " + e.Friendly()); LastError = e.HResult; _retryAt = Environment.TickCount64 + 5000; Release(); }
    }
    private void Release() { if (_taskbar != null) { Marshal.FinalReleaseComObject(_taskbar); _taskbar = null; } }
    public void Dispose()
    {
        if (_disposed) return;
        Update(default, force: true); RemoveWindowSubclass(_hwnd, _callback, 0x4B696C6E); _disposed = true; Release();
    }
    private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);

    [ComImport, Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit(); void AddTab(IntPtr hwnd); void DeleteTab(IntPtr hwnd); void ActivateTab(IntPtr hwnd); void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total); void SetProgressState(IntPtr hwnd, uint state);
        void RegisterTab(IntPtr tab, IntPtr owner); void UnregisterTab(IntPtr tab); void SetTabOrder(IntPtr tab, IntPtr before); void SetTabActive(IntPtr tab, IntPtr owner, uint flags);
        void ThumbBarAddButtons(IntPtr hwnd, uint count, IntPtr buttons); void ThumbBarUpdateButtons(IntPtr hwnd, uint count, IntPtr buttons); void ThumbBarSetImageList(IntPtr hwnd, IntPtr list);
        void SetOverlayIcon(IntPtr hwnd, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
    }
}
