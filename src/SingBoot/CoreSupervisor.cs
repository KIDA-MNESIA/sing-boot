using Microsoft.Win32.SafeHandles;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SingBoot;

public enum CoreState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Failed
}

public enum CoreEventKind
{
    StateChanged,
    Error
}

public readonly struct CoreEvent
{
    public CoreEventKind Kind { get; }
    public CoreState State { get; }
    public string Message { get; }

    public CoreEvent(CoreEventKind kind, CoreState state, string message)
    {
        Kind = kind;
        State = state;
        Message = message;
    }
}

/// <summary>
/// Background thread that manages the sing-box process lifecycle.
/// Uses a Windows Job Object to ensure the child process is killed when the parent exits.
/// Feeds sing-box config via stdin pipe (<c>sing-box run -c stdin</c>).
/// </summary>
public sealed class CoreSupervisor : IDisposable
{
    private const int StartupGracePeriodMs = 2000;
    private const int MaxCapturedStderrLines = 20;

    private static readonly Regex AnsiEscapeRegex = new(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);

    private readonly string _exePath;
    private readonly BlockingCollection<SupervisorCommand> _queue = new(10);
    private readonly Thread _thread;
    private readonly object _stderrLock = new();
    private readonly Queue<string> _stderrLines = new();

    private IntPtr _jobHandle;
    private IntPtr _processHandle;
    private uint _processId;
    private Thread? _stderrReaderThread;
    private DateTime _startupDeadlineUtc = DateTime.MinValue;
    private CoreState _state = CoreState.Stopped;
    private bool _disposed;

    public event Action<CoreEvent>? OnEvent;

    public CoreState State => _state;
    public uint ProcessId => _processId;

    public CoreSupervisor(string exePath)
    {
        _exePath = exePath;

        _thread = new Thread(Run) { IsBackground = true, Name = "CoreSupervisor" };
        _thread.Start();
    }

    public void RequestStart(string configJson)
    {
        _queue.TryAdd(new SupervisorCommand(CommandKind.Start, configJson));
    }

    public void RequestStop()
    {
        _queue.TryAdd(new SupervisorCommand(CommandKind.Stop, ""));
    }

    /// <summary>
    /// Signals the supervisor to shut down. Blocks until the thread exits.
    /// </summary>
    public void Shutdown()
    {
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(15));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _queue.CompleteAdding(); } catch { }
        if (_thread.IsAlive)
            _thread.Join(TimeSpan.FromSeconds(15));

        CleanupProcess();
        _queue.Dispose();
    }

    private void Run()
    {
        try
        {
            while (!_queue.IsCompleted)
            {
                CheckProcessStatus();

                if (_queue.TryTake(out var cmd, millisecondsTimeout: 200))
                    HandleCommand(cmd);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }

        DoStopGraceful();
    }

    private void HandleCommand(SupervisorCommand cmd)
    {
        switch (cmd.Kind)
        {
            case CommandKind.Start:
                DoStart(cmd.ConfigJson);
                break;
            case CommandKind.Stop:
                DoStopGraceful();
                break;
        }
    }

    private void DoStart(string configJson)
    {
        DoStopGraceful();
        ClearCapturedStderr();
        SetStateAndNotify(CoreState.Starting);

        IntPtr jobHandle = IntPtr.Zero;
        IntPtr processHandle = IntPtr.Zero;
        IntPtr threadHandle = IntPtr.Zero;
        IntPtr stdinRead = IntPtr.Zero;
        IntPtr stdinWrite = IntPtr.Zero;
        IntPtr stderrRead = IntPtr.Zero;
        IntPtr stderrWrite = IntPtr.Zero;
        IntPtr hNullOut = IntPtr.Zero;
        var stderrReaderStarted = false;

        try
        {
            if (!File.Exists(_exePath))
                throw new FileNotFoundException("sing-box executable not found.");

            jobHandle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
            if (jobHandle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject failed.");

            var jobInfo = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            jobInfo.BasicLimitInformation.LimitFlags = NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            if (!NativeMethods.SetInformationJobObject(jobHandle,
                    NativeMethods.JobObjectInfoType.ExtendedLimitInformation,
                    ref jobInfo, (uint)Marshal.SizeOf(jobInfo)))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject failed.");

            var sa = new NativeMethods.SECURITY_ATTRIBUTES
            {
                nLength = (uint)Marshal.SizeOf<NativeMethods.SECURITY_ATTRIBUTES>(),
                bInheritHandle = true,
                lpSecurityDescriptor = IntPtr.Zero
            };

            if (!NativeMethods.CreatePipe(out stdinRead, out stdinWrite, ref sa, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe failed.");
            if (!NativeMethods.SetHandleInformation(stdinWrite, NativeMethods.HANDLE_FLAG_INHERIT, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetHandleInformation (stdin) failed.");

            if (!NativeMethods.CreatePipe(out stderrRead, out stderrWrite, ref sa, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe (stderr) failed.");
            if (!NativeMethods.SetHandleInformation(stderrRead, NativeMethods.HANDLE_FLAG_INHERIT, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetHandleInformation (stderr) failed.");

            hNullOut = NativeMethods.CreateFileW("NUL",
                NativeMethods.GENERIC_WRITE,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                ref sa, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
            if (hNullOut == NativeMethods.INVALID_HANDLE_VALUE)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateFile (NUL) failed.");

            var si = new NativeMethods.STARTUPINFO
            {
                cb = (uint)Marshal.SizeOf<NativeMethods.STARTUPINFO>(),
                dwFlags = NativeMethods.STARTF_USESHOWWINDOW | NativeMethods.STARTF_USESTDHANDLES,
                wShowWindow = NativeMethods.SW_HIDE,
                hStdInput = stdinRead,
                hStdOutput = hNullOut,
                hStdError = stderrWrite
            };

            var cmdLine = $"\"{_exePath}\" run -c stdin";
            var workDir = Path.GetDirectoryName(_exePath) ?? ".";

            if (!NativeMethods.CreateProcess(null, cmdLine, IntPtr.Zero, IntPtr.Zero,
                    bInheritHandles: true,
                    NativeMethods.CREATE_NEW_CONSOLE | NativeMethods.CREATE_SUSPENDED,
                    IntPtr.Zero, workDir, ref si, out var pi))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcess failed.");

            processHandle = pi.hProcess;
            threadHandle = pi.hThread;
            var processId = pi.dwProcessId;

            SafeCloseHandle(ref stdinRead);
            SafeCloseHandle(ref stderrWrite);
            SafeCloseHandle(ref hNullOut);

            if (!NativeMethods.AssignProcessToJobObject(jobHandle, processHandle))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject failed.");

            StartStderrReader(stderrRead);
            stderrReaderStarted = true;
            stderrRead = IntPtr.Zero;

            if (NativeMethods.ResumeThread(threadHandle) == unchecked((uint)-1))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ResumeThread failed.");
            SafeCloseHandle(ref threadHandle);

            var configBytes = Encoding.UTF8.GetBytes(configJson);
            WriteAll(stdinWrite, configBytes);
            SafeCloseHandle(ref stdinWrite);

            _jobHandle = jobHandle;
            _processHandle = processHandle;
            _processId = processId;
            _startupDeadlineUtc = DateTime.UtcNow.AddMilliseconds(StartupGracePeriodMs);
        }
        catch (Exception ex)
        {
            uint? exitCode = null;

            if (processHandle != IntPtr.Zero)
            {
                try { NativeMethods.TerminateProcess(processHandle, 1); } catch { }
                NativeMethods.WaitForSingleObject(processHandle, 500);
                exitCode = TryGetExitCode(processHandle);
            }

            SafeCloseHandle(ref stdinRead);
            SafeCloseHandle(ref stdinWrite);
            SafeCloseHandle(ref stderrWrite);
            SafeCloseHandle(ref hNullOut);
            SafeCloseHandle(ref processHandle);
            SafeCloseHandle(ref threadHandle);
            SafeCloseHandle(ref jobHandle);

            if (!stderrReaderStarted)
                SafeCloseHandle(ref stderrRead);

            DrainStderrReader(TimeSpan.FromMilliseconds(300));
            _startupDeadlineUtc = DateTime.MinValue;

            var message = BuildFailureMessage(
                duringStartup: true,
                stderrSummary: GetCapturedStderrSummary(),
                exitCode: exitCode,
                fallbackMessage: ex.Message);
            SetStateAndNotify(CoreState.Failed, message);
        }
    }

    private static void WriteAll(IntPtr handle, byte[] data)
    {
        var offset = 0;
        while (offset < data.Length)
        {
            var remaining = data.Length - offset;
            var chunk = new byte[remaining];
            Buffer.BlockCopy(data, offset, chunk, 0, remaining);

            if (!NativeMethods.WriteFile(handle, chunk, (uint)chunk.Length, out var written, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WriteFile (stdin) failed.");
            if (written == 0)
                throw new InvalidOperationException("WriteFile (stdin) wrote 0 bytes.");

            offset += (int)written;
        }
    }

    private void DoStopGraceful()
    {
        if (_processHandle == IntPtr.Zero)
            return;

        SetStateAndNotify(CoreState.Stopping);
        var kill = true;

        if (SendCtrlC(_processId))
        {
            if (NativeMethods.WaitForSingleObject(_processHandle, 10_000) != NativeMethods.WAIT_TIMEOUT)
                kill = false;
        }

        if (kill)
        {
            NativeMethods.TerminateProcess(_processHandle, 1);
            NativeMethods.WaitForSingleObject(_processHandle, 1000);
        }

        CleanupProcess();
        SetStateAndNotify(CoreState.Stopped);
    }

    private static bool SendCtrlC(uint processId)
    {
        NativeMethods.FreeConsole();
        if (!NativeMethods.AttachConsole(processId))
            return false;

        try
        {
            NativeMethods.SetConsoleCtrlHandler(null, true);
            try
            {
                var result = NativeMethods.GenerateConsoleCtrlEvent(NativeMethods.CTRL_C_EVENT, 0);
                Thread.Sleep(10);
                return result;
            }
            finally
            {
                NativeMethods.SetConsoleCtrlHandler(null, false);
            }
        }
        finally
        {
            NativeMethods.FreeConsole();
        }
    }

    private void CheckProcessStatus()
    {
        if (_processHandle == IntPtr.Zero)
            return;

        if (NativeMethods.WaitForSingleObject(_processHandle, 0) != NativeMethods.WAIT_TIMEOUT)
        {
            HandleObservedProcessExit(_state == CoreState.Starting);
            return;
        }

        if (_state == CoreState.Starting &&
            _startupDeadlineUtc != DateTime.MinValue &&
            DateTime.UtcNow >= _startupDeadlineUtc)
        {
            _startupDeadlineUtc = DateTime.MinValue;
            SetStateAndNotify(CoreState.Running);
        }
    }

    private void HandleObservedProcessExit(bool duringStartup)
    {
        var exitCode = TryGetExitCode(_processHandle);
        DrainStderrReader(TimeSpan.FromMilliseconds(500));
        var summary = GetCapturedStderrSummary();

        CleanupProcess();

        var fallback = duringStartup
            ? "process exited before becoming ready."
            : "process exited without additional error output.";
        var message = BuildFailureMessage(duringStartup, summary, exitCode, fallback);
        SetStateAndNotify(CoreState.Failed, message);
    }

    private void CleanupProcess()
    {
        SafeCloseHandle(ref _processHandle);
        SafeCloseHandle(ref _jobHandle);
        _processId = 0;
        _startupDeadlineUtc = DateTime.MinValue;
        DrainStderrReader(TimeSpan.FromMilliseconds(500));
    }

    private void StartStderrReader(IntPtr stderrReadHandle)
    {
        var readerThread = new Thread(() => CaptureStderr(stderrReadHandle))
        {
            IsBackground = true,
            Name = "CoreSupervisor.Stderr"
        };

        _stderrReaderThread = readerThread;
        readerThread.Start();
    }

    private void CaptureStderr(IntPtr stderrReadHandle)
    {
        try
        {
            using var handle = new SafeFileHandle(stderrReadHandle, ownsHandle: true);
            using var stream = new FileStream(handle, FileAccess.Read, 4096, isAsync: false);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, false));

            while (true)
            {
                var line = reader.ReadLine();
                if (line is null)
                    break;

                var sanitized = SanitizeStderrLine(line);
                if (string.IsNullOrWhiteSpace(sanitized))
                    continue;

                lock (_stderrLock)
                {
                    if (_stderrLines.Count == MaxCapturedStderrLines)
                        _stderrLines.Dequeue();
                    _stderrLines.Enqueue(sanitized);
                }
            }
        }
        catch
        {
        }
    }

    private void ClearCapturedStderr()
    {
        lock (_stderrLock)
        {
            _stderrLines.Clear();
        }
    }

    private void DrainStderrReader(TimeSpan timeout)
    {
        var readerThread = _stderrReaderThread;
        if (readerThread is null)
            return;

        readerThread.Join(timeout);

        _stderrReaderThread = null;
    }

    private string GetCapturedStderrSummary()
    {
        string[] lines;
        lock (_stderrLock)
        {
            lines = _stderrLines.ToArray();
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("FATAL", StringComparison.OrdinalIgnoreCase))
                return SimplifyStderrLine(line);
        }

        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
                return SimplifyStderrLine(lines[i]);
        }

        return "";
    }

    private static string SimplifyStderrLine(string line)
    {
        var simplified = line.Trim();
        var bracketIndex = simplified.IndexOf("] ", StringComparison.Ordinal);
        if (bracketIndex >= 0 && bracketIndex + 2 < simplified.Length)
            simplified = simplified.Substring(bracketIndex + 2).Trim();

        return simplified;
    }

    private static string SanitizeStderrLine(string line)
    {
        var sanitized = AnsiEscapeRegex.Replace(line, "");
        return sanitized.Replace("\0", "").Trim();
    }

    private static string BuildFailureMessage(bool duringStartup, string stderrSummary, uint? exitCode, string fallbackMessage)
    {
        var prefix = duringStartup ? "sing-box start failed" : "sing-box process exited unexpectedly";
        var detail = string.IsNullOrWhiteSpace(stderrSummary) ? fallbackMessage : stderrSummary;

        return exitCode.HasValue
            ? $"{prefix}: {detail} (exit code {exitCode.Value})."
            : $"{prefix}: {detail}";
    }

    private static uint? TryGetExitCode(IntPtr processHandle)
    {
        if (processHandle == IntPtr.Zero)
            return null;

        return NativeMethods.GetExitCodeProcess(processHandle, out var exitCode)
            ? exitCode
            : null;
    }

    private static void SafeCloseHandle(ref IntPtr handle)
    {
        if (handle != IntPtr.Zero && handle != NativeMethods.INVALID_HANDLE_VALUE)
            NativeMethods.CloseHandle(handle);
        handle = IntPtr.Zero;
    }

    private void SetStateAndNotify(CoreState state, string message = "")
    {
        _state = state;
        RaiseEvent(new CoreEvent(CoreEventKind.StateChanged, state, message));
    }

    private void RaiseEvent(CoreEvent evt)
    {
        var handler = OnEvent;
        if (handler is null) return;

        var ctx = SynchronizationContext.Current;
        if (ctx is not null)
            ctx.Post(_ => handler(evt), null);
        else
            handler(evt);
    }

    private enum CommandKind
    {
        Start,
        Stop
    }

    private readonly struct SupervisorCommand
    {
        public CommandKind Kind { get; }
        public string ConfigJson { get; }

        public SupervisorCommand(CommandKind kind, string configJson)
        {
            Kind = kind;
            ConfigJson = configJson;
        }
    }

    private static class NativeMethods
    {
        public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
        public const uint CREATE_NEW_CONSOLE = 0x00000010;
        public const uint CREATE_SUSPENDED = 0x00000004;
        public const uint STARTF_USESHOWWINDOW = 0x00000001;
        public const uint STARTF_USESTDHANDLES = 0x00000100;
        public const ushort SW_HIDE = 0;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;
        public const uint HANDLE_FLAG_INHERIT = 0x00000001;
        public const uint WAIT_TIMEOUT = 0x00000102;
        public const uint CTRL_C_EVENT = 0;
        public static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

        public enum JobObjectInfoType
        {
            ExtendedLimitInformation = 9
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SECURITY_ATTRIBUTES
        {
            public uint nLength;
            public IntPtr lpSecurityDescriptor;

            [MarshalAs(UnmanagedType.Bool)]
            public bool bInheritHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
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
        public struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public uint cb;
            public string? lpReserved;
            public string? lpDesktop;
            public string? lpTitle;
            public uint dwX;
            public uint dwY;
            public uint dwXSize;
            public uint dwYSize;
            public uint dwXCountChars;
            public uint dwYCountChars;
            public uint dwFillAttribute;
            public uint dwFlags;
            public ushort wShowWindow;
            public ushort cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType infoType,
            ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint cbInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe,
            ref SECURITY_ATTRIBUTES lpPipeAttributes, uint nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            ref SECURITY_ATTRIBUTES lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcess(string? lpApplicationName, string lpCommandLine,
            IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
            uint dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite,
            out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate? handler, [MarshalAs(UnmanagedType.Bool)] bool add);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

        public delegate bool ConsoleCtrlDelegate(uint ctrlType);
    }
}
