using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SingBoot.Tests;

[TestClass]
public sealed class AppLogTests
{
    [TestMethod]
    public void DescribeExceptionIncludesNetworkCauseWithoutUrlCredentials()
    {
        var exception = new HttpRequestException("Failed https://user:password@example.test/private/config?token=secret",
            new WebException("Connection failed", new SocketException(10054), WebExceptionStatus.ReceiveFailure, null));

        var detail = AppLog.DescribeException(exception);

        StringAssert.Contains(detail, "HttpRequestException");
        StringAssert.Contains(detail, "status=ReceiveFailure");
        StringAssert.Contains(detail, "socket=ConnectionReset; native=10054");
        StringAssert.Contains(detail, "https://example.test/<redacted>");
        Assert.IsFalse(detail.Contains("password"));
        Assert.IsFalse(detail.Contains("/private"));
        Assert.IsFalse(detail.Contains("token=secret"));
    }

    [TestMethod]
    public void DescribeExceptionDoesNotEchoNestedParserInput()
    {
        var detail = AppLog.DescribeException(new InvalidOperationException("Invalid configuration",
            new ArgumentException("Invalid JSON primitive: private-value")));

        StringAssert.Contains(detail, "ArgumentException");
        Assert.IsFalse(detail.Contains("private-value"));
    }

    [TestMethod]
    public void TryWriteCreatesDailyLogAndKeepsEachEntryOnOneLine()
    {
        using var directory = new TestDirectory();
        var timestamp = new DateTimeOffset(2026, 7, 27, 16, 30, 0, TimeSpan.FromHours(8));

        var written = AppLog.TryWrite(directory.Path, timestamp, "startup\r\ndecision");

        Assert.IsTrue(written);
        var logPath = AppLog.GetLogPath(directory.Path, timestamp);
        Assert.IsTrue(File.Exists(logPath));
        var content = File.ReadAllText(logPath);
        StringAssert.Contains(content, "2026-07-27T16:30:00.0000000+08:00 startup  decision");
        Assert.AreEqual(1, File.ReadAllLines(logPath).Length);
    }
}
