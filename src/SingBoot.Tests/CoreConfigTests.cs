using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SingBoot.Tests;

[TestClass]
public sealed class CoreConfigTests
{
    [TestMethod]
    public void LoadSingBox_DetectsTunInbound()
    {
        var config = LoadSingBox("{\"inbounds\":[{\"type\":\"mixed\"},{\"type\":\"tun\"}]}");

        Assert.IsTrue(config.RequiresElevation);
    }

    [TestMethod]
    public void LoadSingBox_DoesNotElevateWithoutTunInbound()
    {
        var config = LoadSingBox("{\"inbounds\":[{\"type\":\"mixed\"}]}");

        Assert.IsFalse(config.RequiresElevation);
    }

    [TestMethod]
    public void LoadSingBox_RejectsInvalidJson()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => LoadSingBox("{\"port\": 1 2}"));
    }

    [TestMethod]
    [DataRow("404")]
    [DataRow("[]")]
    [DataRow("\"string\"")]
    public void Parse_RejectsNonObjectJsonRoot(string json)
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => CoreConfig.Parse(json));
    }

    [TestMethod]
    public void Parse_StripsLeadingUtf8Bom()
    {
        var config = CoreConfig.Parse("\uFEFF{\"inbounds\":[{\"type\":\"tun\"}]}");

        Assert.IsTrue(config.RequiresElevation);
    }

    private static CoreConfig LoadSingBox(string json)
    {
        using var directory = new TestDirectory();
        var configPath = directory.WriteFile("config.json", json);
        var profile = new CoreProfile("sing-box",
            System.IO.Path.Combine(directory.Path, "sing-box.exe"), configPath, directory.Path);
        return CoreConfig.Load(profile);
    }
}
