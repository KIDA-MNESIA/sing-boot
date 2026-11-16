using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SingBoot.Tests;

[TestClass]
public sealed class RemoteConfigRetryTests
{
    private static readonly Uri ConfigUri = new("https://example.test/private/config?token=secret");

    [TestMethod]
    public async Task RetriesConnectionResetUntilDownloadSucceeds()
    {
        var attempts = 0;
        var logs = new List<string>();
        var downloaded = await RemoteConfigUpdater.FetchWithRetryAsync(
            ConfigUri,
            (_, _) => ++attempts < 3
                ? Task.FromException<string>(new HttpRequestException("Send failed",
                    new IOException("Connection reset", new SocketException((int)SocketError.ConnectionReset))))
                : Task.FromResult("{\"inbounds\":[]}"),
            CancellationToken.None,
            totalTimeout: TimeSpan.FromSeconds(5),
            retryDelay: TimeSpan.FromMilliseconds(5),
            log: logs.Add);

        Assert.AreEqual(3, attempts);
        Assert.AreEqual("{\"inbounds\":[]}", downloaded.RawContent);
        Assert.AreEqual(3, logs.Count);
        StringAssert.Contains(logs[0], "socket=ConnectionReset");
        StringAssert.Contains(logs[2], "attempts=3");
        Assert.IsFalse(string.Join("\n", logs).Contains("token=secret"));
    }

    [TestMethod]
    [DataRow(408)]
    [DataRow(429)]
    [DataRow(500)]
    [DataRow(502)]
    [DataRow(503)]
    [DataRow(504)]
    public async Task RetriesTemporaryHttpStatus(int status)
    {
        using var handler = new StatusThenSuccessHandler((HttpStatusCode)status);
        var downloaded = await FetchUsingHandler(handler);

        Assert.AreEqual(2, handler.Attempts);
        Assert.AreEqual("{}", downloaded.RawContent);
    }

    [TestMethod]
    [DataRow(400)]
    [DataRow(401)]
    [DataRow(403)]
    [DataRow(404)]
    [DataRow(501)]
    public async Task DoesNotRetryPermanentHttpStatus(int status)
    {
        using var handler = new StatusThenSuccessHandler((HttpStatusCode)status);
        var error = await Assert.ThrowsExactlyAsync<RemoteConfigHttpException>(() => FetchUsingHandler(handler));

        Assert.AreEqual(1, handler.Attempts);
        Assert.AreEqual((HttpStatusCode)status, error.StatusCode);
    }

    [TestMethod]
    public async Task DoesNotRetryInvalidConfiguration()
    {
        var attempts = 0;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            RemoteConfigUpdater.FetchWithRetryAsync(ConfigUri, (_, _) =>
            {
                attempts++;
                return Task.FromResult("<html>Not a configuration</html>");
            }, CancellationToken.None));

        Assert.AreEqual(1, attempts);
    }

    [TestMethod]
    public async Task DoesNotRetryCertificateFailure()
    {
        var attempts = 0;
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            RemoteConfigUpdater.FetchWithRetryAsync(ConfigUri, (_, _) =>
            {
                attempts++;
                return Task.FromException<string>(new HttpRequestException("TLS failed",
                    new WebException("Untrusted certificate", WebExceptionStatus.TrustFailure)));
            }, CancellationToken.None));

        Assert.AreEqual(1, attempts);
    }

    [TestMethod]
    public async Task TotalDeadlineCancelsAnInFlightRequest()
    {
        var attempts = 0;
        var requestCancelled = false;
        var elapsed = Stopwatch.StartNew();
        await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
            RemoteConfigUpdater.FetchWithRetryAsync(ConfigUri, async (_, token) =>
            {
                attempts++;
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                    return "{}";
                }
                finally
                {
                    requestCancelled = token.IsCancellationRequested;
                }
            }, CancellationToken.None, totalTimeout: TimeSpan.FromMilliseconds(100)));

        Assert.AreEqual(1, attempts);
        Assert.IsTrue(requestCancelled);
        Assert.IsLessThan(3000d, elapsed.Elapsed.TotalMilliseconds);
    }

    [TestMethod]
    public async Task TotalDeadlineIncludesRetryWaitAndPreservesLastFailure()
    {
        var attempts = 0;
        var failure = new HttpRequestException("Connection refused");
        var elapsed = Stopwatch.StartNew();
        var error = await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
            RemoteConfigUpdater.FetchWithRetryAsync(ConfigUri, (_, _) =>
            {
                attempts++;
                return Task.FromException<string>(failure);
            }, CancellationToken.None, totalTimeout: TimeSpan.FromMilliseconds(100), retryDelay: TimeSpan.FromSeconds(5)));

        Assert.AreEqual(1, attempts);
        Assert.AreSame(failure, error.InnerException);
        Assert.IsLessThan(3000d, elapsed.Elapsed.TotalMilliseconds);
    }

    [TestMethod]
    public async Task CallerCancellationStopsRetryWait()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            RemoteConfigUpdater.FetchWithRetryAsync(ConfigUri, (_, _) =>
            {
                attempts++;
                return Task.FromException<string>(new HttpRequestException("Network is starting"));
            }, cancellation.Token, log: _ => cancellation.Cancel()));

        Assert.AreEqual(1, attempts);
    }

    [TestMethod]
    public async Task CallerCancellationStopsAnInFlightRequestWithoutRetry()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            RemoteConfigUpdater.FetchWithRetryAsync(ConfigUri, async (_, token) =>
            {
                attempts++;
                cancellation.Cancel();
                await Task.Delay(Timeout.Infinite, token);
                return "{}";
            }, cancellation.Token));

        Assert.AreEqual(1, attempts);
    }

    [TestMethod]
    public async Task ManualFetchDoesNotRetry()
    {
        using var directory = new TestDirectory();
        var attempts = 0;
        using var app = new SingBootApp(LaunchMode.Normal, directory.Path, (_, _) =>
        {
            attempts++;
            return Task.FromException<string>(new HttpRequestException("Network is unavailable"));
        });

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            app.FetchRemoteConfigAsync(ConfigUri, CancellationToken.None));
        Assert.AreEqual(1, attempts);
    }

    private static Task<DownloadedCoreConfig> FetchUsingHandler(HttpMessageHandler handler)
    {
        return RemoteConfigUpdater.FetchWithRetryAsync(ConfigUri,
            (uri, token) => RemoteConfigUpdater.DownloadTextAsync(uri, handler, TimeSpan.FromSeconds(1), token),
            CancellationToken.None, totalTimeout: TimeSpan.FromSeconds(5), retryDelay: TimeSpan.FromMilliseconds(5));
    }

    private sealed class StatusThenSuccessHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _initialStatus;
        public int Attempts { get; private set; }

        public StatusThenSuccessHandler(HttpStatusCode initialStatus)
        {
            _initialStatus = initialStatus;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            return Task.FromResult(new HttpResponseMessage(Attempts == 1 ? _initialStatus : HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
        }
    }
}
