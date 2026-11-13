using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SingBoot.Tests;

[TestClass]
public sealed class SingBootSettingsTests
{
    [TestMethod]
    public void TryLoad_ReturnsEmptyWhenSettingsFileDoesNotExist()
    {
        using var directory = new TestDirectory();

        var succeeded = SingBootSettings.TryLoad(directory.Path, out var settings, out var message);

        Assert.IsTrue(succeeded, message);
        Assert.IsFalse(settings.HasConfigUrl);
        Assert.IsNull(settings.ConfigUrl);
    }

    [TestMethod]
    public void TryLoad_ParsesConfigUrlWithCommentsAndTrailingComma()
    {
        using var directory = new TestDirectory();
        const string expectedUrl =
            "https://raw.githubusercontent.com/SagerNet/sing-box/refs/heads/testing/test/config/vless-tls-client.json";
        directory.WriteFile(
            SingBootSettings.FileName,
            $$"""
              {
                // Remote sing-box config URL
                "configUrl": "{{expectedUrl}}",
              }
              """);

        var succeeded = SingBootSettings.TryLoad(directory.Path, out var settings, out var message);

        Assert.IsTrue(succeeded, message);
        Assert.IsTrue(settings.HasConfigUrl);
        Assert.AreEqual(new Uri(expectedUrl), settings.ConfigUrl);
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"configUrl\": null}")]
    [DataRow("{\"configUrl\": \"   \"}")]
    public void TryParse_ReturnsEmptyWhenConfigUrlIsMissingOrBlank(string json)
    {
        var succeeded = SingBootSettings.TryParse(json, out var settings, out var message);

        Assert.IsTrue(succeeded, message);
        Assert.IsFalse(settings.HasConfigUrl);
        Assert.IsNull(settings.ConfigUrl);
    }

    [TestMethod]
    [DataRow("{\"configUrl\": \"ftp://example.com/config.json\"}")]
    [DataRow("{\"configUrl\": \"file:///C:/sing-box/config.json\"}")]
    [DataRow("{\"configUrl\": \"relative/config.json\"}")]
    [DataRow("{\"configUrl\": 123}")]
    [DataRow("[]")]
    [DataRow("{invalid")]
    public void TryParse_RejectsInvalidSettings(string json)
    {
        var succeeded = SingBootSettings.TryParse(json, out var settings, out var message);

        Assert.IsFalse(succeeded);
        Assert.IsFalse(settings.HasConfigUrl);
        Assert.IsFalse(string.IsNullOrWhiteSpace(message));
    }

    [TestMethod]
    public void DescribeUriForLog_RedactsPathQueryAndCredentials()
    {
        var uri = new Uri("https://user:secret@example.com:8443/sub/path?token=abcd1234#node");

        var description = SingBootSettings.DescribeUriForLog(uri);

        Assert.AreEqual("https://example.com:8443", description);
    }
}
