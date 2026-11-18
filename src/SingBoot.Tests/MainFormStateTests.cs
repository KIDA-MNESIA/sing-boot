using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SingBoot.Tests;

[TestClass]
public sealed class MainFormStateTests
{
    private static readonly string[] HandoffArgs = ["--handoff-start"];
    private static readonly string[] AutoStartArgs = ["--auto-start"];
    private static readonly string[] EmptyArgs = [];

    [TestMethod]
    public void GetStartStopAction_MapsOnlyStableStatesToActions()
    {
        Assert.AreEqual(StartStopAction.Start, MainForm.GetStartStopAction(CoreState.Stopped, actionPending: false));
        Assert.AreEqual(StartStopAction.Start, MainForm.GetStartStopAction(CoreState.Failed, actionPending: false));
        Assert.AreEqual(StartStopAction.Stop, MainForm.GetStartStopAction(CoreState.Running, actionPending: false));
        Assert.AreEqual(StartStopAction.None, MainForm.GetStartStopAction(CoreState.Starting, actionPending: false));
        Assert.AreEqual(StartStopAction.None, MainForm.GetStartStopAction(CoreState.Stopping, actionPending: false));
        Assert.AreEqual(StartStopAction.None, MainForm.GetStartStopAction(CoreState.Stopped, actionPending: true));
    }

    [TestMethod]
    public void ShouldProceedWithAutomaticStartRequiresTheRequestToRemainEnabled()
    {
        Assert.IsTrue(MainForm.ShouldProceedWithAutomaticStart(
            closePending: false,
            cancellationRequested: false,
            automaticStartStillEnabled: true));
        Assert.IsFalse(MainForm.ShouldProceedWithAutomaticStart(
            closePending: false,
            cancellationRequested: false,
            automaticStartStillEnabled: false));
        Assert.IsFalse(MainForm.ShouldProceedWithAutomaticStart(
            closePending: false,
            cancellationRequested: true,
            automaticStartStillEnabled: true));
    }

    [TestMethod]
    public void ShouldClearResumeIntentOnExitOnlyForExplicitManualQuit()
    {
        Assert.IsTrue(MainForm.ShouldClearResumeIntentOnExit(
            isSystemExit: false,
            isManualQuit: true));
        Assert.IsFalse(MainForm.ShouldClearResumeIntentOnExit(
            isSystemExit: true,
            isManualQuit: false));
        Assert.IsFalse(MainForm.ShouldClearResumeIntentOnExit(
            isSystemExit: true,
            isManualQuit: true));
        Assert.IsFalse(MainForm.ShouldClearResumeIntentOnExit(
            isSystemExit: false,
            isManualQuit: false));
    }

    [TestMethod]
    public void CanUpdateConfig_AllowsOnlyStableStatesWithoutPendingOperations()
    {
        Assert.IsTrue(MainForm.CanUpdateConfig(CoreState.Running, startStopActionPending: false, isUpdatingConfig: false));
        Assert.IsTrue(MainForm.CanUpdateConfig(CoreState.Stopped, startStopActionPending: false, isUpdatingConfig: false));
        Assert.IsTrue(MainForm.CanUpdateConfig(CoreState.Failed, startStopActionPending: false, isUpdatingConfig: false));
        Assert.IsFalse(MainForm.CanUpdateConfig(CoreState.Starting, startStopActionPending: false, isUpdatingConfig: false));
        Assert.IsFalse(MainForm.CanUpdateConfig(CoreState.Stopping, startStopActionPending: false, isUpdatingConfig: false));
        Assert.IsFalse(MainForm.CanUpdateConfig(CoreState.Running, startStopActionPending: true, isUpdatingConfig: false));
        Assert.IsFalse(MainForm.CanUpdateConfig(CoreState.Running, startStopActionPending: false, isUpdatingConfig: true));
    }

    [TestMethod]
    public void GetRemoteConfigApplyAction_MapsCurrentStateForTrayUpdate()
    {
        Assert.AreEqual(
            RemoteConfigApplyAction.SaveAndRestartCore,
            MainForm.GetRemoteConfigApplyAction(
                closePending: false,
                cancellationRequested: false,
                currentState: CoreState.Running));
        Assert.AreEqual(
            RemoteConfigApplyAction.SaveOnly,
            MainForm.GetRemoteConfigApplyAction(
                closePending: false,
                cancellationRequested: false,
                currentState: CoreState.Stopped));
        Assert.AreEqual(
            RemoteConfigApplyAction.SaveOnly,
            MainForm.GetRemoteConfigApplyAction(
                closePending: false,
                cancellationRequested: false,
                currentState: CoreState.Failed));
        Assert.AreEqual(
            RemoteConfigApplyAction.None,
            MainForm.GetRemoteConfigApplyAction(
                closePending: false,
                cancellationRequested: false,
                currentState: CoreState.Starting));
        Assert.AreEqual(
            RemoteConfigApplyAction.None,
            MainForm.GetRemoteConfigApplyAction(
                closePending: false,
                cancellationRequested: false,
                currentState: CoreState.Stopping));
        Assert.AreEqual(
            RemoteConfigApplyAction.None,
            MainForm.GetRemoteConfigApplyAction(
                closePending: true,
                cancellationRequested: false,
                currentState: CoreState.Running));
        Assert.AreEqual(
            RemoteConfigApplyAction.None,
            MainForm.GetRemoteConfigApplyAction(
                closePending: false,
                cancellationRequested: true,
                currentState: CoreState.Running));
    }

    [TestMethod]
    public void ParseLaunchMode_MapsSupportedFlags()
    {
        Assert.AreEqual(LaunchMode.HandoffStart, Program.ParseLaunchMode(HandoffArgs));
        Assert.AreEqual(LaunchMode.AutoStart, Program.ParseLaunchMode(AutoStartArgs));
        Assert.AreEqual(LaunchMode.Normal, Program.ParseLaunchMode(EmptyArgs));
    }
}
