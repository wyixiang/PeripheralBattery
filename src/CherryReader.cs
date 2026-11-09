namespace MouseBattery;

internal static class CherryReader
{
    internal static async Task<DeviceState> Read()
    {
        // Match the verified MX 10.1 vendor collection; never open a keyboard
        // input collection or send a query to an unrelated CHERRY device.
        var devices=Hid.Enumerate(0x046a).Where(d=>d.ProductId==0x01ef && d.UsagePage==0xff1c && d.Usage==0x92 && d.InputLength==64 && d.OutputLength==64).ToArray();
        if(devices.Length==0) throw new IOException("未检测到 CHERRY MX 10.1 电量接口");
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
