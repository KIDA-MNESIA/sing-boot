using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SingBoot.Tests;

[TestClass]
public sealed class CoreConfigTests
{
    [TestMethod]
    [DataRow("tun:\n  enable: true")]
    [DataRow("listeners: [{name: web, type: http}, {name: tunnel, type: tun}]")]
    [DataRow("defaults: &tunDefaults\n  enable: true\ntun: *tunDefaults")]
    [DataRow("defaults: &tunDefaults\n  enable: true\ntun:\n  <<: *tunDefaults")]
    public void LoadMihomo_DetectsTunAcrossValidYamlForms(string yaml)
    {
        var config = LoadMihomo(yaml);

        Assert.IsTrue(config.RequiresElevation);
    }

    [TestMethod]
    public void LoadMihomo_DoesNotElevateWhenTunIsDisabled()
    {
        var config = LoadMihomo("tun:\n  enable: false");

        Assert.IsFalse(config.RequiresElevation);
    }

    [TestMethod]
    public void LoadMihomo_RejectsInvalidYaml()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => LoadMihomo("tun: ["));
    }

    [TestMethod]
    public void LoadSingBox_RejectsInvalidJsonThatPreviouslyChangedMeaning()
    {
        using var directory = new TestDirectory();
        var configPath = directory.WriteFile("config.json", "{\"port\": 1 2}");
        var profile = new CoreProfile(CoreKind.SingBox, "sing-box",
            System.IO.Path.Combine(directory.Path, "sing-box.exe"), configPath, directory.Path);

        Assert.ThrowsExactly<InvalidOperationException>(() => CoreConfig.Load(profile));
    }

    private static CoreConfig LoadMihomo(string yaml)
    {
        using var directory = new TestDirectory();
        var configPath = directory.WriteFile("config.yaml", yaml);
        var profile = new CoreProfile(CoreKind.Mihomo, "mihomo",
            System.IO.Path.Combine(directory.Path, "mihomo.exe"), configPath, directory.Path);
        return CoreConfig.Load(profile);
    }
}
