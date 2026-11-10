namespace PeripheralBattery;

internal static class CherryDeviceSupport
{
    // These predicates are used only after filtering CHERRY VID 046A.
    // MX 10.0 presence is detected from its standard keyboard collection;
    // no handle is opened and no MX 10.1 battery command is sent to it.
    internal static bool IsWiredMx100(ushort productId,ushort usagePage,ushort usage)
        => productId==0x00bb && usagePage==1 && usage==6;

    internal static bool IsMx101BatteryInterface(ushort productId,ushort usagePage,ushort usage,ushort inputLength,ushort outputLength)
        => productId==0x01ef && usagePage==0xff1c && usage==0x92 && inputLength==64 && outputLength==64;
}
