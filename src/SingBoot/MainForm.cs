using Microsoft.Win32;
using System.Reflection;

namespace SingBoot;

internal sealed class MainForm : Form
{
    private readonly SingBootApp _app;
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _miStartStop;
    private readonly ToolStripMenuItem _miAutoStart;
    private readonly Icon _iconRunning;
    private readonly Icon _iconStopped;
    private readonly bool _startCoreAfterLaunch;
    private bool _closePending;
    private bool _systemExitPending;
    private bool _manualQuitRequested;
    private bool _exitStatePrepared;

    public MainForm(SingBootApp app, bool startCoreAfterLaunch)
    {
        _app = app;
        _startCoreAfterLaunch = startCoreAfterLaunch;

        // Load icons from embedded resources
        _iconRunning = LoadEmbeddedIcon("icon.ico");
        _iconStopped = LoadEmbeddedIcon("icon_disabled.ico");

        // Build context menu
        _miStartStop = new ToolStripMenuItem("Start", null, OnStartStopClick);
        _miAutoStart = new ToolStripMenuItem("Auto-start")
        {
            CheckOnClick = true,
            Checked = AutoStart.IsEnabled()
        };
        _miAutoStart.Click += OnAutoStartClick;

        var miQuit = new ToolStripMenuItem("Quit", null, OnQuitClick);

        var menu = new ContextMenuStrip();
        menu.Items.Add(_miStartStop);
        menu.Items.Add(_miAutoStart);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(miQuit);

        // Setup tray icon
        _trayIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = _iconStopped,
            Text = "sing-boot",
            Visible = true
        };
        _trayIcon.MouseClick += OnTrayClick;

        // Hide the form
        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        Opacity = 0;

        // Subscribe to app events
        _app.OnCoreEvent += HandleCoreEvent;
        SystemEvents.SessionEnding += OnSessionEnding;
        UpdateUI(_app.State);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        if (_startCoreAfterLaunch)
            BeginInvoke(TryStartCore);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_closePending)
        {
            // Already shutting down — allow close
            base.OnFormClosing(e);
            return;
        }

        _closePending = true;
        ShowOnlyQuitInTray();
        PrepareExitState(isSystemExit: _systemExitPending || e.CloseReason == CloseReason.WindowsShutDown,
            isManualQuit: _manualQuitRequested);

        _app.OnCoreEvent -= HandleCoreEvent;
        _app.Shutdown();

        _trayIcon.Visible = false;

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.SessionEnding -= OnSessionEnding;
            _trayIcon.Dispose();
            _iconRunning.Dispose();
            _iconStopped.Dispose();
            _app.Dispose();
        }
        base.Dispose(disposing);
    }

    // ─── Event handlers ──────────────────────────────────────────────

    private void HandleCoreEvent(CoreEvent evt)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => HandleCoreEvent(evt));
            return;
        }

        switch (evt.Kind)
        {
            case CoreEventKind.StateChanged:
                UpdateUI(evt.State);
                if (evt.State == CoreState.Failed && !string.IsNullOrEmpty(evt.Message))
                    ShowBalloon(evt.Message, "Error", ToolTipIcon.Error);
                break;

            case CoreEventKind.Error:
                ShowBalloon(evt.Message, "Error", ToolTipIcon.Error);
                break;
        }
    }

    private void OnStartStopClick(object? sender, EventArgs e)
    {
        if (_closePending) return;
        ToggleStartStop();
    }

    private void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && !_closePending)
            ToggleStartStop();
    }

    private void OnAutoStartClick(object? sender, EventArgs e)
    {
        _app.UpdateAutoStart(_miAutoStart.Checked);
    }

    private void OnQuitClick(object? sender, EventArgs e)
    {
        _manualQuitRequested = true;
        Close();
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e)
    {
        _systemExitPending = true;
        PrepareExitState(isSystemExit: true);
    }

    // ─── UI helpers ──────────────────────────────────────────────────

    private void ToggleStartStop()
    {
        if (_app.IsRunning)
        {
            _app.Stop();
            return;
        }

        TryStartCore();
    }

    private void TryStartCore()
    {
        if (_closePending)
            return;

        var preparation = _app.PrepareForStart(out var message);
        switch (preparation)
        {
            case StartPreparationResult.Ready:
                _app.Start();
                break;

            case StartPreparationResult.RelaunchStarted:
                Close();
                break;

            case StartPreparationResult.Blocked:
                if (!string.IsNullOrEmpty(message))
                    ShowBalloon(message, "Start", ToolTipIcon.Warning);
                break;
        }
    }

    private void UpdateUI(CoreState state)
    {
        var running = state == CoreState.Running;

        _trayIcon.Icon = running ? _iconRunning : _iconStopped;
        _miStartStop.Text = running ? "Stop" : "Start";
        _miStartStop.Enabled = state != CoreState.Starting && state != CoreState.Stopping;
    }

    private void ShowBalloon(string text, string title, ToolTipIcon icon, int timeoutMs = 10000)
    {
        _trayIcon.ShowBalloonTip(timeoutMs, title, text, icon);
    }

    private void ShowOnlyQuitInTray()
    {
        if (_trayIcon.ContextMenuStrip is null) return;

        foreach (ToolStripItem item in _trayIcon.ContextMenuStrip.Items)
            item.Visible = item.Text == "Quit";
    }

    private void PrepareExitState(bool isSystemExit, bool isManualQuit = false)
    {
        if (_exitStatePrepared)
            return;

        _exitStatePrepared = true;

        if (isSystemExit)
        {
            _app.PrepareForSystemExit();
            return;
        }

        if (isManualQuit)
            _app.PrepareForManualExit();
    }

    private static Icon LoadEmbeddedIcon(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
        return new Icon(stream);
    }
}
