using System.Diagnostics;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Win32;
namespace PeripheralBattery;

internal sealed class WidgetContext : IDisposable
{
    internal readonly bool InspectUi;
    internal readonly Preferences Settings=Preferences.Load();
    internal DeviceState? State;
    internal string? Error;
    internal DeviceState? KeyboardState;
    internal string? KeyboardError;
    internal bool Busy;
    internal bool IsQuitting => stopping || disposed || !UiDispatch.Available(dispatcher);
    internal bool IsDark => Settings.Theme=="dark" || Settings.Theme=="system" && SystemTheme.IsDark;
    internal DockWindow Dock { get; }
    DetailsWindow? details;
    System.Windows.Controls.ContextMenu? menu;
    readonly NotifyIcon tray;
    readonly TaskbarLayerWatcher layerWatcher;
    readonly DispatcherTimer timer=new() { Interval=TimeSpan.FromSeconds(1) };
    readonly Dispatcher dispatcher=System.Windows.Application.Current.Dispatcher;
    readonly List<EventWaitHandle> events=new();
    readonly List<RegisteredWaitHandle> waits=new();
    int tick;
    volatile bool disposed,stopping;
    bool? lastTheme;
    int lastIcon=-1000;
    internal WidgetContext(bool inspectUi=false)
    {
        InspectUi=inspectUi;
        Dock=new(this);
        layerWatcher=new(System.Windows.Application.Current.Dispatcher,()=>
        {
            if(IsQuitting) return;
            try { Dock.DockToTaskbar(); } catch(Exception e) { Log(e); }
        });
        tray=new NotifyIcon { Visible=true,Text="外设电量 · 正在读取",Icon=TrayIconRenderer.Create(null,false,IsDark) };
        tray.MouseClick+=(_,e)=> { if(e.Button==MouseButtons.Right) ShowMenu(); else if(e.Button==MouseButtons.Left) ShowDetails(); };
        foreach(var name in new[]{"Show","Menu","Exit"})
        {
            var signal=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\PeripheralBattery."+name); events.Add(signal);
            waits.Add(ThreadPool.RegisterWaitForSingleObject(signal,(_,_)=>PostUi(()=> { if(name=="Exit") Quit("command-line --exit"); else if(name=="Menu") ShowMenu(); else ShowDetails(); }),null,-1,false));
        }
        SystemEvents.DisplaySettingsChanged+=DisplaysChanged;
        SystemEvents.PowerModeChanged+=PowerChanged;
        timer.Tick+=(_,_)=>
        {
            try
            {
                if(IsQuitting) return;
                if(lastTheme!=IsDark) { lastTheme=IsDark; Repaint(); }
                Dock.DockToTaskbar();
                if(++tick%30==0) { Program.History?.Heartbeat(Busy); _=Refresh(); }
            }
            catch(Exception e) { Log(e); }
        };
        timer.Start(); Dock.Show(); Dock.DockToTaskbar(); _=Refresh();
    }
    void PostUi(Action action)=>UiDispatch.Post(dispatcher,()=>!IsQuitting,action);
    void DisplaysChanged(object? sender,EventArgs e) => PostUi(()=> { Dock.PrepareForReconnect(); Dock.DockToTaskbar(); });
    void PowerChanged(object sender,PowerModeChangedEventArgs e)
    {
        if(IsQuitting) return;
        Program.History?.Note("power-mode",e.Mode.ToString());
        if(e.Mode==PowerModes.Resume) PostUi(()=> { Dock.PrepareForReconnect(); _=Refresh(); });
    }
    internal async Task Refresh()
    {
        if(Busy || IsQuitting) return;
        Busy=true; Repaint();
        async Task ReadOne(bool keyboard)
        {
            try
            {
                Func<Task<DeviceState>> reader=keyboard?CherryReader.Read:BatteryReader.Read;
                var value=await Task.Run(reader);
                if(IsQuitting) return;
                if(keyboard) { KeyboardState=value; KeyboardError=null; }
                else { State=value; Error=null; }
            }
            catch(Exception e)
            {
                if(IsQuitting) return;
                string error=e is OperationCanceledException?(keyboard?"键盘":"鼠标")+"休眠、离线或接口忙碌":e.Message;
                if(keyboard) KeyboardError=error; else Error=error;
                Log(e);
            }
        }
        try { await Task.WhenAll(ReadOne(false),ReadOne(true)); }
        finally
        {
            Busy=false;
            if(!IsQuitting)
            {
                Repaint();
                try
                {
                    Directory.CreateDirectory(Preferences.Folder);
                    File.WriteAllText(Path.Combine(Preferences.Folder,"status.json"),JsonSerializer.Serialize(new { State,Error,KeyboardState,KeyboardError,Busy,Time=DateTimeOffset.Now },new JsonSerializerOptions { WriteIndented=true }));
                }
                catch(IOException) { }
            }
        }
    }
    internal void Repaint()
    {
        if(IsQuitting) return;
        Dock.Repaint();
        if(details is { IsDisposed:false }) details.Repaint();
        string Summary(string label,DeviceState? state,string? error)=>error is not null?$"{label} —":state is not null?$"{label} {state.ValueText}"+(state.Power?.Charging==true?"（充电中）":""):$"{label} 读取中";
        var tooltip=Summary("鼠标",State,Error)+" · "+Summary("键盘",KeyboardState,KeyboardError)+"\n电池图标：鼠标电量";
        tray.Text=tooltip[..Math.Min(63,tooltip.Length)];
        bool live=Error is null && State is not null;
        int? percent=live?State!.Power?.Percent:null;
        bool charging=live && State!.Power?.Charging==true;
        var iconKey=(percent??-1)+(charging?200:0)+(IsDark?1000:0);
        if(lastIcon!=iconKey)
        {
            lastIcon=iconKey;
            var icon=TrayIconRenderer.Create(percent,charging,IsDark);
            var previous=tray.Icon; tray.Icon=icon; previous?.Dispose();
        }
    }
    internal void ShowDetails()
    {
        if(IsQuitting) return;
        Program.History?.Note("details-opened");
        if(details is { CanUse:true }) { details.Show(); details.WindowState=System.Windows.WindowState.Normal; details.Activate(); return; }
        if(details is { IsClosing:true }) return;
        details=new(this); details.Show(); details.Activate();
    }
    internal void ShowMenu()
    {
        if(IsQuitting || !Dock.CanRestore) return;
        if(menu?.IsOpen==true) { menu.IsOpen=false; return; }
        menu=WidgetMenu.Create(this);
        menu.PlacementTarget=Dock;
        menu.Placement=System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen=true;
    }
    internal void Change(Action<Preferences> change) { if(IsQuitting) return; change(Settings); Settings.Save(); Repaint(); Dock.DockToTaskbar(); }
    internal static void OpenGearLink()=>Process.Start(new ProcessStartInfo("https://gearlink.asus.com/zh-cn") { UseShellExecute=true });
    internal bool Autostart
    {
        get { using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return key?.GetValue("PeripheralBattery") is string value && value.Contains(Environment.ProcessPath!,StringComparison.OrdinalIgnoreCase); }
    }
    internal void ToggleAutostart()
    {
        try
        {
            bool enabled=Autostart;
            using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if(enabled) key.DeleteValue("PeripheralBattery",false); else key.SetValue("PeripheralBattery","\""+Environment.ProcessPath+"\"");
        }
        catch(Exception e) { System.Windows.MessageBox.Show("无法修改开机启动："+e.Message,"外设电量"); }
    }
    internal void BeginStopping()=>stopping=true;
    internal void Quit(string reason="user-menu-exit") { if(IsQuitting) return; Program.History?.RequestExit(reason); BeginStopping(); Dispose(); System.Windows.Application.Current.Shutdown(); }
    internal static void Log(Exception e)
    {
        try
        {
            Directory.CreateDirectory(Preferences.Folder);
            var path=Path.Combine(Preferences.Folder,"error.log");
            if(File.Exists(path) && new FileInfo(path).Length>128*1024) File.WriteAllText(path,"");
            File.AppendAllText(path,$"{DateTimeOffset.Now:o} {ExceptionDiagnostics.Describe(e)}\n");
        }
        catch { }
    }
    public void Dispose()
    {
        if(disposed) return; BeginStopping(); disposed=true;
        Program.History?.Note("disposing","清理计时器、托盘和窗口");
        timer.Stop();
        layerWatcher.Dispose();
        SystemEvents.DisplaySettingsChanged-=DisplaysChanged; SystemEvents.PowerModeChanged-=PowerChanged;
        foreach(var wait in waits) wait.Unregister(null);
        foreach(var signal in events) signal.Dispose();
        var icon=tray.Icon; tray.Visible=false; tray.Dispose(); icon?.Dispose();
        if(menu is not null) menu.IsOpen=false;
        if(details is { IsDisposed:false }) details.Close();
        if(!Dock.IsDisposed) Dock.Close();
        Program.History?.MarkCleanupComplete();
    }
}
