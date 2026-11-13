using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
namespace PeripheralBattery;

internal sealed record DataLocation(string RequestedDirectory,string? ActualDirectory);
internal sealed record MigrationResult(string File,string Result);
internal static class AppDataPaths
{
    internal const string CodexFamily="OpenAI.Codex_2p2nqsd0c76g0";
    internal static string LocalRoot
    {
        get
        {
            var folder=new Guid("F1B32785-6FBA-4FCF-9D55-7B8E7F157091"); // LocalAppData
            Marshal.ThrowExceptionForHR(SHGetKnownFolderPath(ref folder,0x10000,IntPtr.Zero,out var path)); // NO_PACKAGE_REDIRECTION
            try { return Marshal.PtrToStringUni(path)??throw new IOException("Missing LocalAppData path"); }
            finally { Marshal.FreeCoTaskMem(path); }
        }
    }
    internal static string Folder=>Path.Combine(LocalRoot,"PeripheralBattery");
    internal static string PackagedFolder=>Path.Combine(LocalRoot,"Packages",CodexFamily,"LocalCache","Local","PeripheralBattery");

    // Copy only known application records and never overwrite canonical files.
    // Old package records remain intact as evidence; no launch setting is changed.
    internal static IReadOnlyList<MigrationResult> Import(string destination,IEnumerable<string> sources)
    {
        var results=new List<MigrationResult>();
        foreach(var source in sources)
        {
            if(string.Equals(Path.GetFullPath(source),Path.GetFullPath(destination),StringComparison.OrdinalIgnoreCase)) continue;
            foreach(var name in new[]{"preferences.json","session.json","previous-session.json","status.json","lifecycle.jsonl","lifecycle.previous.jsonl","error.log"})
            {
                string old=Path.Combine(source,name),target=Path.Combine(destination,name);
                if(!File.Exists(old) || File.Exists(target)) continue;
                try
                {
                    if(new FileInfo(old).Length>1024*1024) { results.Add(new(name,"skipped-too-large")); continue; }
                    if(name.EndsWith(".json",StringComparison.Ordinal))
                    {
                        using var json=JsonDocument.Parse(File.ReadAllText(old));
                        if(json.RootElement.ValueKind!=JsonValueKind.Object) throw new InvalidDataException();
                    }
                    Directory.CreateDirectory(destination);
                    File.Copy(old,target,false);
                    results.Add(new(name,"imported"));
                }
                catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException) { results.Add(new(name,"skipped-unreadable")); }
            }
        }
        return results;
    }
    internal static DataLocation Inspect(string folder)
    {
        try
        {
            using var file=File.OpenHandle(Path.Combine(folder,"session.json"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            var path=new StringBuilder(32768);
            uint length=GetFinalPathNameByHandle(file,path,(uint)path.Capacity,0);
            if(length==0 || length>=path.Capacity) return new(folder,null);
            string actual=path.ToString();
            if(actual.StartsWith(@"\\?\UNC\",StringComparison.OrdinalIgnoreCase)) actual=@"\\"+actual[8..];
            else if(actual.StartsWith(@"\\?\",StringComparison.Ordinal)) actual=actual[4..];
            return new(folder,Path.GetDirectoryName(actual));
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) { return new(folder,null); }
    }
    [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath(ref Guid folder,uint flags,IntPtr token,out IntPtr path);
    [DllImport("kernel32.dll",EntryPoint="GetFinalPathNameByHandleW",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetFinalPathNameByHandle(SafeFileHandle file,StringBuilder path,uint count,uint flags);
}
