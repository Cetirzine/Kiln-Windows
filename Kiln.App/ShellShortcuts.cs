using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Kiln.App;

// Windows' built-in shell-link implementation; no scripting host or additional runtime.
internal static class ShellShortcuts
{
    internal const string AppId = "Cetirzine.Kiln";
    private static readonly PropertyKey AppIdKey = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
    private static IShellLinkW NewLink() => (IShellLinkW)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!)!;

    internal static void Write(string path, string target, string arguments, string icon, string description)
    {
        var link = NewLink();
        try
        {
            link.SetPath(target); link.SetArguments(arguments); link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
            link.SetDescription(description); link.SetIconLocation(icon, 0); link.SetShowCmd(1);
            var key = AppIdKey; var value = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(AppId) };
            try { var properties = (IPropertyStore)link; properties.SetValue(ref key, ref value); properties.Commit(); }
            finally { PropVariantClear(ref value); }
            ((IPersistFile)link).Save(path, false);
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    internal static (string Target, string Arguments, string Icon, string AppId) Read(string path)
    {
        var link = NewLink();
        try
        {
            ((IPersistFile)link).Load(path, 0);
            var target = new StringBuilder(32768); var arguments = new StringBuilder(32768); var icon = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 4); link.GetArguments(arguments, arguments.Capacity); link.GetIconLocation(icon, icon.Capacity, out _);
            var key = AppIdKey; ((IPropertyStore)link).GetValue(ref key, out var value);
            try { return (target.ToString(), arguments.ToString(), icon.ToString(), value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? "" : ""); }
            finally { PropVariantClear(ref value); }
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid format, uint id) { public Guid Format = format; public uint Id = id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }
    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    // Vtable order is the native IShellLinkW declaration, including unused methods.
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, IntPtr findData, uint flags);
        void GetIDList(out IntPtr item);
        void SetIDList(IntPtr item);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int size);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string path);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int size);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short key);
        void SetHotkey(short key);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
