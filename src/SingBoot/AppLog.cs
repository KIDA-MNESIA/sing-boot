using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;

namespace SingBoot;

internal static class AppLog
{
    private static readonly object SyncRoot = new();
    private static readonly Regex UrlPattern = new(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase);

    public static void Write(string message)
    {
        TryWrite(AppContext.BaseDirectory, DateTimeOffset.Now, message);
    }

    internal static string DescribeException(Exception exception)
    {
        var details = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var detail = $"{current.GetType().Name} (0x{current.HResult:X8})";
            if (current is WebException webError)
                detail += $" status={webError.Status}";
            if (current is SocketException socketError)
                detail += $" socket={socketError.SocketErrorCode}; native={socketError.NativeErrorCode}";

            // Parser exceptions can echo downloaded configuration. Keep their types, not their contents.
            if (ReferenceEquals(current, exception) ||
                current is HttpRequestException or WebException or IOException or TimeoutException or AuthenticationException)
            {
                detail += $": {RedactUrls(current.Message)}";
            }
            details.Add(detail);
        }
        return string.Join(" --> ", details);
    }

    internal static string RedactUrls(string message)
    {
        return UrlPattern.Replace(message, match =>
            Uri.TryCreate(match.Value, UriKind.Absolute, out var uri)
                ? SingBootSettings.DescribeUriForLog(uri) + "/<redacted>"
                : "<redacted-url>");
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
