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
}
