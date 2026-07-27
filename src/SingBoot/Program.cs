namespace SingBoot;

internal enum LaunchMode
{
    Normal,
    AutoStart,
    HandoffStart
}

internal static class Program
{
    private const string AppTitle = "sing-boot";
    private const string MutexName = "SingBoot_SingleInstance_Mutex";
    private static readonly TimeSpan HandoffAcquireTimeout = TimeSpan.FromSeconds(15);

    [STAThread]
    static void Main(string[] args)
    {
        EmbeddedAssemblyResolver.Initialize();
        var launchMode = ParseLaunchMode(args);

        using var singleInstance = new SingleInstance();
        if (!AcquireSingleInstance(singleInstance, launchMode))
        {
            var message = launchMode == LaunchMode.HandoffStart
                ? "Unable to complete the elevated start handoff."
                : "Another instance of this application is already running.";
            var icon = launchMode == LaunchMode.HandoffStart
                ? MessageBoxIcon.Error
                : MessageBoxIcon.Warning;
            MessageBox.Show(message, AppTitle, MessageBoxButtons.OK, icon);
            return;
        }

        var autoStartReadSucceeded = AutoStart.TryGetEnabled(out var autoStartEnabled, out var autoStartReadMessage);
        var resumeReadSucceeded = AutoStart.TryGetResumeCoreOnAutoStart(
            out var resumeRequested,
            out var resumeReadMessage);
        var startupDecision = StartupDecision.Create(launchMode, autoStartEnabled, resumeRequested);
        AppLog.Write(
            $"startup decision: mode={startupDecision.LaunchMode}; " +
            $"autoStartRead={DescribeRead(autoStartReadSucceeded, autoStartReadMessage)}; " +
            $"autoStartEnabled={startupDecision.AutoStartEnabled}; " +
            $"resumeRead={DescribeRead(resumeReadSucceeded, resumeReadMessage)}; " +
            $"resumeRequested={startupDecision.ResumeRequested}; " +
            $"action={startupDecision.Action}");

        SingBootApp app;
        try
        {
            app = new SingBootApp(launchMode);
        }
        catch (Exception ex)
        {
            var msg = string.IsNullOrEmpty(ex.Message) ? "Unknown error." : ex.Message;
            AppLog.Write($"startup failed: {msg}");
            MessageBox.Show(msg, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(
            app,
            startupDecision.StartCoreAfterLaunch,
            startupDecision.WaitForDefaultGatewayBeforeStart));
    }

    private static LaunchMode ParseLaunchMode(string[] args)
    {
        foreach (var arg in args)
        {
            if (string.Equals(arg, "--handoff-start", StringComparison.OrdinalIgnoreCase))
                return LaunchMode.HandoffStart;
        }

        foreach (var arg in args)
        {
            if (string.Equals(arg, "--auto-start", StringComparison.OrdinalIgnoreCase))
                return LaunchMode.AutoStart;
        }

        return LaunchMode.Normal;
    }

    private static bool AcquireSingleInstance(SingleInstance singleInstance, LaunchMode launchMode)
    {
        if (launchMode != LaunchMode.HandoffStart)
            return singleInstance.Acquire(MutexName);

        var deadline = DateTime.UtcNow + HandoffAcquireTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (singleInstance.Acquire(MutexName))
                return true;

            Thread.Sleep(250);
        }

        return false;
    }

    private static string DescribeRead(bool succeeded, string message)
    {
        return succeeded ? "ok" : $"error({message})";
    }
}
