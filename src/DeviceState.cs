namespace PeripheralBattery;

// Only successful observations become DeviceState. A null Power explicitly
// means a USB-powered device without a battery, not a missing/failed reading.
internal sealed record DeviceState(string Name, ushort ProductId, BatteryReading? Power, DateTimeOffset UpdatedAt, string Source="ROG OMNI 接收器")
{
    public bool IsWired => Power is null;
    public string ValueText => Power is { } power?$"{power.Percent}%":"有线";
    public string ConnectionText => Power is { } power?power.Charging?"正在充电":"已连接":"USB 供电";
}
