using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PeripheralBattery;

internal sealed record LaunchEnvironment(string? PackageFullName, bool InJob, uint? JobLimitFlags)
{
    internal bool HostBound => PackageFullName is not null || InJob;
}

internal static class LaunchIsolation
{
    internal const string RelaunchFlag = "--standalone";
    private const uint BreakawayOk = 0x800, SilentBreakawayOk = 0x1000;
    private const uint CreateSuspended = 0x4, ExtendedStartupInfoPresent = 0x80000, CreateBreakawayFromJob = 0x1000000;

    internal static LaunchEnvironment Current => Inspect(GetCurrentProcess());

    internal static LaunchEnvironment Inspect(IntPtr process)
    {
        uint length = 0;
        var result = GetPackageFullName(process, ref length, null);
        string? package = null;
        if (result == 122)
        {
            var name = new StringBuilder((int)length);
            result = GetPackageFullName(process, ref length, name);
            if (result == 0) package = name.ToString();
        }
        if (result != 0 && result != 15700) throw new Win32Exception(result);
        if (!IsProcessInJob(process, IntPtr.Zero, out var inJob)) throw new Win32Exception();
        uint? flags = null;
        if (inJob && process == GetCurrentProcess() && QueryInformationJobObject(IntPtr.Zero, 2, out var limits,
            (uint)Marshal.SizeOf<JobBasicLimits>(), out _)) flags = limits.LimitFlags;
        return new(package, inJob, flags);
    }

    // Run before taking the single-instance mutex. A development host may otherwise
    // end this app with its own package update or kill-on-close job.
    internal static bool TryRelaunch(string[] args)
    {
        var environment = Current;
        if (!environment.HostBound) return false;
        if (args.Contains(RelaunchFlag)) throw new InvalidOperationException("Independent launch did not leave the host environment.");
        StartIndependent(Environment.ProcessPath ?? throw new InvalidOperationException("Missing executable path."),
            args.Append(RelaunchFlag), AppContext.BaseDirectory);
        return true;
    }

    internal static uint StartIndependent(string executable, IEnumerable<string> args, string workingDirectory)
    {
        var environment = Current;
        var flags = CreateSuspended | ExtendedStartupInfoPresent | 0x08000000u; // CREATE_NO_WINDOW for launch helpers
        if (environment.InJob)
        {
            // Nested outer jobs may require an explicit breakaway even when
            // the innermost job enables silent breakaway.
            if ((environment.JobLimitFlags.GetValueOrDefault() & (BreakawayOk | SilentBreakawayOk)) != 0) flags |= CreateBreakawayFromJob;
            else if (environment.PackageFullName is null && environment.HostBound)
                throw new InvalidOperationException("The launch host does not permit an independent process. Start the app from File Explorer.");
        }
        nuint bytes = 0;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref bytes);
        var attributes = Marshal.AllocHGlobal(checked((int)bytes));
        var policy = Marshal.AllocHGlobal(sizeof(uint));
        var initialized = false;
        ProcessInformation child = default;
        var resumed = false;
        try
        {
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref bytes)) throw new Win32Exception();
            initialized = true;
            if (environment.PackageFullName is not null)
            {
                Marshal.WriteInt32(policy, 1); // PROCESS_CREATION_DESKTOP_APP_BREAKAWAY_ENABLE_PROCESS_TREE
                if (!UpdateProcThreadAttribute(attributes, 0, (nuint)0x20012, policy, (nuint)sizeof(uint), IntPtr.Zero, IntPtr.Zero))
                    throw new Win32Exception();
            }
            var startup = new StartupInfoEx { Info = new() { Size = (uint)Marshal.SizeOf<StartupInfoEx>() },
                Attributes = environment.PackageFullName is not null ? attributes : IntPtr.Zero };
            var commandLine = new StringBuilder(string.Join(" ", new[] { executable }.Concat(args).Select(QuoteArgument)));
            if (!CreateProcess(executable, commandLine, IntPtr.Zero, IntPtr.Zero, false, flags,
                IntPtr.Zero, workingDirectory, ref startup, out child)) throw new Win32Exception();
            if (Inspect(child.Process) is { } childEnvironment && childEnvironment.HostBound)
                throw new InvalidOperationException("The child did not leave the host package/job. Start the app from File Explorer.");
            if (ResumeThread(child.Thread) == uint.MaxValue) throw new Win32Exception();
            resumed = true;
            return child.ProcessId;
        }
        finally
        {
            if (child.Process != IntPtr.Zero)
            {
                if (!resumed) TerminateProcess(child.Process, 1); // Only our suspended, not-yet-running child.
                CloseHandle(child.Process);
            }
            if (child.Thread != IntPtr.Zero) CloseHandle(child.Thread);
            if (initialized) DeleteProcThreadAttributeList(attributes);
            Marshal.FreeHGlobal(policy); Marshal.FreeHGlobal(attributes);
        }
    }

    internal static string QuoteArgument(string argument)
    {
        var text = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\') { slashes++; continue; }
            text.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            text.Append(character); slashes = 0;
        }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobBasicLimits
    {
        public long ProcessTime, JobTime;
        public uint LimitFlags;
        public nuint MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcesses;
        public nuint Affinity;
        public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public uint Size;
        public string? Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
        public ushort ShowWindow, ReservedBytes;
        public IntPtr ReservedData, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo Info; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackageFullName(IntPtr process, ref uint length, StringBuilder? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool QueryInformationJobObject(IntPtr job, int infoClass, out JobBasicLimits info, uint size, out uint returned);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref nuint bytes);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcess(string application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInformation info);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
