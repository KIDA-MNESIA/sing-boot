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

        SingBootApp app;
        try
        {
            app = new SingBootApp(launchMode);
        }
        catch (Exception ex)
        {
            var msg = string.IsNullOrEmpty(ex.Message) ? "Unknown error." : ex.Message;
            MessageBox.Show(msg, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var startCoreAfterLaunch = launchMode == LaunchMode.HandoffStart ||
                                   (launchMode == LaunchMode.AutoStart && app.ShouldResumeCoreOnAutoStart());
        Application.Run(new MainForm(app, startCoreAfterLaunch));
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
}
