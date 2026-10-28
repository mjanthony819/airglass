using AirGlass.Core;

namespace AirGlass;

public sealed class MainForm : Form
{
    private readonly EngineManager _engine = new();
    private readonly StatusParser _parser = new();
    private readonly EngineInstaller _installer = new();
    private readonly System.Windows.Forms.Timer _watchdog = new() { Interval = 1000 };
    private AppSettings _settings = AppSettings.Load();
    private bool _downloading;
    private bool _reallyClose;

    private readonly Label _statusLabel = new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI", 16f, FontStyle.Bold),
        Text = "Stopped",
    };
    private readonly Label _detailLabel = new()
    {
        AutoSize = true,
        ForeColor = Color.DimGray,
        Text = "Mirroring is off.",
    };
    private readonly Button _startButton = new() { Text = "Start mirroring", AutoSize = true };
    private readonly Button _stopButton = new() { Text = "Stop", AutoSize = true, Enabled = false };
    private readonly Button _downloadButton = new() { Text = "Download engine", AutoSize = true };
    private readonly Button _linkButton = new() { Text = "iPhone Link...", AutoSize = true };
    private readonly ProgressBar _progress = new() { Width = 420, Visible = false };
    private readonly Label _progressLabel = new() { AutoSize = true, Visible = false };
    private readonly TextBox _nameBox = new() { Width = 180 };
    private readonly ComboBox _resolutionBox = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 130,
    };
    private readonly ComboBox _latencyBox = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 190,
    };
    private readonly CheckBox _pinCheck = new() { Text = "Require PIN", AutoSize = true };
    private readonly TextBox _pinBox = new() { Width = 60, MaxLength = 4 };
    private readonly NumericUpDown _portBox = new()
    {
        Minimum = 1024,
        Maximum = 65532,
        Width = 80,
    };
    private readonly CheckBox _startupCheck = new() { Text = "Start AirGlass with Windows", AutoSize = true };
    private readonly TextBox _logBox = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 9f),
        BackColor = Color.White,
    };
    private readonly NotifyIcon _tray;

    public MainForm()
    {
        Text = "AirGlass - iPhone mirroring for Windows";
        ClientSize = new Size(620, 560);
        MinimumSize = new Size(560, 480);
        StartPosition = FormStartPosition.CenterScreen;

        Icon appIcon;
        try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
        catch { appIcon = SystemIcons.Application; }
        Icon = appIcon;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // status
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // detail
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // buttons
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // progress
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // settings
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // log
        Controls.Add(root);

        root.Controls.Add(_statusLabel, 0, 0);
        root.Controls.Add(_detailLabel, 0, 1);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.AddRange(new Control[] { _startButton, _stopButton, _downloadButton, _linkButton });
        root.Controls.Add(buttons, 0, 2);

        var progressPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        progressPanel.Controls.Add(_progress);
        progressPanel.Controls.Add(_progressLabel);
        root.Controls.Add(progressPanel, 0, 3);

        var settings = new GroupBox { Text = "Settings", AutoSize = true, Dock = DockStyle.Top };
        var grid = new TableLayoutPanel { ColumnCount = 4, AutoSize = true, Dock = DockStyle.Top };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.Controls.Add(grid);
        grid.Controls.Add(new Label { Text = "AirPlay name", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        grid.Controls.Add(_nameBox, 1, 0);
        grid.Controls.Add(new Label { Text = "Resolution", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);
        grid.Controls.Add(_resolutionBox, 3, 0);
        grid.Controls.Add(new Label { Text = "Mode", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        grid.Controls.Add(_latencyBox, 1, 1);
        grid.Controls.Add(new Label { Text = "Base port", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 1);
        grid.Controls.Add(_portBox, 3, 1);
        grid.Controls.Add(_pinCheck, 0, 2);
        grid.Controls.Add(_pinBox, 1, 2);
        grid.Controls.Add(_startupCheck, 2, 2);
        grid.SetColumnSpan(_startupCheck, 2);
        root.Controls.Add(settings, 0, 4);

        var logPanel = new Panel { Dock = DockStyle.Fill };
        var logButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
        };
        var copyLog = new Button { Text = "Copy log", AutoSize = true };
        var openFolder = new Button { Text = "Open engine folder", AutoSize = true };
        var about = new Button { Text = "About", AutoSize = true };
        logButtons.Controls.AddRange(new Control[] { copyLog, openFolder, about });
        logPanel.Controls.Add(_logBox);
        logPanel.Controls.Add(logButtons);
        root.Controls.Add(logPanel, 0, 5);

        _resolutionBox.Items.AddRange(UxPlayArguments.Resolutions);
        _latencyBox.Items.AddRange(new object[] { "Smooth (recommended)", "Low latency" });

        _tray = new NotifyIcon
        {
            Text = "AirGlass",
            Icon = appIcon,
            Visible = false,
            ContextMenuStrip = BuildTrayMenu(),
        };
        _tray.DoubleClick += (_, _) => RestoreFromTray();

        _startButton.Click += (_, _) => StartMirroring();
        _stopButton.Click += (_, _) => StopMirroring();
        _downloadButton.Click += async (_, _) => await DownloadEngineAsync();
        _linkButton.Click += (_, _) => { using var f = new LinkForm(new AirGlass.Link.NoCertificateProvider()); f.ShowDialog(this); };
        copyLog.Click += (_, _) =>
        {
            if (_logBox.TextLength > 0) Clipboard.SetText(_logBox.Text);
        };
        openFolder.Click += (_, _) =>
        {
            var dir = EngineManager.DefaultEngineDirectory;
            if (Directory.Exists(dir))
                System.Diagnostics.Process.Start("explorer.exe", dir);
            else
                MessageBox.Show("The engine is not installed yet.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        about.Click += (_, _) => ShowAbout();

        _engine.LogLine += line => BeginInvoke(() => HandleEngineLine(line));
        _engine.Exited += () => BeginInvoke(OnEngineExited);
        _watchdog.Tick += (_, _) => RefreshButtons();
        _watchdog.Start();

        LoadSettingsIntoUi();
        SetStatus(MirrorStatus.Stopped);
        RefreshButtons();

        if (!_engine.EngineInstalled)
            AppendLog("Mirroring engine not installed yet. Click \"Download engine\" (one-time, about 80 MB).");
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open AirGlass", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            _reallyClose = true;
            Close();
        });
        return menu;
    }

    private void LoadSettingsIntoUi()
    {
        _nameBox.Text = _settings.DeviceName;
        _resolutionBox.SelectedItem = _settings.Resolution;
        if (_resolutionBox.SelectedIndex < 0) _resolutionBox.SelectedItem = "1920x1080";
        _latencyBox.SelectedIndex = _settings.LowLatency ? 1 : 0;
        _pinCheck.Checked = _settings.UsePin;
        _pinBox.Text = _settings.Pin;
        _portBox.Value = Math.Clamp(_settings.BasePort, _portBox.Minimum, _portBox.Maximum);
        _startupCheck.Checked = _settings.StartWithWindows;
    }

    private bool SaveUiToSettings()
    {
        var candidate = new AppSettings
        {
            DeviceName = _nameBox.Text,
            Resolution = _resolutionBox.SelectedItem?.ToString() ?? "1920x1080",
            LowLatency = _latencyBox.SelectedIndex == 1,
            UsePin = _pinCheck.Checked,
            Pin = _pinBox.Text.Trim(),
            BasePort = (int)_portBox.Value,
            StartWithWindows = _startupCheck.Checked,
            FirewallTipShown = _settings.FirewallTipShown,
        };
        try
        {
            UxPlayArguments.Build(candidate); // validation
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        _settings = candidate;
        _settings.Save();
        try
        {
            StartupRegistration.SetEnabled(_settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            AppendLog("Could not update the Windows startup entry: " + ex.Message);
        }
        return true;
    }

    private void StartMirroring()
    {
        if (_engine.IsRunning) return;
        if (!_engine.EngineInstalled)
        {
            AppendLog("Engine missing - starting the one-time download.");
            _ = DownloadEngineAsync(thenStart: true);
            return;
        }
        if (!SaveUiToSettings()) return;

        if (!_settings.FirewallTipShown)
        {
            _settings.FirewallTipShown = true;
            _settings.Save();
            MessageBox.Show(
                "The first time the engine runs, Windows may ask whether to allow it through the firewall. " +
                "Allow it on your private (home) network, or your iPhone will not find this PC.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        try
        {
            _parser.Reset();
            _engine.Start(_settings);
            AppendLog("uxplay.exe " + UxPlayArguments.ToCommandLine(UxPlayArguments.Build(_settings)));
            SetStatus(MirrorStatus.Starting);
        }
        catch (Exception ex)
        {
            AppendLog("Failed to start the engine: " + ex.Message);
            MessageBox.Show("Could not start the engine: " + ex.Message,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        RefreshButtons();
    }

    private void StopMirroring()
    {
        _engine.Stop();
        SetStatus(MirrorStatus.Stopped);
        AppendLog("Mirroring stopped.");
        RefreshButtons();
    }

    private async Task DownloadEngineAsync(bool thenStart = false)
    {
        if (_downloading) return;
        _downloading = true;
        _progress.Visible = true;
        _progressLabel.Visible = true;
        RefreshButtons();
        var progress = new Progress<(int Percent, string Message)>(p =>
        {
            _progress.Value = Math.Clamp(p.Percent, 0, 100);
            _progressLabel.Text = p.Message;
        });
        try
        {
            await _installer.DownloadAndInstallAsync(progress, CancellationToken.None);
            AppendLog("Engine installed to " + EngineManager.DefaultEngineDirectory);
            if (thenStart) StartMirroring();
        }
        catch (Exception ex)
        {
            AppendLog("Engine download failed: " + ex.Message);
            MessageBox.Show(
                "The engine could not be downloaded.\n\n" + ex.Message +
                "\n\nCheck your internet connection and try again.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _downloading = false;
            _progress.Visible = false;
            _progressLabel.Visible = false;
            RefreshButtons();
        }
    }

    private void HandleEngineLine(string line)
    {
        AppendLog(line);
        if (_parser.Feed(line))
            SetStatus(_parser.Status);
    }

    private void OnEngineExited()
    {
        if (_parser.Status != MirrorStatus.Stopped)
        {
            AppendLog("Engine exited.");
            SetStatus(MirrorStatus.Stopped);
        }
        RefreshButtons();
    }

    private void SetStatus(MirrorStatus status)
    {
        _statusLabel.Text = status switch
        {
            MirrorStatus.Stopped => "Stopped",
            MirrorStatus.Starting => "Starting...",
            MirrorStatus.Waiting => "Waiting for iPhone",
            MirrorStatus.Connected => "Connected",
            _ => "?",
        };
        _detailLabel.Text = status switch
        {
            MirrorStatus.Stopped => "Mirroring is off.",
            MirrorStatus.Starting => "Starting the engine...",
            MirrorStatus.Waiting =>
                $"On your iPhone: Control Centre > Screen Mirroring > choose \"{_settings.DeviceName}\". " +
                "Both devices must be on the same Wi-Fi.",
            MirrorStatus.Connected =>
                "Showing " + (_parser.ClientName ?? _parser.ClientAddress ?? "your iPhone") + ".",
            _ => "",
        };
        _statusLabel.ForeColor = status switch
        {
            MirrorStatus.Connected => Color.ForestGreen,
            MirrorStatus.Waiting => Color.DarkOrange,
            _ => Color.Black,
        };
    }

    private void RefreshButtons()
    {
        var running = _engine.IsRunning;
        _startButton.Enabled = !_downloading && !running;
        _stopButton.Enabled = running;
        _downloadButton.Enabled = !_downloading && !running;
        _downloadButton.Text = _engine.EngineInstalled ? "Reinstall engine" : "Download engine";
    }

    private void AppendLog(string line)
    {
        if (_logBox.TextLength > 200_000)
            _logBox.Text = _logBox.Text[^100_000..];
        _logBox.AppendText(line + Environment.NewLine);
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            "AirGlass 1.0.0\n\n" +
            "View your iPhone or iPad screen on a Windows PC over AirPlay.\n\n" +
            "The mirroring engine is UxPlay (GPLv3), built unmodified from source by " +
            "this project's own GitHub Actions pipeline; AirGlass downloads it separately " +
            "on first run. AirGlass itself is MIT-licensed.\n\n" +
            "github.com/mjanthony819/airglass",
            "About AirGlass", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        _tray.Visible = false;
        Activate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
        {
            Hide();
            _tray.Visible = true;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_reallyClose && e.CloseReason == CloseReason.UserClosing)
        {
            // Closing to the tray keeps the receiver running; Exit quits fully.
            e.Cancel = true;
            Hide();
            _tray.Visible = true;
            _tray.ShowBalloonTip(3000, "AirGlass",
                "AirGlass is still running in the tray. Right-click the icon to exit.",
                ToolTipIcon.Info);
            return;
        }
        _watchdog.Stop();
        _engine.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _engine.Dispose();
        base.OnFormClosing(e);
    }
}
