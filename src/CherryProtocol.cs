namespace MouseBattery;

internal static class CherryProtocol
{
    // CHERRY Utility's read-only HidReadPower request, six status bytes at index 0.
    // Bytes 1..2 contain the sum of request bytes 3..63; replies echo this header.
    internal static byte[] PowerRequest()
    {
        var request=new byte[64];
        request[0]=4; request[1]=0x20; request[3]=0x1a; request[4]=6;
        return request;
    }
    internal static bool IsPowerResponse(ReadOnlySpan<byte> response) => response.Length>=8 && response[..7].SequenceEqual(PowerRequest().AsSpan(0,7));
    internal static BatteryReading ParsePower(byte[] response)
    {
        if(response.Length!=64 || !IsPowerResponse(response)) throw new InvalidDataException("CHERRY 电量响应格式无效");
        if(response[7]!=0) throw new IOException("CHERRY 键盘未返回有效状态，可能已休眠或断开");
        if(response[8]>100 || response[9]>2) throw new InvalidDataException("CHERRY 键盘未返回有效电量");
        // Utility uses the charging image for state 1/2, and normal power for 0.
        return new(response[8],response[9] is 1 or 2,0);
    }
}
