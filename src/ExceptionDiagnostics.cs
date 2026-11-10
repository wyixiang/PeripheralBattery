using System.Diagnostics;
using System.Text.Json;
namespace PeripheralBattery;

internal sealed record DiagnosticException(string Type,int HResult,string[] Stack,DiagnosticException[] Inner);
internal static class ExceptionDiagnostics
{
    // Messages, Data, URLs, file names and source paths can contain credentials
    // or service payloads. Keep only code identity, HResult and bounded frames.
    internal static DiagnosticException Capture(Exception error)
    {
        int framesLeft=32,exceptionsLeft=8;
        DiagnosticException Read(Exception value)
        {
            exceptionsLeft--;
            var stack=new List<string>();
            foreach(var frame in new StackTrace(value,false).GetFrames())
            {
                if(framesLeft--<=0 || stack.Count>=16) break;
                var method=frame.GetMethod();
                if(method is not null) stack.Add(Clip(method.DeclaringType?.FullName+"."+method.Name,256)+" [IL "+frame.GetILOffset()+"]");
            }
            var children=value is AggregateException aggregate?aggregate.InnerExceptions.Take(3):value.InnerException is { } inner?new[]{inner}:Enumerable.Empty<Exception>();
            var nested=new List<DiagnosticException>();
            foreach(var child in children) { if(exceptionsLeft<=0) break; nested.Add(Read(child)); }
            return new(Clip(value.GetType().FullName??value.GetType().Name,256),value.HResult,stack.ToArray(),nested.ToArray());
        }
        return Read(error);
    }
    internal static string Describe(Exception error)=>JsonSerializer.Serialize(Capture(error));
    static string Clip(string value,int count)=>value.Length<=count?value:value[..count];
}
