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
    /// Returns true if the auto-start registry entry exists and points to the current executable.
    /// </summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Enables or disables auto-start by writing/removing the registry value.
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return;

            if (enabled)
            {
                var processPath = Application.ExecutablePath;
                if (string.IsNullOrEmpty(processPath))
                    return;

                var command = $"\"{processPath}\" {AutoStartArgument}";
                key.SetValue(ValueName, command, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                SetResumeCoreOnAutoStart(false);
            }
        }
        catch
        {
            // Silently ignore registry errors
        }
    }

    public static bool ShouldResumeCoreOnAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppKeyPath, writable: false);
            var value = key?.GetValue(ResumeCoreValueName);
            return value switch
            {
                int intValue => intValue != 0,
                string stringValue => string.Equals(stringValue, "true", StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(stringValue, "1", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    public static void SetResumeCoreOnAutoStart(bool shouldResume)
    {
        try
        {
            if (shouldResume)
            {
                using var key = Registry.CurrentUser.CreateSubKey(AppKeyPath);
                if (key is null) return;

                key.SetValue(ResumeCoreValueName, 1, RegistryValueKind.DWord);
                return;
            }

            using var existingKey = Registry.CurrentUser.OpenSubKey(AppKeyPath, writable: true);
            existingKey?.DeleteValue(ResumeCoreValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Silently ignore registry errors
        }
    }
}
