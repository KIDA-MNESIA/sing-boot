using Microsoft.Win32;
using System.Windows.Forms;

namespace SingBoot;

/// <summary>
/// Manages auto-start on Windows logon via the HKCU Run registry key.
/// </summary>
public static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppKeyPath = @"Software\SingBoot";
    private const string ValueName = "SingBoot";
    private const string ResumeCoreValueName = "ResumeCoreOnAutoStart";
    private const string AutoStartArgument = "--auto-start";

    /// <summary>
    /// Returns true only when the auto-start registry entry points to this executable.
    /// </summary>
    public static bool IsEnabled()
    {
        return TryGetEnabled(out var enabled, out _) && enabled;
    }

    internal static bool TryGetEnabled(out bool enabled, out string message)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(ValueName) as string;
            enabled = IsCurrentExecutableCommand(value, Application.ExecutablePath);
            message = "";
            return true;
        }
        catch (Exception ex)
        {
            enabled = false;
            message = ex.Message;
            return false;
        }
    }

    public static bool TrySetEnabled(bool enabled, out string message)
    {
        try
        {
            if (enabled)
            {
                var processPath = Application.ExecutablePath;
                if (string.IsNullOrWhiteSpace(processPath))
                {
                    message = "Unable to locate the current executable for auto-start.";
                    return false;
                }

                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                if (key is null)
                {
                    message = "Unable to open the Windows auto-start registry key.";
                    return false;
                }

                key.SetValue(ValueName, BuildCommand(processPath), RegistryValueKind.String);
                message = "";
                return true;
            }

            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
                key?.DeleteValue(ValueName, throwOnMissingValue: false);

            if (!TrySetResumeCoreOnAutoStart(false, out var resumeMessage))
            {
                message = $"Auto-start was disabled, but its resume state could not be cleared: {resumeMessage}";
                return false;
            }

            message = "";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Unable to update Windows auto-start: {ex.Message}";
            return false;
        }
    }

    public static bool ShouldResumeCoreOnAutoStart()
    {
        return TryGetResumeCoreOnAutoStart(out var shouldResume, out _) && shouldResume;
    }

    internal static bool TryGetResumeCoreOnAutoStart(out bool shouldResume, out string message)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppKeyPath, writable: false);
            var value = key?.GetValue(ResumeCoreValueName);
            shouldResume = value switch
            {
                int intValue => intValue != 0,
                string stringValue => string.Equals(stringValue, "true", StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(stringValue, "1", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
            message = "";
            return true;
        }
        catch (Exception ex)
        {
            shouldResume = false;
            message = ex.Message;
            return false;
        }
    }

    public static bool TrySetResumeCoreOnAutoStart(bool shouldResume, out string message)
    {
        try
        {
            if (shouldResume)
            {
                using var key = Registry.CurrentUser.CreateSubKey(AppKeyPath);
                if (key is null)
                {
                    message = "Unable to open the sing-boot state registry key.";
                    return false;
                }

                key.SetValue(ResumeCoreValueName, 1, RegistryValueKind.DWord);
                message = "";
                return true;
            }

            using var existingKey = Registry.CurrentUser.OpenSubKey(AppKeyPath, writable: true);
            existingKey?.DeleteValue(ResumeCoreValueName, throwOnMissingValue: false);
            message = "";
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    public static void SetResumeCoreOnAutoStart(bool shouldResume)
    {
        TrySetResumeCoreOnAutoStart(shouldResume, out _);
    }

    internal static bool IsCurrentExecutableCommand(string? command, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(executablePath))
            return false;

        return string.Equals(command!.Trim(), BuildCommand(executablePath), StringComparison.OrdinalIgnoreCase);
    }

    internal static string BuildCommand(string executablePath)
    {
        return $"\"{Path.GetFullPath(executablePath)}\" {AutoStartArgument}";
    }
}
