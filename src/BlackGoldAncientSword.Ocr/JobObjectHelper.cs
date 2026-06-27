using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BlackGoldAncientSword.Ocr;

/// <summary>
/// Windows Job Object 封装，确保宿主进程退出时子进程被自动清理。
/// 使用 JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE 标志：
/// 当 Job 句柄关闭时（宿主退出/崩溃），OS 自动终止所有关联子进程。
/// </summary>
internal sealed class JobObjectHelper : IDisposable
{
    private readonly IntPtr _jobHandle;
    private bool _disposed;

    public JobObjectHelper()
    {
        _jobHandle = CreateJobObject(IntPtr.Zero, null);
        if (_jobHandle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject 失败");

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
            },
        };

        var infoPtr = Marshal.AllocHGlobal(Marshal.SizeOf(info));
        try
        {
            Marshal.StructureToPtr(info, infoPtr, false);
            if (!SetInformationJobObject(_jobHandle,
                    JobObjectInfoType.ExtendedLimitInformation,
                    infoPtr,
                    (uint)Marshal.SizeOf(info)))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject 失败");
        }
        finally
        {
            Marshal.FreeHGlobal(infoPtr);
        }
    }

    /// <summary>将指定进程绑定到此 Job，宿主退出时自动清理。</summary>
    public void AssignProcess(IntPtr processHandle)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(JobObjectHelper));

        if (!AssignProcessToJobObject(_jobHandle, processHandle))
        {
            // 进程可能已被其他 Job 绑定（如调试器），只警告不崩溃
            var error = Marshal.GetLastWin32Error();
            System.Diagnostics.Debug.WriteLine(
                $"AssignProcessToJobObject 失败 (0x{error:X})，子进程清理可能失效");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseHandle(_jobHandle);
        // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE 会在此触发
    }

    // ═══════════════════════════════════════════════
    //  P/Invoke
    // ═══════════════════════════════════════════════

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        JobObjectInfoType infoType,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private enum JobObjectInfoType
    {
        ExtendedLimitInformation = 9,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
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
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
