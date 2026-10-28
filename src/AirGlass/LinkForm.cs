using AirGlass.Core.Link;
using AirGlass.Link;

namespace AirGlass;

/// <summary>Controls for iPhone Link: start/stop, pairing code, approval, kill switch, folders.</summary>
public sealed class LinkForm : Form
{
    private readonly ICertificateProvider _certs;
    private readonly PairingService _pairing = new();
    private readonly WindowsInput _input;
    private LinkServer? _server;
    private readonly string _inbox = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AirGlass", "Received");
    private readonly string _outbox = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AirGlass", "Shared");

    private readonly Label _state = new() { AutoSize = true, Font = new Font("Segoe UI", 13f, FontStyle.Bold), Text = "Link is off" };
    private readonly Label _cert = new() { AutoSize = false, Width = 500, Height = 44, ForeColor = Color.DimGray };
    private readonly Button _toggle = new() { Text = "Start Link", AutoSize = true };
    private readonly Label _url = new() { AutoSize = true, Font = new Font("Segoe UI", 10f), Text = "" };
    private readonly Label _code = new() { AutoSize = true, Font = new Font("Consolas", 28f, FontStyle.Bold), Text = "------" };
    private readonly Button _newCode = new() { Text = "New code", AutoSize = true, Enabled = false };
    private readonly ListBox _pending = new() { Width = 300, Height = 60 };
    private readonly Button _approve = new() { Text = "Approve", AutoSize = true, Enabled = false };
    private readonly Button _deny = new() { Text = "Deny", AutoSize = true, Enabled = false };
    private readonly CheckBox _pause = new() { Text = "Pause phone input (kill switch)", AutoSize = true };
    private readonly Button _revoke = new() { Text = "Disconnect all phones", AutoSize = true };
    private readonly Label _paired = new() { AutoSize = true, Text = "Paired phones: 0" };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 110, Width = 500, Font = new Font("Consolas", 9f) };
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    private readonly List<PendingDevice> _pendingItems = new();

    public LinkForm(ICertificateProvider certs)
    {
        _certs = certs;
        _input = new WindowsInput(this);
        Text = "AirGlass Link - iPhone as trackpad, keyboard and file drop";
        ClientSize = new Size(540, 560);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12), AutoScroll = true };
        Controls.Add(p);
        p.Controls.Add(_state);
        p.Controls.Add(_cert);
        p.Controls.Add(_toggle);
        p.Controls.Add(new Label { AutoSize = true, Text = "On your iPhone open this address in Safari:", Margin = new Padding(3, 12, 3, 0) });
        p.Controls.Add(_url);
        p.Controls.Add(new Label { AutoSize = true, Text = "Pairing code (valid 2 minutes, single use):" });
        p.Controls.Add(_code);
        p.Controls.Add(_newCode);
        p.Controls.Add(new Label { AutoSize = true, Text = "Phones asking to pair (only approve one you recognise):", Margin = new Padding(3, 12, 3, 0) });
        var pr = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        pr.Controls.AddRange(new Control[] { _pending, _approve, _deny });
        p.Controls.Add(pr);
        p.Controls.Add(_paired);
        p.Controls.Add(_pause);
        p.Controls.Add(_revoke);
        var fr = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var openIn = new Button { Text = "Open Received folder", AutoSize = true };
        var openOut = new Button { Text = "Open Shared folder (files phone can fetch)", AutoSize = true };
        fr.Controls.AddRange(new Control[] { openIn, openOut });
        p.Controls.Add(fr);
        p.Controls.Add(_log);

        _toggle.Click += async (_, _) => await ToggleAsync();
        _newCode.Click += (_, _) => _code.Text = _pairing.NewCode();
        _approve.Click += (_, _) => Decide(true);
        _deny.Click += (_, _) => Decide(false);
        _pending.SelectedIndexChanged += (_, _) => _approve.Enabled = _deny.Enabled = _pending.SelectedIndex >= 0;
        _pause.CheckedChanged += (_, _) => { if (_server is not null) _server.InputEnabled = !_pause.Checked; };
        _revoke.Click += (_, _) => { _pairing.RevokeAll(); _pendingItems.Clear(); _pending.Items.Clear(); Log("All phones disconnected; tokens revoked."); };
        openIn.Click += (_, _) => { Directory.CreateDirectory(_inbox); System.Diagnostics.Process.Start("explorer.exe", _inbox); };
        openOut.Click += (_, _) => { Directory.CreateDirectory(_outbox); System.Diagnostics.Process.Start("explorer.exe", _outbox); };
        _tick.Tick += (_, _) => { _paired.Text = $"Paired phones: {_pairing.PairedCount}"; };
        _tick.Start();
        FormClosing += async (_, _) => await StopAsync();
        _cert.Text = _certs.Description;
    }

    private void Log(string s) => _log.AppendText($"{DateTime.Now:HH:mm:ss}  {s}{Environment.NewLine}");

    private async Task ToggleAsync()
    {
        if (_server is not null) { await StopAsync(); return; }
        var cert = _certs.TryGetCertificate(out var problem);
        if (cert is null)
        {
            _cert.Text = problem ?? _certs.Description;
            MessageBox.Show(this, problem ?? "No certificate available.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            var s = new LinkServer(_pairing, _input, _input, _inbox, _outbox) { InputEnabled = !_pause.Checked };
            s.Log += l => BeginInvoke(() => Log(l));
            s.PairRequested += d => BeginInvoke(() =>
            {
                _pendingItems.Add(d);
                _pending.Items.Add($"{d.DeviceName}  ({d.Id[..4]})");
                Log($"Pairing request from \"{d.DeviceName}\". Approve only if you just entered the code on your phone.");
            });
            await s.StartAsync(cert, 47800, _certs.BindAddress);
            _server = s;
            _state.Text = "Link is on";
            _toggle.Text = "Stop Link";
            _newCode.Enabled = true;
            _code.Text = _pairing.NewCode();
            _url.Text = $"https://{_certs.Host ?? "(unknown host)"}:{s.Port}/";
            Log("Started. Windows may ask to allow AirGlass through the firewall: allow Private networks only.");
        }
        catch (Exception ex)
        {
            Log("Could not start: " + ex.Message);
        }
    }

    private async Task StopAsync()
    {
        var s = _server;
        _server = null;
        if (s is not null) await s.DisposeAsync();
        _pairing.CancelCode();
        _state.Text = "Link is off";
        _toggle.Text = "Start Link";
        _newCode.Enabled = false;
        _code.Text = "------";
        _url.Text = "";
    }

    private void Decide(bool ok)
    {
        var i = _pending.SelectedIndex;
        if (i < 0 || i >= _pendingItems.Count) return;
        var d = _pendingItems[i];
        if (ok) _pairing.Approve(d.Id); else _pairing.Deny(d.Id);
        Log(ok ? $"Approved \"{d.DeviceName}\"." : $"Denied \"{d.DeviceName}\".");
        _pendingItems.RemoveAt(i);
        _pending.Items.RemoveAt(i);
    }
}
