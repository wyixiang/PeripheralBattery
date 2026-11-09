using System.Text.Json;
namespace MouseBattery;
internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        try { Run(args); }
        catch(Exception e) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"startup-error.txt"),e.ToString()); }
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
                            catch(Exception e) { lines.Add(e.ToString()); }
                        }
                    }
                }
            }
            catch(Exception e) { lines.Add(e.ToString()); }
            try { lines.Add("KEYBOARD "+JsonSerializer.Serialize(CherryReader.Read().GetAwaiter().GetResult())); }
            catch(Exception e) { lines.Add("KEYBOARD "+e.GetType().Name+": "+e.Message); }
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"diagnostic.txt"),lines);
            return;
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var command=args.Contains("--exit")?"Exit":args.Contains("--menu")?"Menu":"Show";
        if(args.Contains("--exit")) { Signal(command); return; }
        using var mutex=new Mutex(true,@"Local\MouseBattery.Personal",out var first);
        if(!first) { Signal(command); return; }
        var app=new System.Windows.Application { ShutdownMode=System.Windows.ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException+=(_,e)=>WidgetContext.Log(e.Exception);
        using var context=new WidgetContext(args.Contains("--inspect-ui"));
        if(args.Contains("--details")) app.Dispatcher.BeginInvoke(new Action(context.ShowDetails));
        app.Run();
    }
    static void Signal(string name)
    {
        try { using var handle=EventWaitHandle.OpenExisting(@"Local\MouseBattery."+name); handle.Set(); }
        catch(WaitHandleCannotBeOpenedException) { }
    }
}
