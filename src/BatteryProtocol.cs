namespace MouseBattery;

internal sealed record PairedMouse(ushort ProductId, byte ControlReport, byte EventReport);
internal sealed record BatteryReading(int Percent, bool Charging, int Millivolts);

internal static class BatteryProtocol
{
    internal static IReadOnlyList<PairedMouse> ParsePairing(byte[] response)
    {
        if(response.Length<4 || response[0]!=0xa0 || response[1]!=0) throw new InvalidDataException("配对清单响应无效");
        int count=response[2]|response[3]<<8;
        if(count>14 || 4+count*4>response.Length) throw new InvalidDataException("配对清单长度无效");
        var mice=new List<PairedMouse>();
        for(int k=0;k<count;k++)
        {
            int o=4+4*k;
            if(response[o+2]==3 && response[o+3]==5) mice.Add(new((ushort)(response[o]|response[o+1]<<8),response[o+2],response[o+3]));
        }
        return mice;
    }
    internal static BatteryReading ParsePower(byte[] response)
    {
        if(response.Length<12 || response[0]!=0x12 || response[1]!=7 || response[2]!=0 || response[3]!=0) throw new InvalidDataException("电量响应无效");
        if(response[4]>100 || response[9]>1) throw new InvalidDataException("鼠标未返回有效电量");
        return new(response[4],response[9]==1,response[7]|response[8]<<8);
    }
}
