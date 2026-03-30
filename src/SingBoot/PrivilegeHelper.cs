using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Forms;

namespace SingBoot;

internal enum ElevationRequestResult
{
    Started,
    Cancelled,
    Failed
}

internal static class PrivilegeHelper
{
    private const int ErrorCancelled = 1223;

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static ElevationRequestResult TryRelaunchElevatedForStart(out string message)
    {
        var exePath = Application.ExecutablePath;
        if (string.IsNullOrEmpty(exePath))
        {
            message = "Unable to locate the current executable for elevation.";
            return ElevationRequestResult.Failed;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--handoff-start",
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(psi);
            message = "";
            return ElevationRequestResult.Started;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            message = "Starting this configuration requires administrator permission because it uses a TUN inbound.";
            return ElevationRequestResult.Cancelled;
        }
        catch (Exception ex)
        {
            message = $"Unable to request administrator permission: {ex.Message}";
            return ElevationRequestResult.Failed;
        }
    }
}
