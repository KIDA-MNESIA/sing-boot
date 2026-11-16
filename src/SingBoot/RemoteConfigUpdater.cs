using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;

namespace SingBoot;

internal delegate Task<string> RemoteTextDownloader(Uri uri, CancellationToken cancellationToken);

internal sealed class RemoteConfigHttpException : HttpRequestException
{
    public HttpStatusCode StatusCode { get; }

    public RemoteConfigHttpException(HttpStatusCode statusCode)
        : base($"Remote server returned HTTP {(int)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
    }
}

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
    private static readonly TimeSpan AutomaticUpdateTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan AutomaticRetryDelay = TimeSpan.FromSeconds(3);

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

    internal static async Task<DownloadedCoreConfig> FetchWithRetryAsync(
        Uri configUrl,
        RemoteTextDownloader? downloader,
        CancellationToken cancellationToken,
        TimeSpan? totalTimeout = null,
        TimeSpan? retryDelay = null,
        Action<string>? log = null)
    {
        var timeout = totalTimeout ?? AutomaticUpdateTimeout;
        var delay = retryDelay ?? AutomaticRetryDelay;
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(totalTimeout));
        if (delay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retryDelay));

        using var deadline = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var elapsed = Stopwatch.StartNew();
        var source = SingBootSettings.DescribeUriForLog(configUrl);
        var attempt = 0;
        Exception? lastFailure = null;

        try
        {
            while (true)
            {
                linked.Token.ThrowIfCancellationRequested();
                attempt++;
                try
                {
                    var downloaded = await FetchAsync(configUrl, downloader, linked.Token).ConfigureAwait(false);
                    linked.Token.ThrowIfCancellationRequested();
                    log?.Invoke($"remote config download succeeded: source={source}; attempts={attempt}; elapsedMs={elapsed.ElapsedMilliseconds}");
                    return downloaded;
                }
                catch (Exception ex) when (IsTransientDownloadFailure(ex) && !linked.IsCancellationRequested)
                {
                    lastFailure = ex;
                    log?.Invoke(
                        $"remote config download retry: source={source}; attempt={attempt}; " +
                        $"elapsedMs={elapsed.ElapsedMilliseconds}; retryDelayMs={delay.TotalMilliseconds}; " +
                        $"error={AppLog.DescribeException(ex)}");
                }

                // The same deadline covers requests and waits, including a slow final request.
                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (linked.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"Automatic remote configuration update timed out after {timeout.TotalSeconds:0.###}s ({attempt} attempts).",
                lastFailure ?? ex);
        }
    }

    private static bool IsTransientDownloadFailure(Exception exception)
    {
        if (exception is RemoteConfigHttpException httpError)
        {
            return (int)httpError.StatusCode is 408 or 429 or 500 or 502 or 503 or 504;
        }

        if (exception is TimeoutException)
            return true;

        if (exception is not HttpRequestException && exception is not IOException && exception is not WebException)
            return false;

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException)
                return false;

            if (current is WebException webError)
            {
                return webError.Status is WebExceptionStatus.ConnectFailure or
                    WebExceptionStatus.ConnectionClosed or WebExceptionStatus.KeepAliveFailure or
                    WebExceptionStatus.ReceiveFailure or WebExceptionStatus.SendFailure or
                    WebExceptionStatus.NameResolutionFailure or WebExceptionStatus.ProxyNameResolutionFailure or
                    WebExceptionStatus.Timeout;
            }
        }

        return true;
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
                throw new RemoteConfigHttpException(response.StatusCode);
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
