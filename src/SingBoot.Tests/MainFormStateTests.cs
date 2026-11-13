using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SingBoot.Tests;

[TestClass]
public sealed class MainFormStateTests
{
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
    public void ConsumeShouldScheduleRemoteUpdate_SkipsOnlyFirstStartAfterPostUpdateHandoff()
    {
        var skipOnce = true;

        Assert.IsFalse(MainForm.ConsumeShouldScheduleRemoteUpdate(ref skipOnce));
        Assert.IsFalse(skipOnce);
        Assert.IsTrue(MainForm.ConsumeShouldScheduleRemoteUpdate(ref skipOnce));
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
    public void GetRemoteConfigApplyAction_DistinguishesManualAndPostStartUpdates()
    {
        Assert.AreEqual(
            RemoteConfigApplyAction.SaveAndRestartCore,
            MainForm.GetRemoteConfigApplyAction(
                closePending: false,
                cancellationRequested: false,
                isManualRequest: false,
                currentState: CoreState.Running));
        Assert.AreEqual(
            RemoteConfigApplyAction.SaveAndRestartCore,
            MainForm.GetRemoteConfigApplyAction(
                closePending: false,
                cancellationRequested: false,
                isManualRequest: true,
                currentState: CoreState.Running));
        Assert.AreEqual(
            RemoteConfigApplyAction.SaveOnly,
            MainForm.GetRemoteConfigApplyAction(
                closePending: false,
                cancellationRequested: false,
                isManualRequest: true,
                currentState: CoreState.Stopped));
        Assert.AreEqual(
            RemoteConfigApplyAction.SaveOnly,
            MainForm.GetRemoteConfigApplyAction(
                closePending: false,
                cancellationRequested: false,
                isManualRequest: true,
                currentState: CoreState.Failed));
        Assert.AreEqual(
            RemoteConfigApplyAction.None,
            MainForm.GetRemoteConfigApplyAction(
                closePending: false,
                cancellationRequested: false,
                isManualRequest: false,
                currentState: CoreState.Stopped));
        Assert.AreEqual(
            RemoteConfigApplyAction.None,
            MainForm.GetRemoteConfigApplyAction(
                closePending: true,
                cancellationRequested: false,
                isManualRequest: true,
                currentState: CoreState.Running));
        Assert.AreEqual(
            RemoteConfigApplyAction.None,
            MainForm.GetRemoteConfigApplyAction(
                closePending: false,
                cancellationRequested: true,
                isManualRequest: true,
                currentState: CoreState.Running));
    }

    private static readonly string[] HandoffOnlyArgs = ["--handoff-start"];

    [TestMethod]
    public void ProgramArguments_SupportPostUpdateHandoffFlag()
    {
        var args = PrivilegeHelper.BuildElevatedStartArguments(skipConfigUpdateOnce: true)
            .Split(' ');

        Assert.AreEqual(LaunchMode.HandoffStart, Program.ParseLaunchMode(args));
        Assert.IsTrue(Program.ParseSkipInitialConfigUpdate(args));
        Assert.IsFalse(Program.ParseSkipInitialConfigUpdate(HandoffOnlyArgs));
    }
}
