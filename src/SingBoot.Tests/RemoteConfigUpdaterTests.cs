using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SingBoot.Tests;

[TestClass]
public sealed class RemoteConfigUpdaterTests
{
    [TestMethod]
    public async Task FetchAsync_DownloadsValidatesJsoncAndStripsUtf8Bom()
    {
        const string remoteContent = "\uFEFF{\n  // remote comment\n  \"inbounds\": [{\"type\": \"tun\"},]\n}";
        var targetUri = new Uri("https://example.test/config.json");

        var downloaded = await RemoteConfigUpdater.FetchAsync(
            targetUri,
            (uri, _) =>
            {
                Assert.AreEqual(targetUri, uri);
                return Task.FromResult(remoteContent);
            },
            CancellationToken.None);

        Assert.IsFalse(downloaded.RawContent.StartsWith("\uFEFF", StringComparison.Ordinal));
        StringAssert.Contains(downloaded.RawContent, "// remote comment");
        Assert.IsTrue(downloaded.Config.RequiresElevation);
        Assert.IsNotNull(downloaded.Config.StandardInputContent);
        Assert.IsFalse(downloaded.Config.StandardInputContent.Contains("// remote comment"));
    }

    [TestMethod]
    public void BackupAndReplaceConfig_BacksUpExistingConfigAndOverwritesPreviousBackup()
    {
        using var directory = new TestDirectory();
        const string initialConfig = "{\"log\":{\"level\":\"info\"},\"inbounds\":[]}";
        const string secondConfig = "{\"log\":{\"level\":\"warn\"},\"inbounds\":[]}";
        const string thirdConfig = "{\"log\":{\"level\":\"error\"},\"inbounds\":[]}";

        var configPath = directory.WriteFile("config.json", initialConfig);
        var backupPath = RemoteConfigUpdater.GetBackupPath(configPath);

        RemoteConfigUpdater.BackupAndReplaceConfig(configPath, secondConfig);

        Assert.IsTrue(File.Exists(backupPath));
        Assert.AreEqual(initialConfig, File.ReadAllText(backupPath, Encoding.UTF8));
        Assert.AreEqual(secondConfig, File.ReadAllText(configPath, Encoding.UTF8));

        RemoteConfigUpdater.BackupAndReplaceConfig(configPath, thirdConfig);

        Assert.AreEqual(secondConfig, File.ReadAllText(backupPath, Encoding.UTF8));
        Assert.AreEqual(thirdConfig, File.ReadAllText(configPath, Encoding.UTF8));
    }

    [TestMethod]
    public void BackupAndReplaceConfig_RejectsInvalidJsonWithoutTouchingExistingFiles()
    {
        using var directory = new TestDirectory();
        const string originalConfig = "{\"inbounds\":[{\"type\":\"mixed\"}]}";
        const string existingBackup = "{\"inbounds\":[]}";

        var configPath = directory.WriteFile("config.json", originalConfig);
        var backupPath = directory.WriteFile("config.json.bak", existingBackup);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            RemoteConfigUpdater.BackupAndReplaceConfig(configPath, "<html>404 Not Found</html>"));

        Assert.AreEqual(originalConfig, File.ReadAllText(configPath, Encoding.UTF8));
        Assert.AreEqual(existingBackup, File.ReadAllText(backupPath, Encoding.UTF8));
    }

    [TestMethod]
    public async Task DownloadTextAsync_ThrowsOnHttpFailureStatus()
    {
        using var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                ReasonPhrase = "Not Found"
            }));

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            RemoteConfigUpdater.DownloadTextAsync(
                new Uri("https://example.test/missing.json"),
                handler,
                TimeSpan.FromSeconds(5),
                CancellationToken.None));

        StringAssert.Contains(exception.Message, "404");
    }

    [TestMethod]
    public async Task DownloadTextAsync_ThrowsTimeoutExceptionWhenServerIsTooSlow()
    {
        using var handler = new StubHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
            RemoteConfigUpdater.DownloadTextAsync(
                new Uri("https://example.test/slow.json"),
                handler,
                TimeSpan.FromMilliseconds(50),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task DownloadTextAsync_ThrowsOperationCanceledExceptionWhenCallerCancels()
    {
        using var cts = new CancellationTokenSource();
        using var handler = new StubHttpMessageHandler(async (_, token) =>
        {
            cts.Cancel();
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            RemoteConfigUpdater.DownloadTextAsync(
                new Uri("https://example.test/cancel.json"),
                handler,
                TimeSpan.FromSeconds(10),
                cts.Token));
    }

    [TestMethod]
    public async Task SingBootApp_FetchesBacksUpAndPreparesRestartWithRemoteConfig()
    {
        using var directory = new TestDirectory();
        const string remoteUrl =
            "https://raw.githubusercontent.com/SagerNet/sing-box/refs/heads/testing/test/config/vless-tls-client.json";
        const string oldConfig = "{\"inbounds\":[{\"type\":\"mixed\",\"listen_port\":2080}]}";
        const string newConfig = "{\"inbounds\":[{\"type\":\"mixed\",\"listen_port\":7890}]}";

        directory.WriteFile("sing-box.exe", "stub");
        var configPath = directory.WriteFile("config.json", oldConfig);
        directory.WriteFile(SingBootSettings.FileName, $"{{\"configUrl\":\"{remoteUrl}\"}}");

        using var app = new SingBootApp(
            LaunchMode.Normal,
            directory.Path,
            (uri, _) =>
            {
                Assert.AreEqual(new Uri(remoteUrl), uri);
                return Task.FromResult(newConfig);
            },
            conflictDetector: (_, _) => false);

        Assert.IsTrue(app.TryGetRemoteConfigUrl(out var parsedUrl, out var settingsMessage), settingsMessage);
        Assert.IsNotNull(parsedUrl);

        var downloaded = await app.FetchRemoteConfigAsync(parsedUrl, CancellationToken.None);
        var result = app.ApplyRemoteConfigAndPrepareRestart(downloaded, out var prepMessage);

        Assert.AreEqual(StartPreparationResult.Ready, result, prepMessage);
        Assert.AreEqual(oldConfig, File.ReadAllText(configPath + ".bak", Encoding.UTF8));
        Assert.AreEqual(newConfig, File.ReadAllText(configPath, Encoding.UTF8));
        Assert.IsNotNull(app.Config);
        Assert.AreEqual(newConfig, app.Config.StandardInputContent);
    }

    [TestMethod]
    public async Task EndToEnd_StartsCoreFetchesRemoteConfigBacksUpAndRestartsWithNewStdin()
    {
        using var directory = new TestDirectory();
        const string remoteUrl =
            "https://raw.githubusercontent.com/SagerNet/sing-box/refs/heads/testing/test/config/vless-tls-client.json";
        const string initialConfig = "{\"inbounds\":[{\"type\":\"mixed\",\"listen_port\":2080}]}";
        const string updatedConfig = "{\"inbounds\":[{\"type\":\"mixed\",\"listen_port\":7890}]}";

        CompileStubSingBoxExecutable(System.IO.Path.Combine(directory.Path, "sing-box.exe"));
        var configPath = directory.WriteFile("config.json", initialConfig);
        directory.WriteFile(SingBootSettings.FileName, $"{{\"configUrl\":\"{remoteUrl}\"}}");

        var observedStates = new ConcurrentQueue<CoreState>();
        var firstRunningTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRunningTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runningCount = 0;

        using var app = new SingBootApp(
            LaunchMode.Normal,
            directory.Path,
            (_, _) => Task.FromResult(updatedConfig),
            conflictDetector: (_, _) => false,
            startupGracePeriodMs: 150);

        app.OnCoreEvent += evt =>
        {
            if (evt.Kind != CoreEventKind.StateChanged)
                return;

            observedStates.Enqueue(evt.State);
            if (evt.State == CoreState.Running)
            {
                var current = Interlocked.Increment(ref runningCount);
                if (current == 1)
                    firstRunningTcs.TrySetResult(true);
                else if (current == 2)
                    secondRunningTcs.TrySetResult(true);
            }
        };

        Assert.AreEqual(StartPreparationResult.Ready, app.PrepareForStart(out var startPrepMsg), startPrepMsg);
        Assert.IsTrue(app.Start(out var startMsg), startMsg);

        await WaitWithTimeoutAsync(firstRunningTcs.Task, TimeSpan.FromSeconds(5));

        Assert.IsTrue(app.TryGetRemoteConfigUrl(out var url, out var settingsMsg), settingsMsg);
        Assert.IsNotNull(url);

        var downloaded = await app.FetchRemoteConfigAsync(url, CancellationToken.None);
        Assert.AreEqual(
            StartPreparationResult.Ready,
            app.ApplyRemoteConfigAndPrepareRestart(downloaded, out var restartPrepMsg),
            restartPrepMsg);
        Assert.IsTrue(app.Restart(out var restartMsg), restartMsg);

        await WaitWithTimeoutAsync(secondRunningTcs.Task, TimeSpan.FromSeconds(10));

        app.Shutdown();

        Assert.AreEqual(initialConfig, File.ReadAllText(configPath + ".bak", Encoding.UTF8));
        Assert.AreEqual(updatedConfig, File.ReadAllText(configPath, Encoding.UTF8));

        var launchesLogPath = System.IO.Path.Combine(directory.Path, "launches.log");
        Assert.IsTrue(File.Exists(launchesLogPath));
        var launches = File.ReadAllLines(launchesLogPath, Encoding.UTF8);
        CollectionAssert.AreEqual(new[] { initialConfig, updatedConfig }, launches);

        CollectionAssert.AreEqual(
            new[]
            {
                CoreState.Starting,
                CoreState.Running,
                CoreState.Stopping,
                CoreState.Starting,
                CoreState.Running
            },
            observedStates.ToArray());
    }

    private static async Task WaitWithTimeoutAsync(Task task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (!ReferenceEquals(completed, task))
            Assert.Fail($"Timed out after {timeout.TotalSeconds}s waiting for expected core state.");
        await task;
    }

    private static void CompileStubSingBoxExecutable(string outputPath)
    {
        const string source = """
                              using System;
                              using System.IO;
                              using System.Text;
                              using System.Threading;

                              internal static class StubSingBox
                              {
                                  private static int Main(string[] args)
                                  {
                                      if (args.Length != 3 || args[0] != "run" || args[1] != "-c" || args[2] != "stdin")
                                      {
                                          Console.Error.WriteLine("FATAL unexpected arguments");
                                          return 2;
                                      }

                                      var exitEvent = new ManualResetEvent(false);
                                      Console.CancelKeyPress += (_, eventArgs) =>
                                      {
                                          eventArgs.Cancel = true;
                                          exitEvent.Set();
                                      };

                                      var stdin = Console.In.ReadToEnd().Trim();
                                      File.AppendAllText(
                                          Path.Combine(Environment.CurrentDirectory, "launches.log"),
                                          stdin + Environment.NewLine,
                                          new UTF8Encoding(false));

                                      exitEvent.WaitOne(TimeSpan.FromSeconds(20));
                                      return 0;
                                  }
                              }
                              """;

        using var provider = new CSharpCodeProvider();
        var parameters = new CompilerParameters
        {
            GenerateExecutable = true,
            OutputAssembly = outputPath,
            TreatWarningsAsErrors = false
        };
        parameters.ReferencedAssemblies.Add("System.dll");

        var results = provider.CompileAssemblyFromSource(parameters, source);
        if (results.Errors.HasErrors)
        {
            var errors = new StringBuilder();
            foreach (CompilerError error in results.Errors)
                errors.AppendLine(error.ToString());
            Assert.Fail($"Failed to compile stub sing-box.exe: {errors}");
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return _responder(request, cancellationToken);
        }
    }
}
