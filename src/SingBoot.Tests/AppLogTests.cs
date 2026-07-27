using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SingBoot.Tests;

[TestClass]
public sealed class AppLogTests
{
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
