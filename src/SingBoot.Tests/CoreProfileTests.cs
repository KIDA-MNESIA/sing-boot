using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SingBoot.Tests;

[TestClass]
public sealed class CoreProfileTests
{
    [TestMethod]
    public void CreateStartRequest_MihomoPassesDiscoveredConfigWithFileArgument()
    {
        using var directory = new TestDirectory();
        var configPath = directory.WriteFile("config.yml", "mixed-port: 7890");
        var executablePath = System.IO.Path.Combine(directory.Path, "mihomo.exe");
        var profile = new CoreProfile(CoreKind.Mihomo, "mihomo", executablePath, configPath, directory.Path);
        var config = CoreConfig.Load(profile);

        var request = profile.CreateStartRequest(config);

        StringAssert.Contains(request.CommandLine, $"-d \"{profile.DirectoryArgument}\"");
        StringAssert.Contains(request.CommandLine, $"-f \"{profile.ConfigPath}\"");
    }
}
