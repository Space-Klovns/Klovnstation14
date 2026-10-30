using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Content.Server._KS14.Llm;

/// <summary>
///     A Windows job object created with <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>. The child is assigned to it,
///         and this process holds the only handle, so when this process dies - however it dies, crash included -
///         the OS closes the handle and kills the child with it. Disposing it does the same on purpose.
/// </summary>
internal sealed class KsLlmJobObject : IDisposable
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    private IntPtr _handle;

    private KsLlmJobObject(IntPtr handle)
    {
        _handle = handle;
    }

    /// <summary>
    ///     Windows only. Returns null, after logging, if the job could not be set up - the child still runs, it
    ///         just loses the hard-crash guarantee.
    /// </summary>
    public static KsLlmJobObject? TryCreateFor(Process process, ISawmill sawmill)
    {
        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
        {
            sawmill.Warning($"CreateJobObject failed ({Marshal.GetLastWin32Error()}); llama-server may outlive a crash.");
            return null;
        }

        var jobObject = new KsLlmJobObject(handle);

        var information = new JobObjectExtendedLimitInformationStruct
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = JobObjectLimitKillOnJobClose },
        };

        var length = Marshal.SizeOf<JobObjectExtendedLimitInformationStruct>();
        var informationPointer = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(information, informationPointer, fDeleteOld: false);

            if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, informationPointer, (uint)length)
                || !AssignProcessToJobObject(handle, process.Handle))
            {
                sawmill.Warning($"Could not put llama-server in a job object ({Marshal.GetLastWin32Error()}); it may outlive a crash.");
                jobObject.Dispose();
                return null;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(informationPointer);
        }

        return jobObject;
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;

        CloseHandle(_handle);
        _handle = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformationStruct
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int jobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
