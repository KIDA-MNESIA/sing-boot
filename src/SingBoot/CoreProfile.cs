namespace SingBoot;

public enum CoreKind
{
    SingBox,
    Mihomo
}

public sealed class CoreProfile
{
    public CoreKind Kind { get; }
    public string DisplayName { get; }
    public string ExecutablePath { get; }
    public string ConfigPath { get; }
    public string WorkingDirectory { get; }
    public string DirectoryArgument { get; }

    public CoreProfile(CoreKind kind, string displayName, string executablePath, string configPath, string workingDirectory)
    {
        Kind = kind;
        DisplayName = displayName;
        ExecutablePath = Path.GetFullPath(executablePath);
        ConfigPath = Path.GetFullPath(configPath);
        WorkingDirectory = Path.GetFullPath(workingDirectory);
        DirectoryArgument = NormalizeDirectoryArgument(WorkingDirectory);
    }

    public CoreStartRequest CreateStartRequest(CoreConfig config)
    {
        return Kind switch
        {
            CoreKind.SingBox => new CoreStartRequest(
                DisplayName,
                ExecutablePath,
                $"{Quote(ExecutablePath)} run -c stdin",
                WorkingDirectory,
                config.StandardInputContent),
            CoreKind.Mihomo => new CoreStartRequest(
                DisplayName,
                ExecutablePath,
                $"{Quote(ExecutablePath)} -d {Quote(DirectoryArgument)} -f {Quote(ConfigPath)}",
                WorkingDirectory,
                null),
            _ => throw new InvalidOperationException("Unsupported core kind.")
        };
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

    private static string NormalizeDirectoryArgument(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            return fullPath;

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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

    private static readonly string[] MihomoExecutableNames =
    {
        "mihomo-windows-amd64.exe",
        "mihomo.exe"
    };

    private static readonly string[] MihomoConfigNames =
    {
        "config.yaml",
        "config.yml"
    };

    public static bool TryDiscover(string baseDirectory, out CoreProfile? profile, out string message)
    {
        var directory = Path.GetFullPath(baseDirectory);

        var mihomoExe = FindExistingFile(directory, MihomoExecutableNames);
        var mihomoConfig = FindExistingFile(directory, MihomoConfigNames);
        if (mihomoExe is not null && mihomoConfig is not null)
        {
            profile = new CoreProfile(CoreKind.Mihomo, "mihomo", mihomoExe, mihomoConfig, directory);
            message = "";
            return true;
        }

        var singBoxExe = Path.Combine(directory, SingBoxExecutableName);
        var singBoxConfig = Path.Combine(directory, SingBoxConfigName);
        if (File.Exists(singBoxExe) && File.Exists(singBoxConfig))
        {
            profile = new CoreProfile(CoreKind.SingBox, "sing-box", singBoxExe, singBoxConfig, directory);
            message = "";
            return true;
        }

        profile = null;
        message = "Unable to find a supported core layout. Place one of these file sets next to sing-boot.exe:\n" +
                  "- mihomo: mihomo-windows-amd64.exe (or mihomo.exe) + config.yaml (or config.yml)\n" +
                  "- sing-box: sing-box.exe + config.json";
        return false;
    }

    private static string? FindExistingFile(string directory, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
                return path;
        }

        return null;
    }
}
