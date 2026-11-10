namespace PeripheralBattery;

internal static class BatteryReader
{
    internal static async Task<DeviceState> Read()
    {
        var devices=Hid.Enumerate();
        var receivers=devices.Where(d=>d.ProductId==0x1ace && d.UsagePage==0xff02 && d.Usage==1 && d.OutputIds.Contains((byte)1)).ToArray();
        if(receivers.Length==0) throw new IOException("未检测到 ROG OMNI 接收器");
        Exception? last=null;
        foreach(var receiver in receivers)
        {
            try
            {
                using var session=new HidSession(receiver);
                var paired=BatteryProtocol.ParsePairing(await session.Query(1,0xa0));
                if(paired.Count==0) throw new IOException("接收器未配对鼠标");
                foreach(var mouse in paired)
                {
                    var prefix=receiver.Path.Split("&col",StringSplitOptions.None)[0];
                    var control=devices.FirstOrDefault(d=>d.Path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase) && d.UsagePage==0xff01 && d.OutputIds.Contains(mouse.ControlReport));
                    if(control is null) throw new IOException("找不到鼠标电量接口");
                    using var mouseSession=new HidSession(control);
                    var alive=await mouseSession.Query(mouse.ControlReport,0x12,0,2);
                    if(alive.Length<5 || alive[4]!=1) throw new IOException("鼠标休眠或已关闭");
                    var power=BatteryProtocol.ParsePower(await mouseSession.Query(mouse.ControlReport,0x12,7));
                    var name=mouse.ProductId==7182?"ROG 月刃2 RGB":"ROG 无线鼠标";
                    return new(name,mouse.ProductId,power,DateTimeOffset.Now);
                }
            }
            catch(Exception e) when(e is IOException or System.ComponentModel.Win32Exception or OperationCanceledException or InvalidOperationException) { last=e; }
        }
        throw last??new IOException("没有可读取的鼠标");
    }
}
