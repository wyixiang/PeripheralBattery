using System.Text.Json;
namespace PeripheralBattery;
internal static class Program
{
    internal static RunHistory? History { get; private set; }
    static Mutex? instanceMutex;
    [STAThread] static void Main(string[] args)
    {
        try
        {
            // Command helpers are short-lived and must not own resident state.
            if(!args.Contains("--exit") && !args.Contains("--diagnose") && LaunchIsolation.TryRelaunch(args)) return;
            Run(args);
        }
        catch(Exception e)
        {
            Environment.ExitCode=1;
            History?.Crash("main-unhandled-exception",e);
            try { Directory.CreateDirectory(Preferences.Folder); File.WriteAllText(Path.Combine(Preferences.Folder,"startup-error.txt"),ExceptionDiagnostics.Describe(e)); } catch { }
        }
        finally
        {
            History?.Finish("main-returned-without-exit-request",Environment.ExitCode);
            // Finish the journal while still owning the single-instance lock.
            // A quickly restarted process must not have its cache overwritten.
            if(instanceMutex is not null) { instanceMutex.ReleaseMutex(); instanceMutex.Dispose(); }
        }
    }
    static void Run(string[] args)
    {
        if(args.Contains("--diagnose"))
        {
            var lines=new List<string>();
            try
            {
                var devices=Hid.Enumerate();
                foreach(var d in devices)
                {
                    lines.Add(JsonSerializer.Serialize(d));
                    if(d.ProductId==6862 && d.UsagePage==0xff02 && d.OutputLength>0)
                    {
                        foreach(var id in d.OutputIds)
                        {
                            try
                            {
                                using var session=new HidSession(d);
                                var scan=session.Query(id,0xa0).GetAwaiter().GetResult(); lines.Add("PAIRED "+Convert.ToHexString(scan));
                                int count=scan[2]|scan[3]<<8;
                                for(int k=0;k<Math.Min(count,8);k++)
                                {
                                    int offset=4+k*4;
                                    var report=scan[offset+2];
                                    if(report!=3) continue;
                                    var mouse=devices.First(m=>m.ProductId==d.ProductId && m.OutputIds.Contains(report));
                                    using var mouseSession=new HidSession(mouse);
                                    var power=mouseSession.Query(report,0x12,7).GetAwaiter().GetResult(); lines.Add("POWER "+Convert.ToHexString(power));
                                }
                            }
                            catch(Exception e) { lines.Add(ExceptionDiagnostics.Describe(e)); }
                        }
                    }
                }
            }
            catch(Exception e) { lines.Add(ExceptionDiagnostics.Describe(e)); }
            try { lines.Add("KEYBOARD "+JsonSerializer.Serialize(CherryReader.Read().GetAwaiter().GetResult())); }
            catch(Exception e) { lines.Add("KEYBOARD "+ExceptionDiagnostics.Describe(e)); }
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"diagnostic.txt"),lines);
            return;
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var command=args.Contains("--exit")?"Exit":args.Contains("--menu")?"Menu":"Show";
        if(args.Contains("--exit")) { Signal(command); return; }
        var mutex=new Mutex(true,@"Local\PeripheralBattery.Personal",out var first);
        if(!first) { mutex.Dispose(); Signal(command); return; }
        instanceMutex=mutex;
        var migrations=AppDataPaths.Import(Preferences.Folder,new[]{AppDataPaths.PackagedFolder,Path.Combine(AppDataPaths.LocalRoot,"MouseBattery")});
        History=new RunHistory(Preferences.Folder,typeof(Program).Assembly.GetName().Version?.ToString()??"unknown");
        History.RecordStartup(LaunchIsolation.Current,AppDataPaths.Inspect(Preferences.Folder));
        foreach(var migration in migrations) History.Note("data-migration",migration.File+": "+migration.Result);
        WidgetContext? context=null;
        AppDomain.CurrentDomain.UnhandledException+=(_,e)=>
        {
            Environment.ExitCode=1;
            context?.BeginStopping();
            History.Crash("appdomain-unhandled-exception",e.ExceptionObject as Exception??new Exception());
        };
        AppDomain.CurrentDomain.ProcessExit+=(_,_)=>History.Finish("process-exit-without-request",Environment.ExitCode);
        TaskScheduler.UnobservedTaskException+=(_,e)=>History.Note("unobserved-task-exception","后台任务异常（未直接终止进程）",e.Exception);
        var app=new System.Windows.Application { ShutdownMode=System.Windows.ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException+=(_,e)=> { context?.BeginStopping(); History.Crash("wpf-dispatcher-unhandled-exception",e.Exception); WidgetContext.Log(e.Exception); };
        Application.ThreadException+=(_,e)=>
        {
            History.Crash("winforms-thread-exception",e.Exception);
            context?.BeginStopping();
            Environment.ExitCode=1;
            UiDispatch.Post(app.Dispatcher,()=>true,()=>app.Shutdown(1));
        };
        app.SessionEnding+=(_,e)=> { History.RequestExit("windows-session-ending: "+e.ReasonSessionEnding); context?.BeginStopping(); };
        app.Exit+=(_,e)=>History.ObserveExit(e.ApplicationExitCode);
        using(context=new WidgetContext(args.Contains("--inspect-ui")))
        {
            if(args.Contains("--details")) UiDispatch.Post(app.Dispatcher,()=>!context.IsQuitting,context.ShowDetails);
            History.Note("ready","任务栏卡片、托盘与刷新器已创建");
            try { app.Run(); }
            finally { context.BeginStopping(); }
        }
    }
    static void Signal(string name)
    {
        try { using var handle=EventWaitHandle.OpenExisting(@"Local\PeripheralBattery."+name); handle.Set(); }
        catch(WaitHandleCannotBeOpenedException) { }
    }
}
