namespace PeripheralBattery;

internal static class CherryReader
{
    internal static async Task<DeviceState> Read()
    {
        var connected=Hid.Enumerate(0x046a);
        var wired=connected.FirstOrDefault(d=>CherryDeviceSupport.IsWiredMx100(d.ProductId,d.UsagePage,d.Usage));
        if(wired is not null)
            return new("CHERRY MX 10.0 RGB",wired.ProductId,null,DateTimeOffset.Now,"USB 有线连接（无需电池）");

        // Match only the verified MX 10.1 vendor collection.
        var devices=connected.Where(d=>CherryDeviceSupport.IsMx101BatteryInterface(d.ProductId,d.UsagePage,d.Usage,d.InputLength,d.OutputLength)).ToArray();
        if(devices.Length==0) throw new IOException("未检测到支持的 CHERRY 键盘（MX 10.0 / MX 10.1）");
        Exception? last=null;
        foreach(var device in devices)
        {
            try
            {
                using var stream=Hid.Open(device);
                using var timeout=new CancellationTokenSource(TimeSpan.FromMilliseconds(1600));
                await stream.WriteAsync(CherryProtocol.PowerRequest(),timeout.Token);
                await stream.FlushAsync(timeout.Token);
                var response=new byte[64];
                while(true)
                {
                    int length=await stream.ReadAsync(response,timeout.Token);
                    if(length==0) throw new IOException("CHERRY 键盘已断开");
                    if(!CherryProtocol.IsPowerResponse(response.AsSpan(0,length))) continue;
                    var power=CherryProtocol.ParsePower(response[..length]);
                    return new("CHERRY MX 10.1",device.ProductId,power,DateTimeOffset.Now,"CHERRY HID 设备接口");
                }
            }
            catch(Exception e) when(e is IOException or System.ComponentModel.Win32Exception or OperationCanceledException or InvalidOperationException) { last=e; }
        }
        throw last??new IOException("没有可读取的 CHERRY 键盘");
    }
}
