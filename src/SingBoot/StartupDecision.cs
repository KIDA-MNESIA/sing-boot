namespace SingBoot;

internal sealed class StartupDecision
{
    private StartupDecision(
        LaunchMode launchMode,
        bool autoStartEnabled,
        bool resumeRequested,
        bool startCoreAfterLaunch,
        bool waitForDefaultGatewayBeforeStart)
    {
        LaunchMode = launchMode;
        AutoStartEnabled = autoStartEnabled;
        ResumeRequested = resumeRequested;
        StartCoreAfterLaunch = startCoreAfterLaunch;
        WaitForDefaultGatewayBeforeStart = waitForDefaultGatewayBeforeStart;
    }

    public LaunchMode LaunchMode { get; }
    public bool AutoStartEnabled { get; }
    public bool ResumeRequested { get; }
    public bool StartCoreAfterLaunch { get; }
    public bool WaitForDefaultGatewayBeforeStart { get; }

    public string Action => WaitForDefaultGatewayBeforeStart
        ? "wait-for-default-gateway-then-start"
        : StartCoreAfterLaunch
            ? "start-after-launch"
            : "tray-only";

    public static StartupDecision Create(
        LaunchMode launchMode,
        bool autoStartEnabled,
        bool resumeRequested)
    {
        var resumeFromAutoStart = launchMode == LaunchMode.AutoStart &&
                                  autoStartEnabled &&
                                  resumeRequested;
        var startCoreAfterLaunch = launchMode == LaunchMode.HandoffStart || resumeFromAutoStart;

        return new StartupDecision(
            launchMode,
            autoStartEnabled,
            resumeRequested,
            startCoreAfterLaunch,
            waitForDefaultGatewayBeforeStart: resumeFromAutoStart);
    }
}
