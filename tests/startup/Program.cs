using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using PeripheralBattery;

internal static class StartupChecks
{
    static readonly string[] Payload=["","with spaces","中文路径","quoted\"value",@"C:\folder with space\","slashes\\\\\"end"];
    sealed record Probe(int ProcessId,LaunchEnvironment Environment,string[] Arguments);
    static int checks;
    static void Check(bool value,string name) { if(!value) throw new Exception(name); checks++; Console.WriteLine("PASS: "+name); }
    static Probe Read(string marker)
    {
        var timeout=Stopwatch.StartNew();
        while(timeout.Elapsed<TimeSpan.FromSeconds(8))
        {
            try { if(File.Exists(marker)) return JsonSerializer.Deserialize<Probe>(File.ReadAllText(marker))!; }
            catch(JsonException) { }
            catch(IOException) { }
            Thread.Sleep(20);
        }
        throw new Exception("Test child readiness timed out");
    }
    static string ReadHeartbeat(string marker)
    {
        var timeout=Stopwatch.StartNew();
        while(timeout.Elapsed<TimeSpan.FromSeconds(3))
        {
            try
            {
                using var stream=new FileStream(marker+".heartbeat",FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                using var reader=new StreamReader(stream);
                string value=reader.ReadToEnd();
                if(DateTimeOffset.TryParseExact(value,"O",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out _)) return value;
            }
            catch(IOException) { }
            Thread.Sleep(20);
        }
        throw new Exception("Test heartbeat read timed out");
    }
    [STAThread] static int Main(string[] args)
    {
        try { return Run(args); }
        catch(Exception error)
        {
            if(args.FirstOrDefault()=="--probe") try { File.WriteAllText(args[1]+".error",error.ToString()); } catch { }
            Console.Error.WriteLine("FAIL: "+error); return 1;
        }
    }
    static int Run(string[] args)
    {
        if(args.FirstOrDefault()=="--probe")
        {
            if(LaunchIsolation.TryRelaunch(args)) return 0;
            string marker=args[1];
            File.WriteAllText(marker,JsonSerializer.Serialize(new Probe(Environment.ProcessId,LaunchIsolation.Current,args)));
            var timeout=Stopwatch.StartNew();
            while(!File.Exists(marker+".stop") && timeout.Elapsed<TimeSpan.FromSeconds(20))
            { File.WriteAllText(marker+".heartbeat",DateTimeOffset.UtcNow.ToString("O")); Thread.Sleep(50); }
            return 0;
        }
        if(args.FirstOrDefault()=="--control")
        {
            while(!File.Exists(args[1]+".go")) Thread.Sleep(20);
            File.WriteAllText(args[1],JsonSerializer.Serialize(new Probe(Environment.ProcessId,LaunchIsolation.Current,args)));
            Thread.Sleep(20000); return 0;
        }
        if(args.FirstOrDefault()=="--host") return Host(args[1],uint.Parse(args[2]));

        var parent=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"PeripheralBattery-startup-checks"));
        var directory=Path.Combine(parent,Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            Check(!new LaunchEnvironment(null,false,null).HostBound,"ordinary desktop launch stays in the same process");
            Check(new LaunchEnvironment("example.package",false,null).HostBound,"package identity requires independent launch");
            Check(new LaunchEnvironment(null,true,0x2000).HostBound,"kill-on-close job requires independent launch");
            Check(new LaunchEnvironment(null,true,null).HostBound,"unknown job flags cannot be silently treated as independent");
            foreach(uint flags in new uint[]{0x2800,0x3000})
            {
                string marker=Path.Combine(directory,flags+".json");
                // Start the controlled host outside the real development host's
                // job before assigning its own disposable test job.
                uint pid=LaunchIsolation.StartIndependent(Environment.ProcessPath!,new[]{"--host",marker,flags.ToString()},AppContext.BaseDirectory);
                using var host=Process.GetProcessById((int)pid);
                _=host.Handle;
                var childProbe=Read(marker);
                using var child=Process.GetProcessById(childProbe.ProcessId);
                _=child.Handle;
                var controlProbe=Read(marker+".control");
                using var control=Process.GetProcessById(controlProbe.ProcessId);
                try
                {
                    Check(host.WaitForExit(8000) && host.ExitCode==0,"controlled host exits normally ("+flags.ToString("X")+")");
                    Check(controlProbe.Environment.InJob && control.WaitForExit(3000),"unisolated control is terminated by host job closure");
                    Check(!child.HasExited && childProbe.Environment.PackageFullName is null && !childProbe.Environment.InJob,"resident survives host exit outside package and every job");
                    string heartbeat=ReadHeartbeat(marker),next=heartbeat;
                    var live=Stopwatch.StartNew();
                    while(next==heartbeat && !child.HasExited && live.Elapsed<TimeSpan.FromSeconds(3)) { Thread.Sleep(50); next=ReadHeartbeat(marker); }
                    Check(!child.HasExited && next!=heartbeat,"resident continues executing after host is gone"+(File.Exists(marker+".error")?": "+File.ReadAllText(marker+".error"):""));
                    Check(childProbe.Arguments.Skip(2).Take(Payload.Length).SequenceEqual(Payload),"relaunch preserves empty, Unicode, quoted and trailing-slash arguments");
                    Check(childProbe.Arguments.Count(a=>a==LaunchIsolation.RelaunchFlag)==1,"independent relaunch completes once without recursion");
                }
                finally { File.WriteAllText(marker+".stop","stop"); if(!child.WaitForExit(5000)) throw new Exception("Test resident did not stop"); }
            }
            var source=Path.Combine(directory,"package-cache"); var destination=Path.Combine(directory,"canonical"); Directory.CreateDirectory(source);
            const string original="{\"Offset\":-3051,\"Locked\":false,\"Theme\":\"system\"}";
            File.WriteAllText(Path.Combine(source,"preferences.json"),original);
            File.WriteAllText(Path.Combine(source,"session.json"),"{\"RunId\":\"old-evidence\"}");
            File.WriteAllText(Path.Combine(source,"lifecycle.jsonl"),"old evidence");
            var imported=AppDataPaths.Import(destination,new[]{source});
            Check(imported.Count==3 && File.ReadAllText(Path.Combine(destination,"preferences.json"))==original,"settings and old session evidence are imported without rewriting contents");
            File.WriteAllText(Path.Combine(source,"preferences.json"),"{\"Offset\":999}");
            Check(AppDataPaths.Import(destination,new[]{source}).Count==0 && File.ReadAllText(Path.Combine(destination,"preferences.json"))==original,"existing canonical settings and logs are never overwritten");
            Check(File.Exists(Path.Combine(source,"session.json")),"original packaged evidence is retained");
            var bad=Path.Combine(directory,"corrupt"); Directory.CreateDirectory(bad); File.WriteAllText(Path.Combine(bad,"preferences.json"),"not json");
            Check(AppDataPaths.Import(Path.Combine(directory,"new"),new[]{bad}).Single().Result=="skipped-unreadable","corrupt package settings do not prevent startup");
            var location=AppDataPaths.Inspect(destination);
            Check(string.Equals(location.ActualDirectory,Path.GetFullPath(destination),StringComparison.OrdinalIgnoreCase),"diagnostics resolve actual opened file location");
            Check(!AppDataPaths.LocalRoot.Contains("\\Packages\\",StringComparison.OrdinalIgnoreCase),"canonical LocalAppData is outside package cache");
            Console.WriteLine($"PASS: {checks} startup isolation and migration checks");
            return 0;
        }
        finally
        {
            // Only this test's newly-created GUID directory may be removed.
            string resolved=Path.GetFullPath(directory);
            if(!string.Equals(Path.GetDirectoryName(resolved),parent,StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(resolved),"N",out _)) throw new Exception("Unsafe test cleanup path");
            Directory.Delete(resolved,true);
        }
    }
    static int Host(string marker,uint flags)
    {
        IntPtr job=CreateJobObject(IntPtr.Zero,null); if(job==IntPtr.Zero) throw new Win32Exception();
        bool launched=false;
        try
        {
            var limits=new ExtendedLimits { Basic=new() { LimitFlags=flags } };
            if(!SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf<ExtendedLimits>()) || !AssignProcessToJobObject(job,Process.GetCurrentProcess().Handle)) throw new Win32Exception();
            var info=new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false,CreateNoWindow=true };
            info.ArgumentList.Add("--control"); info.ArgumentList.Add(marker+".control");
            using var control=Process.Start(info)!;
            if(!AssignProcessToJobObject(job,control.Handle)) throw new Win32Exception();
            File.WriteAllText(marker+".control.go","go");
            _=Read(marker+".control");
            if(!LaunchIsolation.TryRelaunch(new[]{"--probe",marker}.Concat(Payload).ToArray())) throw new Exception("Host isolation was skipped");
            _=Read(marker); Thread.Sleep(300); launched=true; return 0;
        }
        finally { if(!launched) CloseHandle(job); }
        // On success the OS closes this process's non-inherited job handle on exit.
    }
    [StructLayout(LayoutKind.Sequential)] struct BasicLimits { public long ProcessTime,JobTime; public uint LimitFlags; public nuint MinimumWorkingSet,MaximumWorkingSet; public uint ActiveProcesses; public nuint Affinity; public uint Priority,Scheduling; }
    [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits { public BasicLimits Basic; public ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes; public nuint ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory; }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job,int type,ref ExtendedLimits limits,uint size);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
}
