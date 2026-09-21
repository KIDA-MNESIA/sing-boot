using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SingBoot.Tests;

[TestClass]
public sealed class CoreProfileTests
{
    [TestMethod]
    public void CreateStartRequest_SingBoxPassesNormalizedConfigThroughStandardInput()
    {
        using var directory = new TestDirectory();
        var configPath = directory.WriteFile("config.json", "{\"inbounds\": [],}");
        var executablePath = System.IO.Path.Combine(directory.Path, "sing-box.exe");
        var profile = new CoreProfile("sing-box", executablePath, configPath, directory.Path);
        var config = CoreConfig.Load(profile);

        var request = profile.CreateStartRequest(config);

        Assert.AreEqual($"\"{profile.ExecutablePath}\" run -c stdin", request.CommandLine);
        Assert.AreEqual(config.StandardInputContent, request.StandardInputContent);
        Assert.AreEqual("{\"inbounds\": []}", request.StandardInputContent);
    }

    [TestMethod]
    public void TryDiscover_FindsSingBoxExecutableAndConfig()
    {
        using var directory = new TestDirectory();
        var executablePath = directory.WriteFile("sing-box.exe", "");
        var configPath = directory.WriteFile("config.json", "{}");

        var discovered = CoreDiscovery.TryDiscover(directory.Path, out var profile, out var message);

        Assert.IsTrue(discovered, message);
        Assert.IsNotNull(profile);
        Assert.AreEqual("sing-box", profile.DisplayName);
        Assert.AreEqual(System.IO.Path.GetFullPath(executablePath), profile.ExecutablePath);
        Assert.AreEqual(System.IO.Path.GetFullPath(configPath), profile.ConfigPath);
    }
}
