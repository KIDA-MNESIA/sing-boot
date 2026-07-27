using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SingBoot.Tests;

[TestClass]
public sealed class StartupDecisionTests
{
    [TestMethod]
    public void AutoStartRequiresEnabledEntryAndRememberedRunningIntent()
    {
        var resume = StartupDecision.Create(
            LaunchMode.AutoStart,
            autoStartEnabled: true,
            resumeRequested: true);
        var disabled = StartupDecision.Create(
            LaunchMode.AutoStart,
            autoStartEnabled: false,
            resumeRequested: true);
        var stopped = StartupDecision.Create(
            LaunchMode.AutoStart,
            autoStartEnabled: true,
            resumeRequested: false);

        Assert.IsTrue(resume.StartCoreAfterLaunch);
        Assert.IsTrue(resume.WaitForDefaultGatewayBeforeStart);
        Assert.AreEqual("wait-for-default-gateway-then-start", resume.Action);
        Assert.IsFalse(disabled.StartCoreAfterLaunch);
        Assert.IsFalse(stopped.StartCoreAfterLaunch);
    }

    [TestMethod]
    public void HandoffStartsImmediatelyWithoutNetworkWait()
    {
        var decision = StartupDecision.Create(
            LaunchMode.HandoffStart,
            autoStartEnabled: true,
            resumeRequested: true);

        Assert.IsTrue(decision.StartCoreAfterLaunch);
        Assert.IsFalse(decision.WaitForDefaultGatewayBeforeStart);
        Assert.AreEqual("start-after-launch", decision.Action);
    }

    [TestMethod]
    public void NormalLaunchOnlyStartsTheTray()
    {
        var decision = StartupDecision.Create(
            LaunchMode.Normal,
            autoStartEnabled: true,
            resumeRequested: true);

        Assert.IsFalse(decision.StartCoreAfterLaunch);
        Assert.AreEqual("tray-only", decision.Action);
    }
}
