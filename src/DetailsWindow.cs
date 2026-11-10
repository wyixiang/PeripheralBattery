using W=System.Windows;
using C=System.Windows.Controls;
using M=System.Windows.Media;
namespace PeripheralBattery;
internal sealed class DetailsWindow : GlassWindow
{
    readonly WidgetContext context;
    internal DetailsWindow(WidgetContext context):base(context.IsDark)
    {
        this.context=context; Title="外设电量详情"; Width=360; SizeToContent=W.SizeToContent.Height; ShowInTaskbar=context.InspectUi;
        WindowStartupLocation=W.WindowStartupLocation.CenterScreen;
        MouseLeftButtonDown+=(_,e)=> { if(e.OriginalSource is C.TextBlock or C.Border) try { DragMove(); } catch(InvalidOperationException) { } };
        Deactivated+=(_,_)=>Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(()=>
        {
            // Deactivation can arrive before a minimize finishes. Let the state
            // settle, then dismiss only a visible, inactive normal window.
            if(!context.IsQuitting && !IsDisposed && !IsClosing && IsVisible && !IsActive && WindowState==W.WindowState.Normal) Dismiss();
        }));
        Repaint();
        ContentRendered+=(_,_)=>
        {
            var bounds=context.Dock.Bounds;
            if(bounds.IsEmpty) return;
            var dpi=Math.Max(96,Native.GetDpiForWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle))/96.0;
            var screen=Screen.FromRectangle(bounds).WorkingArea;
            Left=Math.Clamp(bounds.Left/dpi,screen.Left/dpi+8,screen.Right/dpi-Width-8);
            Top=Math.Max(screen.Top/dpi+8,bounds.Top/dpi-ActualHeight-10);
        };
    }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if(!context.IsQuitting) { e.Cancel=true; Dismiss(); }
        base.OnClosing(e);
    }
    void Dismiss()
    {
        Hide();
        context.Dock.RestoreTopmost();
    }
    internal void Repaint()
    {
        var p=new Palette(context.IsDark); UpdateBackdrop(context.IsDark);
        Surface.Background=Palette.Brush(context.IsDark?"#ED25292E":"#EDF8FAFC"); Surface.BorderBrush=p.Stroke;
        var stack=new C.StackPanel { Margin=new W.Thickness(20,12,20,18) }; Surface.Child=stack;
        var header=new C.Grid { Margin=new W.Thickness(0,0,0,12) }; stack.Children.Add(header);
        header.Children.Add(new C.TextBlock { Text="外设电量",Foreground=p.Text,FontSize=16,FontWeight=W.FontWeights.SemiBold,VerticalAlignment=W.VerticalAlignment.Center });
        C.Button WindowButton(string text,string name,Action action,int offset)
        {
            var button=Button(text,p,action); button.Width=30; button.Padding=new W.Thickness(0); button.Margin=new W.Thickness(0,0,offset,0);
            button.Content=new C.TextBlock { Text=text,FontSize=18,HorizontalAlignment=W.HorizontalAlignment.Center,VerticalAlignment=W.VerticalAlignment.Center }; button.HorizontalAlignment=W.HorizontalAlignment.Right;
            W.Automation.AutomationProperties.SetName(button,name); return button;
        }
        header.Children.Add(WindowButton("−","收起到托盘",Dismiss,32));
        header.Children.Add(WindowButton("×","关闭详情",Close,0));
        void Device(string label,string fallback,DeviceState? state,string? error)
        {
            bool live=state is not null && error is null;
            var power=state?.Power;
            var card=new C.StackPanel { Margin=new W.Thickness(0,0,0,14) }; stack.Children.Add(card);
            card.Children.Add(new C.TextBlock { Text=label+" · "+(state?.Name??fallback),FontSize=12,Foreground=p.Muted,TextTrimming=W.TextTrimming.CharacterEllipsis });
            var row=new C.StackPanel { Orientation=C.Orientation.Horizontal,Margin=new W.Thickness(0,4,0,6) }; card.Children.Add(row);
            if(live && state!.IsWired)
            {
                var plug=UsbPowerIcon.Create(p.Text,40);
                plug.Margin=new W.Thickness(0,2,0,3);
                row.Children.Add(plug);
            }
            else
                row.Children.Add(new C.TextBlock { Text=live?state!.ValueText:"—",FontSize=34,FontFamily=new M.FontFamily("Segoe UI"),FontWeight=W.FontWeights.SemiBold,Foreground=p.Text });
            row.Children.Add(new C.TextBlock { Text=live?state!.ConnectionText:"等待连接",FontSize=12,Foreground=live?p.Accent:p.Muted,VerticalAlignment=W.VerticalAlignment.Bottom,Margin=new W.Thickness(12,0,0,8) });
            if(state?.IsWired!=true)
                card.Children.Add(new C.ProgressBar { Minimum=0,Maximum=100,Value=live?power?.Percent??0:0,Height=5,Foreground=power?.Percent<=20?Palette.Brush("#D57832"):p.Accent,Background=p.Stroke,BorderThickness=new W.Thickness(0) });
            card.Children.Add(new C.TextBlock { Text=error??(live && state!.IsWired?"此型号由 USB 供电，无需电池":context.Busy?"正在读取设备状态…":state is null?"正在检测设备…":"通过 "+state.Source+" 读取"),TextWrapping=W.TextWrapping.Wrap,FontSize=11,Foreground=error is null?p.Muted:Palette.Brush("#C47B31"),Margin=new W.Thickness(0,8,0,3) });
            card.Children.Add(new C.TextBlock { Text=state is { } s?$"{(live?"更新于":"上次读取")} {s.UpdatedAt:HH:mm:ss}"+(!live?$" · {s.ValueText}（非实时）":""):"尚未收到有效设备状态",FontSize=11,Foreground=p.Muted });
        }
        Device("鼠标","ROG OMNI 无线鼠标",context.State,context.Error);
        stack.Children.Add(new C.Border { Height=1,Background=p.Stroke,Margin=new W.Thickness(0,0,0,14) });
        Device("键盘","CHERRY 键盘",context.KeyboardState,context.KeyboardError);
        stack.Children.Add(new C.TextBlock { Text="每 30 秒刷新 · 两台设备分别读取",FontSize=11,Foreground=p.Muted,Margin=new W.Thickness(0,0,0,10) });
        var buttons=new C.Grid(); buttons.ColumnDefinitions.Add(new C.ColumnDefinition()); buttons.ColumnDefinitions.Add(new C.ColumnDefinition()); stack.Children.Add(buttons);
        var refresh=Button(context.Busy?"读取中…":"刷新电量",p,()=>_=context.Refresh(),"\uE72C"); refresh.IsEnabled=!context.Busy; buttons.Children.Add(refresh);
        var gear=Button("Gear Link",p,WidgetContext.OpenGearLink,"\uE774"); C.Grid.SetColumn(gear,1); buttons.Children.Add(gear);
    }
}
