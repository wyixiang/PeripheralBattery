using W=System.Windows;
using C=System.Windows.Controls;
using M=System.Windows.Media;
using I=System.Windows.Input;
using H=System.Windows.Interop;

namespace PeripheralBattery;
internal sealed class DockWindow : GlassWindow
{
    readonly WidgetContext context;
    readonly MonitorSelection monitor=new();
    Point? press;
    int startOffset;
    bool dragged;
    internal string? MonitorDevice { get; private set; }
    internal bool CanRestore => !context.IsQuitting && CanUse;
    // Never create/recreate a handle from a timer, delayed restore or after Close.
    internal IntPtr Handle => CanRestore?new H.WindowInteropHelper(this).Handle:IntPtr.Zero;
    internal Rectangle Bounds => Handle is var handle && handle!=IntPtr.Zero && Native.GetWindowRect(handle,out var r)?Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom):Rectangle.Empty;
    internal DockWindow(WidgetContext context):base(context.IsDark)
    {
        this.context=context;
        Title="外设电量"; Width=320; Height=40; ShowActivated=false; ShowInTaskbar=context.InspectUi;
        Surface.CornerRadius=new W.CornerRadius(9);
        SourceInitialized+=(_,_)=>Native.SetWindowLongPtr(Handle,-20,new IntPtr(Native.GetWindowLongPtr(Handle,-20).ToInt64()|0x08000000L));
        MouseLeftButtonDown+=(_,e)=> { press=System.Windows.Forms.Cursor.Position; startOffset=context.Settings.Offset; dragged=false; CaptureMouse(); e.Handled=true; };
        MouseMove+=(_,e)=>
        {
            if(press is not { } p || e.LeftButton!=I.MouseButtonState.Pressed || context.Settings.Locked) return;
            int delta=System.Windows.Forms.Cursor.Position.X-p.X;
            if(!dragged && Math.Abs(delta)<5) return;
            dragged=true;
            context.Settings.Offset=startOffset+(int)Math.Round(delta/(Math.Max(96,Native.GetDpiForWindow(Handle))/96.0));
            DockToTaskbar();
        };
        MouseLeftButtonUp+=(_,e)=>
        {
            if(press is null) return;
            press=null; ReleaseMouseCapture(); e.Handled=true;
            if(dragged) context.Settings.Save(); else context.ShowDetails();
        };
        LostMouseCapture+=(_,_)=>press=null;
        Closed+=(_,_)=>Program.History?.CardState(context.IsQuitting?"closed-during-exit":"closed-unexpectedly");
        MouseRightButtonUp+=(_,e)=> { UiDispatch.Post(Dispatcher,()=>CanRestore,context.ShowMenu,System.Windows.Threading.DispatcherPriority.Input); e.Handled=true; };
        Repaint();
    }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if(!context.IsQuitting) e.Cancel=true;
        base.OnClosing(e);
        if(!context.IsQuitting)
        {
            e.Cancel=true; IsClosing=false;
            Program.History?.Note("card-close-prevented","常驻卡片只在明确退出时销毁");
        }
    }
    internal void PrepareForReconnect() { if(CanRestore) monitor.BeginReconnect(DateTimeOffset.UtcNow); }
    internal void RestoreTopmost()
    {
        if(!CanRestore || !context.Settings.Visible || !IsVisible) return;
        var handle=Handle; if(handle==IntPtr.Zero) return;
        // Keep position/size and keyboard focus; don't move the hidden WPF owner.
        Native.SetWindowPos(handle,new IntPtr(-1),0,0,0,0,0x0253);
    }
    internal void Repaint()
    {
        if(!CanRestore) return;
        var p=new Palette(context.IsDark); UpdateBackdrop(context.IsDark);
        Surface.Background=Palette.Brush(context.IsDark?"#C8262C33":"#C8F8FAFC"); Surface.BorderBrush=p.Stroke;
        var grid=new C.Grid { Margin=new W.Thickness(11,4,11,4),Cursor=I.Cursors.Hand };
        foreach(var width in new[]{136,24,136}) grid.ColumnDefinitions.Add(new C.ColumnDefinition { Width=new W.GridLength(width) });
        var tooltips=new List<string>();
        void Device(int column,string label,DeviceState? state,string? error,bool keyboard)
        {
            bool live=state is not null && error is null;
            var power=state?.Power;
            var accent=live?power?.Percent<=20?Palette.Brush("#D57832"):p.Accent:p.Muted;
            var section=new C.Grid(); section.ColumnDefinitions.Add(new C.ColumnDefinition { Width=new W.GridLength(24) }); section.ColumnDefinitions.Add(new C.ColumnDefinition());
            C.Grid.SetColumn(section,column); grid.Children.Add(section);
            W.FrameworkElement symbol;
            if(keyboard)
            {
                var keys=new C.Grid { Margin=new W.Thickness(2) };
                for(int y=0;y<2;y++) for(int x=0;x<4;x++) keys.Children.Add(new C.Border { Width=2,Height=2,Background=accent,HorizontalAlignment=W.HorizontalAlignment.Left,VerticalAlignment=W.VerticalAlignment.Top,Margin=new W.Thickness(x*3,y*4,0,0) });
                symbol=new C.Border { Width=18,Height=13,CornerRadius=new W.CornerRadius(2),BorderThickness=new W.Thickness(1.2),BorderBrush=accent,Child=keys,HorizontalAlignment=W.HorizontalAlignment.Left,VerticalAlignment=W.VerticalAlignment.Center };
            }
            else
            {
                symbol=new C.Border { Width=14,Height=22,CornerRadius=new W.CornerRadius(7),BorderThickness=new W.Thickness(1.5),BorderBrush=accent,HorizontalAlignment=W.HorizontalAlignment.Left,VerticalAlignment=W.VerticalAlignment.Center,Child=new C.Border { Width=2,Height=6,Background=accent,CornerRadius=new W.CornerRadius(1),VerticalAlignment=W.VerticalAlignment.Top,Margin=new W.Thickness(0,3,0,0) } };
            }
            section.Children.Add(symbol);
            var body=new C.Grid(); C.Grid.SetColumn(body,1); section.Children.Add(body);
            body.RowDefinitions.Add(new C.RowDefinition { Height=new W.GridLength(22) }); body.RowDefinitions.Add(new C.RowDefinition { Height=new W.GridLength(4) });
            var row=new C.Grid(); body.Children.Add(row);
            row.Children.Add(new C.TextBlock { Text=live && power?.Charging==true?label+"·充电":label,FontSize=11,Foreground=p.Muted,VerticalAlignment=W.VerticalAlignment.Center });
            if(live && state!.IsWired)
            {
                var plug=UsbPowerIcon.Create(p.Text,22);
                plug.HorizontalAlignment=W.HorizontalAlignment.Right;
                row.Children.Add(plug);
            }
            else
                row.Children.Add(new C.TextBlock { Text=live?state!.ValueText:"—",FontFamily=new M.FontFamily("Segoe UI"),FontSize=18,FontWeight=W.FontWeights.SemiBold,Foreground=live?p.Text:p.Muted,HorizontalAlignment=W.HorizontalAlignment.Right,VerticalAlignment=W.VerticalAlignment.Center });
            if(state?.IsWired!=true)
            {
                var track=new C.Border { Background=p.Stroke,CornerRadius=new W.CornerRadius(2) }; C.Grid.SetRow(track,1); body.Children.Add(track);
                track.Child=new C.Border { Background=accent,CornerRadius=new W.CornerRadius(2),Width=live && power is not null?112*power.Percent/100.0:0,HorizontalAlignment=W.HorizontalAlignment.Left };
            }
            string description=live?$"{state!.Name} · {state.ValueText} · {state.ConnectionText}\n更新于 {state.UpdatedAt:HH:mm:ss}":$"{state?.Name??label} · {error??"正在读取…"}";
            tooltips.Add(description);
        }
        Device(0,"鼠标",context.State,context.Error,false);
        Device(2,"键盘",context.KeyboardState,context.KeyboardError,true);
        var divider=new C.Border { Width=1,Height=22,Background=p.Stroke,VerticalAlignment=W.VerticalAlignment.Center };
        C.Grid.SetColumn(divider,1); grid.Children.Add(divider);
        ToolTip=string.Join("\n\n",tooltips)+"\n\n剩余电量 · 单击详情 · 右键设置 · 拖动调整位置";
        Surface.Child=grid;
        System.Windows.Automation.AutomationProperties.SetName(this,string.Join("；",tooltips.Select(t=>t.Split('\n')[0])));
    }
    internal void DockToTaskbar()
    {
        if(!CanRestore) return;
        if(!context.Settings.Visible) { Program.History?.CardState("hidden-by-user-setting"); Hide(); return; }
        var displays=DisplayCatalog.Read();
        var selected=monitor.Resolve(displays.Select(d=>new MonitorCandidate(d.DeviceName,d.Identity,d.Screen.Primary)).ToArray(),context.Settings.MonitorIdentity,context.Settings.MonitorDevice,DateTimeOffset.UtcNow);
        if(selected is null) { Program.History?.CardState("waiting-for-monitor"); Hide(); return; }
        var display=displays.First(d=>d.DeviceName==selected.DeviceName);
        MonitorDevice=display.DeviceName;
        if(context.Settings.MonitorIdentity is null && !string.IsNullOrEmpty(display.Identity))
        { context.Settings.MonitorIdentity=display.Identity; context.Settings.MonitorDevice=display.DeviceName; context.Settings.Save(); }
        var taskbar=Native.Taskbars().FirstOrDefault(h=>Screen.FromHandle(h).DeviceName==display.DeviceName);
        if(taskbar==IntPtr.Zero || !Native.GetWindowRect(taskbar,out var r)) { Program.History?.CardState("taskbar-not-found"); Hide(); return; }
        var screen=display.Screen.Bounds;
        var scale=Math.Max(96,Native.GetDpiForWindow(taskbar))/96.0;
        int width=(int)Math.Round(320*scale),height=Math.Min((int)Math.Round(40*scale),r.Bottom-r.Top-4);
        if(height<20 || r.Top>=screen.Bottom-3 || r.Bottom<=screen.Top+3) { Program.History?.CardState("taskbar-hidden-or-too-small"); Hide(); return; }
        var handle=Handle; if(handle==IntPtr.Zero) return;
        if(Native.IsFullscreenForeground(screen,handle)) { Program.History?.CardState("hidden-for-fullscreen"); Hide(); return; }
        // Sit immediately to the left of the AI widget when it is on the same taskbar.
        int anchor=(int)Math.Round((r.Right-r.Left)*.16)-width-(int)Math.Round(8*scale);
        var ai=Native.FindWindow(null!,"Codex 任务栏额度");
        if(ai!=IntPtr.Zero && Native.GetWindowRect(ai,out var a) && a.Top>=r.Top && a.Bottom<=r.Bottom) anchor=a.Left-r.Left-width-(int)Math.Round(8*scale);
        int x=r.Left+Math.Clamp(anchor+(int)Math.Round(context.Settings.Offset*scale),8,Math.Max(8,r.Right-r.Left-width-(int)Math.Round(96*scale)));
        int y=r.Top+(r.Bottom-r.Top-height)/2;
        Native.SetWindowPos(handle,new IntPtr(-1),x,y,width,height,0x0250);
        if(!IsVisible && CanRestore) Show();
        Program.History?.CardState("visible");
    }
}
