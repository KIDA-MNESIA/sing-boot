namespace SingBoot;

public sealed class CoreProfile
{
    public string DisplayName { get; }
    public string ExecutablePath { get; }
    public string ConfigPath { get; }
    public string WorkingDirectory { get; }

    public CoreProfile(string displayName, string executablePath, string configPath, string workingDirectory)
    {
        DisplayName = displayName;
        ExecutablePath = Path.GetFullPath(executablePath);
        ConfigPath = Path.GetFullPath(configPath);
        WorkingDirectory = Path.GetFullPath(workingDirectory);
    }

    public CoreStartRequest CreateStartRequest(CoreConfig config)
    {
        return new CoreStartRequest(
            DisplayName,
            ExecutablePath,
            $"{Quote(ExecutablePath)} run -c stdin",
            WorkingDirectory,
            config.StandardInputContent);
    }

    private static string Quote(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length + 2);
        builder.Append('"');

        var backslashCount = 0;
        foreach (var ch in value)
        {
            if (ch == '\\')
            {
                backslashCount++;
                continue;
            }

            if (ch == '"')
            {
                builder.Append('\\', backslashCount * 2 + 1);
                builder.Append('"');
                backslashCount = 0;
                continue;
            }

            builder.Append('\\', backslashCount);
            backslashCount = 0;
            builder.Append(ch);
        }

        builder.Append('\\', backslashCount * 2);
        builder.Append('"');
        return builder.ToString();
    }
}

public sealed class CoreStartRequest
{
    public string DisplayName { get; }
    public string ExecutablePath { get; }
    public string CommandLine { get; }
    public string WorkingDirectory { get; }
    public string? StandardInputContent { get; }

    public CoreStartRequest(
        string displayName,
        string executablePath,
        string commandLine,
        string workingDirectory,
        string? standardInputContent)
    {
        DisplayName = displayName;
        ExecutablePath = executablePath;
        CommandLine = commandLine;
        WorkingDirectory = workingDirectory;
        StandardInputContent = standardInputContent;
    }
}

internal static class CoreDiscovery
{
    private const string SingBoxExecutableName = "sing-box.exe";
    private const string SingBoxConfigName = "config.json";

    public static string GetConfigPath(string baseDirectory)
    {
        return Path.Combine(Path.GetFullPath(baseDirectory), SingBoxConfigName);
    }

    public static bool TryDiscover(string baseDirectory, out CoreProfile? profile, out string message)
    {
        var directory = Path.GetFullPath(baseDirectory);
        var singBoxExe = Path.Combine(directory, SingBoxExecutableName);
        var singBoxConfig = Path.Combine(directory, SingBoxConfigName);
        if (File.Exists(singBoxExe) && File.Exists(singBoxConfig))
        {
            profile = new CoreProfile("sing-box", singBoxExe, singBoxConfig, directory);
            message = "";
            return true;
        }

        profile = null;
        message = "Unable to find the sing-box core layout. Place sing-box.exe and config.json next to sing-boot.exe.";
        return false;
    }
}
