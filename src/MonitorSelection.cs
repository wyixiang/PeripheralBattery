namespace MouseBattery;
internal sealed record MonitorCandidate(string DeviceName, string? Identity, bool Primary);
internal sealed class MonitorSelection
{
    private DateTimeOffset? _missingSince;
    private DateTimeOffset _reconnectUntil;
    internal void BeginReconnect(DateTimeOffset now) { _missingSince=null; _reconnectUntil=now.AddSeconds(15); }
    internal MonitorCandidate? Resolve(IReadOnlyList<MonitorCandidate> displays,string? preferredIdentity,string? legacyDevice,DateTimeOffset now)
    {
        if(displays.Count==0) return null;
        var identified=!string.IsNullOrWhiteSpace(preferredIdentity);
        var preferred=identified
            ? displays.FirstOrDefault(d=>string.Equals(d.Identity,preferredIdentity,StringComparison.OrdinalIgnoreCase))
            : displays.FirstOrDefault(d=>string.Equals(d.DeviceName,legacyDevice,StringComparison.OrdinalIgnoreCase));
        if(preferred is not null) { _missingSince=null; return preferred; }
        if(identified)
        {
            _missingSince??=now;
            if(now<_missingSince.Value.AddSeconds(15) || now<_reconnectUntil) return null;
        }
        return displays.FirstOrDefault(d=>d.Primary)??displays[0];
    }
}
