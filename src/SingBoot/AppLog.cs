using System.Globalization;
using System.Text;

namespace SingBoot;

internal static class AppLog
{
    private static readonly object SyncRoot = new();

    public static void Write(string message)
    {
        TryWrite(AppContext.BaseDirectory, DateTimeOffset.Now, message);
    }

    internal static bool TryWrite(string baseDirectory, DateTimeOffset timestamp, string message)
    {
        try
        {
            var logPath = GetLogPath(baseDirectory, timestamp);
            var logsDirectory = Path.GetDirectoryName(logPath);
            if (string.IsNullOrEmpty(logsDirectory))
                return false;

            var sanitizedMessage = message.Replace('\r', ' ').Replace('\n', ' ');

            lock (SyncRoot)
            {
                Directory.CreateDirectory(logsDirectory);
                using var stream = new FileStream(
                    logPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(timestamp.ToString("O", CultureInfo.InvariantCulture));
                writer.Write(' ');
                writer.WriteLine(sanitizedMessage);
            }

            return true;
        }
        catch
        {
            // Diagnostics must never prevent the tray application from starting or shutting down.
            return false;
        }
    }

    internal static string GetLogPath(string baseDirectory, DateTimeOffset timestamp)
    {
        var fileName = $"sing-boot-{timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log";
        return Path.Combine(Path.GetFullPath(baseDirectory), "logs", fileName);
    }
}
