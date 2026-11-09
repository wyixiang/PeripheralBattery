using System.Runtime.InteropServices;
namespace PeripheralBattery;
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern IntPtr FindWindow(string className,string? title);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string className,string? title);
    internal static IEnumerable<IntPtr> Taskbars()
    {
        var main=FindWindow("Shell_TrayWnd",null); if(main!=IntPtr.Zero) yield return main;
        var next=IntPtr.Zero; while((next=FindWindowEx(IntPtr.Zero,next,"Shell_SecondaryTrayWnd",null))!=IntPtr.Zero) yield return next;
    }
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] internal static extern IntPtr SetWindowLongPtr(IntPtr window,int index,IntPtr value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window,System.Text.StringBuilder text,int count);
    internal static bool IsTaskbar(IntPtr window)
    {
        if(window==IntPtr.Zero) return false;
        var name=new System.Text.StringBuilder(128); GetClassName(window,name,name.Capacity);
        return name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }
    internal static bool IsFullscreenForeground(Rectangle screen,IntPtr own)
    {
        var foreground=GetForegroundWindow(); if(foreground==IntPtr.Zero || foreground==own || !GetWindowRect(foreground,out var r)) return false;
        var name=new System.Text.StringBuilder(128); GetClassName(foreground,name,name.Capacity);
        if(name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        return r.Left<=screen.Left && r.Top<=screen.Top && r.Right>=screen.Right && r.Bottom>=screen.Bottom;
    }
}
