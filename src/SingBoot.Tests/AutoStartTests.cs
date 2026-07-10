using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SingBoot.Tests;

[TestClass]
public sealed class AutoStartTests
{
    [TestMethod]
    public void IsCurrentExecutableCommand_AcceptsOnlyTheCurrentExecutableAndArgument()
    {
        var executablePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Sing Boot", "sing-boot.exe");
        var command = AutoStart.BuildCommand(executablePath);

        Assert.IsTrue(AutoStart.IsCurrentExecutableCommand(command, executablePath));
        Assert.IsTrue(AutoStart.IsCurrentExecutableCommand(command.ToUpperInvariant(), executablePath));
        Assert.IsFalse(AutoStart.IsCurrentExecutableCommand(
            AutoStart.BuildCommand(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "old", "sing-boot.exe")),
            executablePath));
        Assert.IsFalse(AutoStart.IsCurrentExecutableCommand($"\"{executablePath}\"", executablePath));
    }
}
