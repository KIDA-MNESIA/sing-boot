using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace SingBoot;

internal delegate Task<string> RemoteTextDownloader(Uri uri, CancellationToken cancellationToken);

internal sealed class DownloadedCoreConfig
{
    public string RawContent { get; }
    public CoreConfig Config { get; }

    public DownloadedCoreConfig(string rawContent, CoreConfig config)
    {
        RawContent = rawContent;
        Config = config;
    }
}

/// <summary>
/// Downloads, validates, backs up, and atomically replaces sing-box's config.json.
/// </summary>
internal static class RemoteConfigUpdater
{
    public const string BackupExtension = ".bak";
    private const long MaxConfigResponseBytes = 64L * 1024 * 1024;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public static string GetBackupPath(string configPath)
    {
        return Path.GetFullPath(configPath) + BackupExtension;
    }

    public static async Task<DownloadedCoreConfig> FetchAsync(
        Uri configUrl,
        RemoteTextDownloader? downloader,
        CancellationToken cancellationToken)
    {
        var effectiveDownloader = downloader ?? DefaultDownloadTextAsync;
        var rawText = await effectiveDownloader(configUrl, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var sanitized = CoreConfig.StripUtf8Bom(rawText);
        var parsedConfig = CoreConfig.Parse(sanitized);
        return new DownloadedCoreConfig(sanitized, parsedConfig);
    }

    public static void BackupAndReplaceConfig(string configPath, string newRawContent)
    {
        var sanitized = CoreConfig.StripUtf8Bom(newRawContent);
        _ = CoreConfig.Parse(sanitized);

        var fullConfigPath = Path.GetFullPath(configPath);
        var directory = Path.GetDirectoryName(fullConfigPath)
            ?? throw new InvalidOperationException("Unable to determine configuration directory.");

        Directory.CreateDirectory(directory);

        var backupPath = GetBackupPath(fullConfigPath);
        var tempPath = Path.Combine(directory, $".config.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(
                tempPath,
                sanitized,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (File.Exists(fullConfigPath))
            {
                ClearReadOnlyAttributeIfPresent(backupPath);
                ClearReadOnlyAttributeIfPresent(fullConfigPath);

                try
                {
                    File.Replace(
                        sourceFileName: tempPath,
                        destinationFileName: fullConfigPath,
                        destinationBackupFileName: backupPath,
                        ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(fullConfigPath, backupPath, overwrite: true);
                    File.Copy(tempPath, fullConfigPath, overwrite: true);
                }
            }
            else
            {
                File.Move(tempPath, fullConfigPath);
            }
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    internal static Task<string> DefaultDownloadTextAsync(Uri uri, CancellationToken cancellationToken)
    {
        return DownloadTextAsync(uri, customHandler: null, DefaultTimeout, cancellationToken);
    }

    internal static async Task<string> DownloadTextAsync(
        Uri uri,
        HttpMessageHandler? customHandler,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        EnsureModernTls();

        var handler = customHandler ?? CreateDefaultHttpHandler();
        var disposeHandler = customHandler is null;

        using var client = new HttpClient(handler, disposeHandler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = MaxConfigResponseBytes
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("sing-boot", "1.0"));

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            using var response = await client
                .GetAsync(uri, HttpCompletionOption.ResponseContentRead, linkedCts.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
                var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase)
                    ? response.StatusCode.ToString()
                    : response.ReasonPhrase;
                throw new InvalidOperationException($"Remote server returned HTTP {statusCode} ({reason}).");
            }

            using var registration = linkedCts.Token.Register(response.Dispose);
            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is OperationCanceledException ||
            (ex is ObjectDisposedException && linkedCts.IsCancellationRequested) ||
            (ex is HttpRequestException && linkedCts.IsCancellationRequested))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var seconds = Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));
            throw new TimeoutException(
                string.Format(CultureInfo.InvariantCulture, "Remote configuration request timed out after {0}s.", seconds),
                ex);
        }
    }

    private static HttpClientHandler CreateDefaultHttpHandler()
    {
        return new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            UseProxy = true,
            Proxy = WebRequest.GetSystemWebProxy()
        };
    }

#pragma warning disable CA5386 // Ensure TLS 1.2/1.3 are enabled on .NET Framework 4.8 without app.config
    private static void EnsureModernTls()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        try
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288;
        }
        catch (NotSupportedException)
        {
        }
    }
#pragma warning restore CA5386

    private static void ClearReadOnlyAttributeIfPresent(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
