using W=System.Windows;
using C=System.Windows.Controls;
using M=System.Windows.Media;
using I=System.Windows.Input;
using H=System.Windows.Interop;

namespace MouseBattery;
internal sealed class DockWindow : GlassWindow
{
    readonly WidgetContext context;
    readonly MonitorSelection monitor=new();
    Point? press;
    int startOffset;
    bool dragged;
    internal string? MonitorDevice { get; private set; }
    internal IntPtr Handle => new H.WindowInteropHelper(this).EnsureHandle();
    internal Rectangle Bounds => Native.GetWindowRect(Handle,out var r)?Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom):Rectangle.Empty;
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
        MouseRightButtonUp+=(_,e)=> { Dispatcher.BeginInvoke(new Action(context.ShowMenu),System.Windows.Threading.DispatcherPriority.Input); e.Handled=true; };
        Repaint();
    }
    internal void PrepareForReconnect()=>monitor.BeginReconnect(DateTimeOffset.UtcNow);
    internal void RestoreTopmost()
    {
        if(!context.Settings.Visible || !IsVisible || context.IsQuitting) return;
        // Keep position/size and keyboard focus; don't move the hidden WPF owner.
        Native.SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x0253);
    }
    internal void Repaint()
    {
        var p=new Palette(context.IsDark); UpdateBackdrop(context.IsDark);
        Surface.Background=Palette.Brush(context.IsDark?"#C8262C33":"#C8F8FAFC"); Surface.BorderBrush=p.Stroke;
        var grid=new C.Grid { Margin=new W.Thickness(11,4,11,4),Cursor=I.Cursors.Hand };
        foreach(var width in new[]{136,24,136}) grid.ColumnDefinitions.Add(new C.ColumnDefinition { Width=new W.GridLength(width) });
        var tooltips=new List<string>();
        void Device(int column,string label,DeviceState? state,string? error,bool keyboard)
        {
            bool live=state is not null && error is null;
            var power=state?.Power;
            var accent=live?power!.Percent<=20?Palette.Brush("#D57832"):p.Accent:p.Muted;
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
            row.Children.Add(new C.TextBlock { Text=live && power!.Charging?label+"·充电":label,FontSize=11,Foreground=p.Muted,VerticalAlignment=W.VerticalAlignment.Center });
            row.Children.Add(new C.TextBlock { Text=live?$"{power!.Percent}%":"—",FontFamily=new M.FontFamily("Segoe UI"),FontSize=18,FontWeight=W.FontWeights.SemiBold,Foreground=live?p.Text:p.Muted,HorizontalAlignment=W.HorizontalAlignment.Right,VerticalAlignment=W.VerticalAlignment.Center });
            var track=new C.Border { Background=p.Stroke,CornerRadius=new W.CornerRadius(2) }; C.Grid.SetRow(track,1); body.Children.Add(track);
            track.Child=new C.Border { Background=accent,CornerRadius=new W.CornerRadius(2),Width=live?112*power!.Percent/100.0:0,HorizontalAlignment=W.HorizontalAlignment.Left };
            string description=live?$"{state!.Name} · {power!.Percent}% · {(power.Charging?"充电中":"已连接")}\n更新于 {state.UpdatedAt:HH:mm:ss}":$"{state?.Name??label} · {error??"正在读取…"}";
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
        if(!context.Settings.Visible) { Hide(); return; }
        var displays=DisplayCatalog.Read();
        var selected=monitor.Resolve(displays.Select(d=>new MonitorCandidate(d.DeviceName,d.Identity,d.Screen.Primary)).ToArray(),context.Settings.MonitorIdentity,context.Settings.MonitorDevice,DateTimeOffset.UtcNow);
        if(selected is null) { Hide(); return; }
        var display=displays.First(d=>d.DeviceName==selected.DeviceName);
        MonitorDevice=display.DeviceName;
        if(context.Settings.MonitorIdentity is null && !string.IsNullOrEmpty(display.Identity))
        { context.Settings.MonitorIdentity=display.Identity; context.Settings.MonitorDevice=display.DeviceName; context.Settings.Save(); }
        var taskbar=Native.Taskbars().FirstOrDefault(h=>Screen.FromHandle(h).DeviceName==display.DeviceName);
        if(taskbar==IntPtr.Zero || !Native.GetWindowRect(taskbar,out var r)) { Hide(); return; }
        var screen=display.Screen.Bounds;
        var scale=Math.Max(96,Native.GetDpiForWindow(taskbar))/96.0;
        int width=(int)Math.Round(320*scale),height=Math.Min((int)Math.Round(40*scale),r.Bottom-r.Top-4);
        if(height<20 || r.Top>=screen.Bottom-3 || r.Bottom<=screen.Top+3 || Native.IsFullscreenForeground(screen,Handle)) { Hide(); return; }
        // Sit immediately to the left of the AI widget when it is on the same taskbar.
        int anchor=(int)Math.Round((r.Right-r.Left)*.16)-width-(int)Math.Round(8*scale);
        var ai=Native.FindWindow(null!,"Codex 任务栏额度");
        if(ai!=IntPtr.Zero && Native.GetWindowRect(ai,out var a) && a.Top>=r.Top && a.Bottom<=r.Bottom) anchor=a.Left-r.Left-width-(int)Math.Round(8*scale);
        int x=r.Left+Math.Clamp(anchor+(int)Math.Round(context.Settings.Offset*scale),8,Math.Max(8,r.Right-r.Left-width-(int)Math.Round(96*scale)));
        int y=r.Top+(r.Bottom-r.Top-height)/2;
        Native.SetWindowPos(Handle,new IntPtr(-1),x,y,width,height,0x0250);
        if(!IsVisible) Show();
    }
}
