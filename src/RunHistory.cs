using System.Text.Json;

namespace PeripheralBattery;

internal sealed record RunSnapshot(string RunId,int ProcessId,string Version,DateTimeOffset StartedAt,
    DateTimeOffset HeartbeatAt,string Phase,string? ExitReason=null,DateTimeOffset? EndedAt=null,
    int? ExitCode=null,string? LastException=null,string? CardState=null,bool Busy=false,
    DateTimeOffset? ExitRequestedAt=null,bool CleanupCompleted=false,bool ExitObserved=false,int DiagnosticFormat=0);

// Best-effort diagnostics must never bring down the widget. Only the primary
// instance owns this journal; --exit/--details helper processes do not touch it.
internal sealed class RunHistory
{
    readonly object gate=new();
    readonly string folder;
    readonly Func<DateTimeOffset> now;
    readonly long maxLogBytes;
    RunSnapshot snapshot;
    bool finished;
    internal RunSnapshot Snapshot { get { lock(gate) return snapshot; } }
    internal RunSnapshot? Previous { get; }

    internal RunHistory(string folder,string version,Func<DateTimeOffset>? clock=null,long maxLogBytes=256*1024)
    {
        this.folder=folder; now=clock??(()=>DateTimeOffset.Now); this.maxLogBytes=maxLogBytes;
        DiagnosticException? cacheError=null;
        try
        {
            var path=Path.Combine(folder,"session.json");
            if(File.Exists(path))
            {
                var previous=JsonSerializer.Deserialize<RunSnapshot>(File.ReadAllText(path));
                // Never re-emit a pre-sanitization exception message into new logs.
                if(previous is not null) Previous=previous with { LastException=previous.DiagnosticFormat==1?previous.LastException:null };
            }
        }
        catch(Exception e) { cacheError=ExceptionDiagnostics.Capture(e); }
        var time=now();
        snapshot=new(Guid.NewGuid().ToString("N"),Environment.ProcessId,version,time,time,"running",DiagnosticFormat:1);
        if(Previous is not null)
        {
            Safe(()=>WriteAtomic("previous-session.json",Previous));
            if(Previous.Phase=="exited" && (Previous.ExitRequestedAt is null || !Previous.CleanupCompleted || !Previous.ExitObserved || Previous.ExitCode!=0))
                Append("previous-exit-unverified","旧记录写为 exited，但没有足够的正常退出证据；不据此确认正常结束",Previous);
            else if(Previous.Phase is not ("exited" or "crashed"))
                Append(Previous.EndedAt is null?"previous-run-incomplete":"previous-run-abnormal","上次未正常结束，具体原因以已记录证据为准；没有证据时原因未确认",Previous);
        }
        if(cacheError is not null) Append("cache-read-error","无法读取上次运行快照",cacheError);
        Save(); Append("started","主实例启动");
    }
    internal void Note(string eventName,string? detail=null,Exception? error=null)
    {
        lock(gate)
        {
            if(finished) return;
            var diagnostic=error is null?null:ExceptionDiagnostics.Capture(error);
            if(diagnostic is not null && snapshot.Phase!="crashed") { snapshot=snapshot with { LastException=JsonSerializer.Serialize(diagnostic) }; Save(); }
            Append(eventName,detail,diagnostic);
        }
    }
    internal void Heartbeat(bool busy)
    {
        lock(gate)
        {
            if(finished) return;
            snapshot=snapshot with { HeartbeatAt=now(),Busy=busy }; Save();
        }
    }
    internal void CardState(string state)
    {
        lock(gate)
        {
            if(finished || snapshot.CardState==state) return;
            snapshot=snapshot with { CardState=state }; Save(); Append("card-state",state);
        }
    }
    internal void RequestExit(string reason)
    {
        lock(gate)
        {
            if(finished || snapshot.Phase=="crashed" || snapshot.Phase=="stopping" && snapshot.ExitReason==reason) return;
            snapshot=snapshot with { Phase="stopping",ExitReason=reason,ExitRequestedAt=snapshot.ExitRequestedAt??now() }; Save(); Append("exit-requested",reason);
        }
    }
    internal void MarkCleanupComplete()
    {
        lock(gate) { if(finished || snapshot.CleanupCompleted) return; snapshot=snapshot with { CleanupCompleted=true }; Save(); Append("cleanup-completed"); }
    }
    internal void ObserveExit(int exitCode)
    {
        lock(gate) { if(finished) return; snapshot=snapshot with { ExitObserved=true,ExitCode=snapshot.Phase=="crashed"?snapshot.ExitCode:exitCode }; Save(); Append("wpf-exit","ExitCode="+exitCode); }
    }
    internal void Crash(string source,Exception error)
    {
        lock(gate)
        {
            if(finished || snapshot.Phase=="crashed") return;
            snapshot=snapshot with { Phase="crashed",ExitReason=source,EndedAt=now(),ExitCode=1,LastException=ExceptionDiagnostics.Describe(error) };
            Save(); Append("crashed",source,snapshot.LastException);
        }
    }
    internal void Finish(string fallbackReason,int exitCode)
    {
        lock(gate)
        {
            if(finished) return;
            finished=true;
            if(snapshot.Phase!="crashed")
            {
                bool requested=snapshot.ExitRequestedAt is not null;
                int finalCode=exitCode!=0?exitCode:snapshot.ExitCode??exitCode;
                string phase=!requested?"unexpected-exit":!snapshot.CleanupCompleted || !snapshot.ExitObserved?"incomplete-exit":finalCode==0?"exited":"unexpected-exit";
                snapshot=snapshot with { Phase=phase,ExitReason=snapshot.ExitReason??fallbackReason,EndedAt=now(),ExitCode=finalCode };
            }
            Save(); Append("process-ended",snapshot.ExitReason,new { snapshot.Phase,snapshot.ExitCode });
        }
    }
    void Save()=>Safe(()=>WriteAtomic("session.json",snapshot));
    void WriteAtomic(string name,RunSnapshot value)
    {
        Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,name);
        var temp=path+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(value,new JsonSerializerOptions { WriteIndented=true }));
        File.Move(temp,path,true);
    }
    void Append(string eventName,string? detail=null,object? data=null)=>Safe(()=>
    {
        Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,"lifecycle.jsonl");
        if(File.Exists(path) && new FileInfo(path).Length>=maxLogBytes)
            File.Move(path,Path.Combine(folder,"lifecycle.previous.jsonl"),true);
        File.AppendAllText(path,JsonSerializer.Serialize(new { Time=now(),snapshot.RunId,snapshot.ProcessId,Event=eventName,Detail=detail,Data=data })+Environment.NewLine);
    });
    static void Safe(Action action) { try { action(); } catch { /* Logging cannot prevent startup or exit. */ } }
}
