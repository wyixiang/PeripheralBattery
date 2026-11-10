global using System.IO;
namespace PeripheralBattery;

// Real production windows and dispatcher helpers, with an in-memory owner.
// No hardware readers, IPC names, user settings, logs or user instance are used.
internal sealed class TestPreferences
{
    internal bool Visible=false,Locked=true;
    internal int Offset;
    internal string? MonitorIdentity,MonitorDevice;
    internal void Save() { }
}
internal sealed class WidgetContext
{
    internal bool IsDark=>false;
    internal bool InspectUi=>false;
    internal bool IsQuitting;
    internal bool Busy=>false;
    internal TestPreferences Settings=new();
    internal DockWindow Dock=null!;
    internal DeviceState? State=>null;
    internal string? Error=>null;
    internal DeviceState? KeyboardState=>null;
    internal string? KeyboardError=>null;
    internal void ShowDetails() { }
    internal void ShowMenu() { }
    internal Task Refresh()=>Task.CompletedTask;
    internal static void OpenGearLink() { }
}
internal sealed class HistoryStub
{
    internal void CardState(string state) { }
    internal void Note(string name,string? detail=null) { }
}
