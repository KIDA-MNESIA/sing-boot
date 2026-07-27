using System.Diagnostics;

namespace SingBoot;

/// <summary>
/// Main application controller. Manages core config, process supervisor lifecycle,
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
    private readonly CoreSupervisor _supervisor;
    private readonly string _baseDirectory;
    private CoreProfile? _profile;
    private CoreConfig? _config;
    private bool _disposed;

    public CoreProfile? Profile => _profile;
    public CoreConfig? Config => _config;
    public CoreState State => _supervisor.State;
    public bool IsRunning => _supervisor.State == CoreState.Running;
    public bool RequiresElevation => _config?.RequiresElevation == true;
    public string CoreDisplayName => _profile?.DisplayName ?? "core";
    public string TrayText => _profile is null ? "sing-boot" : $"sing-boot - {_profile.DisplayName}";

    /// <summary>
    /// Raised when the core state changes or an error occurs. UI subscribers must marshal
    /// the event to their UI thread.
    /// </summary>
    public event Action<CoreEvent>? OnCoreEvent;

    public SingBootApp(LaunchMode launchMode)
    {
        _baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        CoreDiscovery.TryDiscover(_baseDirectory, out _profile, out _);

        _supervisor = new CoreSupervisor();
        _supervisor.OnEvent += HandleCoreEvent;
    }

    /// <summary>
    /// Start the selected core process.
    /// </summary>
    public bool Start(out string message)
    {
        if (_profile is null || _config is null)
        {
            message = "Configuration has not been loaded.";
            return false;
        }

        if (_supervisor.State is not CoreState.Stopped and not CoreState.Failed)
        {
            message = $"{CoreDisplayName} is already starting, running, or stopping.";
            return false;
        }

        if (!_supervisor.RequestStart(_profile.CreateStartRequest(_config)))
        {
            message = "Unable to queue the start request because the application is shutting down or another operation is pending.";
            return false;
        }

        RememberCoreShouldRun("start-request-accepted");

        message = "";
        return true;
    }

    /// <summary>
    /// Stop the selected core process.
    /// </summary>
    public bool Stop(out string message)
    {
        if (_supervisor.State != CoreState.Running)
        {
            message = $"{CoreDisplayName} is not running.";
            return false;
        }

        if (!_supervisor.RequestStop())
        {
            message = "Unable to queue the stop request because the application is shutting down or another operation is pending.";
            return false;
        }

        UpdateResumeIntent(shouldResume: false, reason: "explicit-stop");
        message = "";
        return true;
    }

    public bool UpdateAutoStart(bool enabled, out string message)
    {
        if (!AutoStart.TrySetEnabled(enabled, out message))
            return false;

        if (enabled && !AutoStart.TrySetResumeCoreOnAutoStart(IsRunning, out var resumeMessage))
        {
            message = $"Auto-start was enabled, but its resume state could not be saved: {resumeMessage}";
            return false;
        }

        AppLog.Write($"auto-start setting changed: enabled={enabled}; resumeRequested={(enabled && IsRunning)}");

        message = "";
        return true;
    }

    public StartPreparationResult PrepareForStart(out string message)
    {
        if (!TryReloadConfig(out message))
            return StartPreparationResult.Blocked;

        if (HasConflictingCoreProcess(out message))
            return StartPreparationResult.Blocked;

        if (RequiresElevation && !PrivilegeHelper.IsAdministrator())
        {
            var elevation = PrivilegeHelper.TryRelaunchElevatedForStart(CoreDisplayName, out message);
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

    public static void PrepareForManualExit()
    {
        UpdateResumeIntent(shouldResume: false, reason: "manual-quit");
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
        if (evt.Kind == CoreEventKind.StateChanged && evt.State == CoreState.Running)
            RememberCoreShouldRun("core-running");

        OnCoreEvent?.Invoke(evt);
    }

    private bool TryReloadConfig(out string message)
    {
        if (!CoreDiscovery.TryDiscover(_baseDirectory, out var profile, out message))
        {
            _profile = null;
            _config = null;
            return false;
        }
        if (profile is null)
        {
            message = "Unable to find a supported core layout.";
            return false;
        }

        try
        {
            _profile = profile;
            _config = CoreConfig.Load(profile);
            message = "";
            return true;
        }
        catch (Exception ex)
        {
            var detail = string.IsNullOrWhiteSpace(ex.Message)
                ? "Unknown error while loading configuration."
                : ex.Message;

            message = $"Unable to load {profile.DisplayName} configuration: {detail}";
            return false;
        }
    }

    private bool HasConflictingCoreProcess(out string message)
    {
        if (_profile is null)
        {
            message = "";
            return false;
        }

        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_profile.ExecutablePath)))
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

                message = $"Another {_profile.DisplayName} process is already running. Stop it before starting from the tray.";
                return true;
            }
        }

        message = "";
        return false;
    }

    private static void RememberCoreShouldRun(string reason)
    {
        if (!AutoStart.TryGetEnabled(out var autoStartEnabled, out var message))
        {
            AppLog.Write($"resume intent unchanged: reason={reason}; auto-start read failed: {message}");
            return;
        }

        if (autoStartEnabled)
            UpdateResumeIntent(shouldResume: true, reason: reason);
    }

    private static void UpdateResumeIntent(bool shouldResume, string reason)
    {
        if (AutoStart.TrySetResumeCoreOnAutoStart(shouldResume, out var message))
        {
            AppLog.Write($"resume intent updated: value={shouldResume}; reason={reason}");
            return;
        }

        AppLog.Write($"resume intent update failed: value={shouldResume}; reason={reason}; error={message}");
    }
}
