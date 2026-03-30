using System.Diagnostics;

namespace SingBoot;

/// <summary>
/// Main application controller. Manages sing-box config, process supervisor lifecycle,
/// and exposes Start/Stop/Shutdown operations for the UI.
/// </summary>
internal enum StartPreparationResult
{
    Ready,
    RelaunchStarted,
    Blocked
}

internal sealed class SingBootApp : IDisposable
{
    private const string CoreExecutableName = "sing-box.exe";
    private const string ConfigFileName = "config.json";

    private readonly CoreSupervisor _supervisor;
    private readonly string _corePath;
    private readonly string _configPath;
    private SingBoxConfig? _config;
    private bool _disposed;

    public SingBoxConfig? Config => _config;
    public CoreState State => _supervisor.State;
    public bool IsRunning => _supervisor.State == CoreState.Running;
    public bool RequiresElevation => _config?.RequiresElevation == true;

    /// <summary>
    /// Raised on the UI thread when the core state changes or an error occurs.
    /// </summary>
    public event Action<CoreEvent>? OnCoreEvent;

    public SingBootApp(LaunchMode launchMode)
    {
        _corePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, CoreExecutableName));
        _configPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ConfigFileName));

        if (!File.Exists(_corePath))
            throw new FileNotFoundException("sing-box executable not found.", _corePath);

        _supervisor = new CoreSupervisor(_corePath);
        _supervisor.OnEvent += HandleCoreEvent;
    }

    /// <summary>
    /// Start the sing-box process.
    /// </summary>
    public void Start()
    {
        if (_config is null)
            throw new InvalidOperationException("Configuration has not been loaded.");

        _supervisor.RequestStart(_config.JsonContent);
    }

    /// <summary>
    /// Stop the sing-box process.
    /// </summary>
    public void Stop()
    {
        AutoStart.SetResumeCoreOnAutoStart(false);
        _supervisor.RequestStop();
    }

    public bool ShouldResumeCoreOnAutoStart()
    {
        return AutoStart.IsEnabled() && AutoStart.ShouldResumeCoreOnAutoStart();
    }

    public void UpdateAutoStart(bool enabled)
    {
        AutoStart.SetEnabled(enabled);
        AutoStart.SetResumeCoreOnAutoStart(enabled && IsRunning);
    }

    public StartPreparationResult PrepareForStart(out string message)
    {
        if (!TryReloadConfig(out message))
            return StartPreparationResult.Blocked;

        if (HasConflictingCoreProcess(out message))
            return StartPreparationResult.Blocked;

        if (RequiresElevation && !PrivilegeHelper.IsAdministrator())
        {
            var elevation = PrivilegeHelper.TryRelaunchElevatedForStart(out message);
            switch (elevation)
            {
                case ElevationRequestResult.Started:
                    return StartPreparationResult.RelaunchStarted;

                case ElevationRequestResult.Cancelled:
                    return StartPreparationResult.Blocked;

                default:
                    return StartPreparationResult.Blocked;
            }
        }

        message = "";
        return StartPreparationResult.Ready;
    }

    /// <summary>
    /// Gracefully shut down the supervisor and all resources.
    /// </summary>
    public void Shutdown()
    {
        _supervisor.OnEvent -= HandleCoreEvent;
        _supervisor.Shutdown();
    }

    public void PrepareForManualExit()
    {
        AutoStart.SetResumeCoreOnAutoStart(false);
    }

    public void PrepareForSystemExit()
    {
        AutoStart.SetResumeCoreOnAutoStart(AutoStart.IsEnabled() && IsRunning);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _supervisor.OnEvent -= HandleCoreEvent;
        _supervisor.Dispose();
    }

    private void HandleCoreEvent(CoreEvent evt)
    {
        if (evt.Kind == CoreEventKind.StateChanged &&
            evt.State == CoreState.Running &&
            AutoStart.IsEnabled())
        {
            AutoStart.SetResumeCoreOnAutoStart(true);
        }

        OnCoreEvent?.Invoke(evt);
    }

    private bool TryReloadConfig(out string message)
    {
        try
        {
            _config = SingBoxConfig.Load(_configPath);
            message = "";
            return true;
        }
        catch (Exception ex)
        {
            var detail = string.IsNullOrWhiteSpace(ex.Message)
                ? "Unknown error while loading configuration."
                : ex.Message;

            message = $"Unable to load configuration: {detail}";
            return false;
        }
    }

    private bool HasConflictingCoreProcess(out string message)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_corePath)))
        {
            using (process)
            {
                try
                {
                    if (process.HasExited)
                        continue;
                }
                catch
                {
                    continue;
                }

                if (_supervisor.ProcessId != 0 && process.Id == (int)_supervisor.ProcessId)
                    continue;

                message = "Another sing-box process is already running. Stop it before starting from the tray.";
                return true;
            }
        }

        message = "";
        return false;
    }
}
