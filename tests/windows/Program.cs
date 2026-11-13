using W=System.Windows;
using I=System.Windows.Input;
using System.Windows.Threading;
namespace PeripheralBattery;

internal static class Program
{
    internal static HistoryStub? History=>null;
    static int checks;
    static void Check(bool value,string name) { if(!value) throw new Exception(name); checks++; }
    static void Pump()
    {
        var frame=new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>frame.Continue=false));
        Dispatcher.PushFrame(frame);
    }
    static I.KeyEventArgs Escape(W.Window window)
    {
        var key=new I.KeyEventArgs(I.Keyboard.PrimaryDevice,W.PresentationSource.FromVisual(window),0,I.Key.Escape) { RoutedEvent=I.Keyboard.PreviewKeyDownEvent };
        window.RaiseEvent(key); return key;
    }
    [STAThread] static void Main()
    {
        var app=new W.Application { ShutdownMode=W.ShutdownMode.OnExplicitShutdown };
        bool asyncComplete=false,wpfContext=false; int before=0,after=0;
        UiDispatch.Post(app.Dispatcher,()=>true,async ()=> { before=Environment.CurrentManagedThreadId; wpfContext=SynchronizationContext.Current is DispatcherSynchronizationContext; await Task.Run(()=>Thread.Sleep(20)); after=Environment.CurrentManagedThreadId; asyncComplete=true; });
        var wait=System.Diagnostics.Stopwatch.StartNew();
        while(!asyncComplete && wait.Elapsed<TimeSpan.FromSeconds(3)) { Pump(); Thread.Sleep(5); }
        Check(asyncComplete && wpfContext && before==after && before==Environment.CurrentManagedThreadId,"initial queued async refresh captures WPF context before the main Run loop");
        var context=new WidgetContext { InspectUi=true };
        var dock=new DockWindow(context) { Left=-20000,Top=-20000 };
        context.Dock=dock;
        Check(dock.Handle==IntPtr.Zero,"reading Handle never creates a native window");
        dock.Show();
        var originalHandle=dock.Handle;
        Check(originalHandle!=IntPtr.Zero && !dock.ShowInTaskbar,"card has a handle but no independent taskbar button even in inspect mode");
        bool blockedDuringClosing=false;
        dock.Closing+=(_,e)=> { blockedDuringClosing=!dock.CanRestore; e.Cancel=false; };
        dock.Close();
        Check(blockedDuringClosing && !dock.IsDisposed && !dock.IsClosing && dock.Handle==originalHandle,"unexpected Close is cancelled and transient closing state resets");
        dock.DockToTaskbar(); dock.RestoreTopmost();
        Check(!dock.IsDisposed && dock.Handle==originalHandle,"position/restore after cancelled Close retains the same live card");
        dock.Show(); Escape(dock);
        Check(!dock.IsDisposed && dock.IsVisible,"Esc does not destroy the persistent card");
        var details=new DetailsWindow(context) { Left=-20000,Top=-20000,WindowStartupLocation=W.WindowStartupLocation.Manual };
        for(int i=0;i<3;i++) { details.Show(); if(i==1) details.WindowState=W.WindowState.Minimized; else details.Close(); Check(!details.ShowInTaskbar && !details.IsDisposed && !details.IsClosing && !details.IsVisible && details.WindowState==W.WindowState.Normal,"details Close/system minimize dismisses without destroying window or showing taskbar button #"+i); }
        details.Show(); var escaped=Escape(details);
        Check(escaped.Handled && !details.IsDisposed && !details.IsVisible && !dock.IsDisposed,"details Esc dismisses only the floating window");
        int late=0;
        Check(UiDispatch.Post(dock.Dispatcher,()=>!context.IsQuitting,()=> { late++; dock.DockToTaskbar(); dock.RestoreTopmost(); }),"restore can be queued while live");
        context.IsQuitting=true;
        details.Close(); dock.Close(); Pump();
        Check(late==0 && dock.IsDisposed && details.IsDisposed,"queued restore is ignored after real exit and windows are destroyed");
        // Even an obsolete owner accidentally marked live must not resurrect Closed.
        context.IsQuitting=false; context.Settings.Visible=true;
        dock.DockToTaskbar(); dock.RestoreTopmost(); dock.Repaint();
        Check(dock.Handle==IntPtr.Zero && dock.Bounds.IsEmpty && !dock.IsVisible,"Closed instance is rejected without Show or EnsureHandle");
        int updates=0;
        using(var watcher=new TaskbarLayerWatcher(Dispatcher.CurrentDispatcher,()=>updates++,installHooks:false))
        {
            watcher.QueueUpdate(); watcher.QueueUpdate(); Pump();
            Check(updates==1,"layer callbacks coalesce");
            watcher.QueueUpdate(); watcher.Dispose(); Pump(); watcher.QueueUpdate(); Pump();
            Check(updates==1,"disposed layer watcher ignores already queued and new callbacks");
        }
        // Exercise real native hook installation/unhooking in this isolated process.
        using(var hooked=new TaskbarLayerWatcher(Dispatcher.CurrentDispatcher,()=>updates++)) { hooked.Dispose(); hooked.QueueUpdate(); Pump(); }
        Check(updates==1,"native watcher can be disposed repeatedly without reviving callbacks");
        bool rejected=false; int afterShutdown=0; Exception? threadError=null;
        var thread=new Thread(()=>
        {
            try
            {
                var dispatcher=Dispatcher.CurrentDispatcher;
                UiDispatch.Post(dispatcher,()=>true,()=>afterShutdown++,DispatcherPriority.Background);
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                Dispatcher.Run();
                rejected=!UiDispatch.Post(dispatcher,()=>true,()=>afterShutdown++);
            }
            catch(Exception error) { threadError=error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Check(thread.Join(5000) && threadError is null && rejected && afterShutdown==0,"dispatcher shutdown rejects late work and abandons queued restores");
        Console.WriteLine($"PASS: {checks} isolated STA/WPF lifecycle checks");
        app.Shutdown();
    }
}
