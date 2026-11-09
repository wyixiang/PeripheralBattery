using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
namespace MouseBattery;

internal sealed record DisplayEntry(Screen Screen, string Name, string? Identity)
{
    internal string DeviceName => Screen.DeviceName;
    internal int Number => int.TryParse(Regex.Match(DeviceName,@"\d+$").Value,out var value) ? value : 0;
    internal string Label => $"显示器 {Number} · {Name}\n{Screen.Bounds.Width} × {Screen.Bounds.Height}" + (Screen.Primary ? " · 主显示器" : "");
}
internal static class DisplayCatalog
{
    internal static IReadOnlyList<DisplayEntry> Read()
    {
        var names=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var identities=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            for(var attempt=0;attempt<3;attempt++)
            {
                if(GetDisplayConfigBufferSizes(2,out var pathsCount,out var modesCount)!=0) break;
                var paths=new DisplayPath[pathsCount];
                var modes=Marshal.AllocHGlobal(checked((int)Math.Max(modesCount,1)*64));
                try
                {
                    var result=QueryDisplayConfig(2,ref pathsCount,paths,ref modesCount,modes,IntPtr.Zero);
                    if(result==122) continue;
                    if(result!=0) break;
                    foreach(var path in paths.Take((int)pathsCount))
                    {
                        var source=new SourceName { Header=new Header { Type=1,Size=(uint)Marshal.SizeOf<SourceName>(),Adapter=path.Source.Adapter,Id=path.Source.Id } };
                        var target=new TargetName { Header=new Header { Type=2,Size=(uint)Marshal.SizeOf<TargetName>(),Adapter=path.Target.Adapter,Id=path.Target.Id } };
                        if(GetSourceName(ref source)==0 && GetTargetName(ref target)==0 && !string.IsNullOrWhiteSpace(source.DeviceName))
                        {
                            if(!string.IsNullOrWhiteSpace(target.FriendlyName)) names[source.DeviceName]=target.FriendlyName;
                            if(!string.IsNullOrWhiteSpace(target.DevicePath)) identities[source.DeviceName]=target.DevicePath;
                        }
                    }
                    break;
                }
                finally { Marshal.FreeHGlobal(modes); }
            }
        }
        catch(ExternalException) { }
        return Screen.AllScreens.Select(screen=>new DisplayEntry(screen,names.GetValueOrDefault(screen.DeviceName,"显示器"),identities.GetValueOrDefault(screen.DeviceName))).OrderBy(entry=>entry.Number).ToArray();
    }
    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct Header { public uint Type,Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential)] private struct Source { public Luid Adapter; public uint Id,Mode,Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct Rational { public uint Numerator,Denominator; }
    [StructLayout(LayoutKind.Sequential)] private struct Target { public Luid Adapter; public uint Id,Mode,Technology,Rotation,Scaling; public Rational Refresh; public uint ScanLine; public int Available; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct DisplayPath { public Source Source; public Target Target; public uint Flags; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct SourceName { public Header Header; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string DeviceName; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct TargetName { public Header Header; public uint Flags,Technology; public ushort Manufacturer,Product; public uint Connector; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)] public string FriendlyName; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string DevicePath; }
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags,out uint paths,out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags,ref uint paths,[Out] DisplayPath[] pathArray,ref uint modes,IntPtr modeArray,IntPtr topology);
    [DllImport("user32.dll",EntryPoint="DisplayConfigGetDeviceInfo",CharSet=CharSet.Unicode)] private static extern int GetSourceName(ref SourceName source);
    [DllImport("user32.dll",EntryPoint="DisplayConfigGetDeviceInfo",CharSet=CharSet.Unicode)] private static extern int GetTargetName(ref TargetName target);
}
internal static class SystemTheme
{
    internal static bool IsDark
    {
        get
        {
            try
            {
                using var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",false);
                return key?.GetValue("AppsUseLightTheme") is int value && value==0;
            }
            catch(System.Security.SecurityException) { return false; }
            catch(UnauthorizedAccessException) { return false; }
        }
    }
}
