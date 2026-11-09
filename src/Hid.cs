using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PeripheralBattery;

internal sealed record HidDevice(string Path, ushort ProductId, ushort UsagePage, ushort Usage,
    ushort InputLength, ushort OutputLength, byte[] OutputIds, string Name);

internal static class Hid
{
    [StructLayout(LayoutKind.Sequential)] struct InterfaceData { public int Size; public Guid Class; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct Attributes { public int Size; public ushort Vendor, Product, Version; }
    [StructLayout(LayoutKind.Sequential)] struct Caps
    {
        public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] Reserved;
        public ushort Links, InputButtons, InputValues, InputIndices, OutputButtons, OutputValues, OutputIndices, FeatureButtons, FeatureValues, FeatureIndices;
    }
    internal static List<HidDevice> Enumerate(ushort vendor=0x0b05)
    {
        var result = new List<HidDevice>(); HidD_GetHidGuid(out var guid);
        var set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 18);
        if(set == new IntPtr(-1)) throw new Win32Exception();
        try
        {
            for(uint i=0;;i++)
            {
                var entry = new InterfaceData { Size=Marshal.SizeOf<InterfaceData>() };
                if(!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref entry))
                { if(Marshal.GetLastWin32Error()==259) break; throw new Win32Exception(); }
                SetupDiGetDeviceInterfaceDetail(set, ref entry, IntPtr.Zero, 0, out var length, IntPtr.Zero);
                var detail=Marshal.AllocHGlobal((int)length);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size==8?8:6);
                    if(!SetupDiGetDeviceInterfaceDetail(set,ref entry,detail,length,out _,IntPtr.Zero)) continue;
                    var path=Marshal.PtrToStringUni(detail+4)!;
                    if(!path.Contains("vid_"+vendor.ToString("x4"),StringComparison.OrdinalIgnoreCase)) continue;
                    using var handle=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero);
                    if(handle.IsInvalid) continue;
                    var attributes=new Attributes { Size=Marshal.SizeOf<Attributes>() };
                    if(!HidD_GetAttributes(handle,ref attributes) || attributes.Vendor!=vendor) continue;
                    if(!HidD_GetPreparsedData(handle,out var prep)) continue;
                    try
                    {
                        if(HidP_GetCaps(prep,out var caps)<0) continue;
                        var ids=new HashSet<byte>();
                        var count=caps.OutputValues;
                        var buffer=Marshal.AllocHGlobal(Math.Max(1,(int)count)*72);
                        try { if(count>0 && HidP_GetValueCaps(1,buffer,ref count,prep)>=0) for(int k=0;k<count;k++) ids.Add(Marshal.ReadByte(buffer,k*72+2)); }
                        finally { Marshal.FreeHGlobal(buffer); }
                        var name=new byte[256]; HidD_GetProductString(handle,name,name.Length);
                        result.Add(new(path,attributes.Product,caps.UsagePage,caps.Usage,caps.InputLength,caps.OutputLength,ids.Order().ToArray(),System.Text.Encoding.Unicode.GetString(name).TrimEnd('\0')));
                    }
                    finally { HidD_FreePreparsedData(prep); }
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }
    internal static FileStream Open(HidDevice device)
    {
        var handle=CreateFile(device.Path,0xC0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
        if(handle.IsInvalid) { var e=Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(e,"无法打开设备接口"); }
        return new FileStream(handle,FileAccess.ReadWrite,Math.Max(64,(int)device.InputLength),true);
    }
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle handle,ref Attributes attributes);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle handle,out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data,out Caps caps);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type,IntPtr caps,ref ushort count,IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_GetProductString(SafeFileHandle handle,[Out] byte[] data,int length);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr SetupDiGetClassDevs(ref Guid guid,string? enumerator,IntPtr parent,uint flags);
    [DllImport("setupapi.dll",SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set,IntPtr info,ref Guid guid,uint index,ref InterfaceData data);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set,ref InterfaceData data,IntPtr detail,uint size,out uint required,IntPtr info);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
}

// Only read commands documented by ASUS Gear Link are sent. No configuration or pairing writes.
internal sealed class HidSession : IDisposable
{
    readonly FileStream stream;
    readonly HidDevice device;
    internal HidSession(HidDevice device) { this.device=device; stream=Hid.Open(device); }
    internal async Task<byte[]> Query(byte report, byte command, byte sub=0, ushort index=0)
    {
        if(!device.OutputIds.Contains(report)) throw new InvalidOperationException("报告 ID 与设备不符");
        var output=new byte[device.OutputLength];
        if(output.Length<21 || device.InputLength<5) throw new InvalidOperationException("不支持的 HID 报告长度");
        output[0]=report; output[1]=command; output[2]=sub; output[3]=(byte)index; output[4]=(byte)(index>>8);
        using var timeout=new CancellationTokenSource(TimeSpan.FromMilliseconds(1600));
        await stream.WriteAsync(output,timeout.Token);
        await stream.FlushAsync(timeout.Token);
        var input=new byte[device.InputLength];
        while(true)
        {
            int length=await stream.ReadAsync(input,timeout.Token);
            if(length==0) throw new IOException("接收器已断开");
            if(length>=5 && input[0]==report && input[1]==command && input[2]==sub && (command==0xa0 || input[3]==(byte)index && input[4]==(byte)(index>>8))) return input[1..length];
        }
    }
    public void Dispose()=>stream.Dispose();
}
