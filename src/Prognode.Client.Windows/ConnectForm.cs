using System.Net;

namespace Prognode.Client.Windows;

/// <summary>
/// Pairs this PC with PROGNODE Core on the plant network: find Core, compare the check code with
/// the Core screen (Settings › Mobile Access › Pair by code), then enter the 6-digit pairing code.
/// Nothing but a TLS handshake and the public identity request happens before the code matches.
/// </summary>
public sealed class ConnectForm : Form
{
    private readonly ListBox _servers = new() { Height = 96, BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Width = 560 };
    private readonly TextBox _host = Theme.Input("Core IP address, e.g. 192.168.1.52");
    private readonly TextBox _port = Theme.Input("HTTPS port");
    private readonly Label _code = Theme.Label("", Theme.Code, Theme.Accent);
    private readonly CheckBox _matches = new() { Text = "The code above is the same as on the PROGNODE Core screen", AutoSize = true, ForeColor = Theme.Text };
    private readonly TextBox _pairingCode = Theme.Input("6-digit pairing code");
    private readonly TextBox _name = Theme.Input("Name of this PC");
    private readonly Button _search = Theme.Secondary("Search the network");
    private readonly Button _check = Theme.Secondary("Show check code");
    private readonly Button _pair = Theme.Primary("Pair this PC");
    private readonly Label _status = Theme.Label("", null, Theme.Muted);
    private readonly Panel _verifyStep = new() { AutoSize = true, Visible = false };
    private CoreCandidate? _candidate;

    public PairingRecord? Result { get; private set; }

    public ConnectForm()
    {
        Theme.Apply(this);
        Text = "Connect to PROGNODE Core";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(26);
        _port.Text = "5443";
        _port.Width = 90;
        _name.Text = Environment.MachineName;
        _pairingCode.MaxLength = 6;

        var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        flow.Controls.Add(Theme.Label("Connect to PROGNODE Core", Theme.Title));
        flow.Controls.Add(Theme.Label("This PC receives alarm notifications from your PROGNODE Core over the plant network. On the Core PC, open Settings › Mobile Access › Pair by code.", null, Theme.Muted));
        flow.Controls.Add(Spacer(10));
        flow.Controls.Add(Theme.Label("1  Find your Core", Theme.Heading));
        flow.Controls.Add(Row(_search, Theme.Label("or enter its address:", null, Theme.Muted), _host, _port));
        flow.Controls.Add(_servers);
        flow.Controls.Add(Row(_check));

        var verify = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        verify.Controls.Add(Spacer(8));
        verify.Controls.Add(Theme.Label("2  Compare the check code", Theme.Heading));
        verify.Controls.Add(Theme.Label("Continue only if PROGNODE Core shows exactly this code. It proves you are connected to your own Core.", null, Theme.Muted));
        verify.Controls.Add(_code);
        verify.Controls.Add(_matches);
        verify.Controls.Add(Spacer(8));
        verify.Controls.Add(Theme.Label("3  Pair", Theme.Heading));
        verify.Controls.Add(Row(_pairingCode, _name));
        verify.Controls.Add(Row(_pair));
        _verifyStep.Controls.Add(verify);
        flow.Controls.Add(_verifyStep);
        flow.Controls.Add(_status);
        Controls.Add(flow);

        _search.Click += async (_, _) => await SearchAsync();
        _check.Click += async (_, _) => await CheckAsync();
        _pair.Click += async (_, _) => await PairAsync();
        _servers.SelectedIndexChanged += (_, _) => { if (_servers.SelectedItem is DiscoveredCore d) { _host.Text = d.Address.ToString(); _port.Text = d.SecurePort.ToString(); } };
        _host.TextChanged += (_, _) => ResetVerification();
        _port.TextChanged += (_, _) => ResetVerification();
        _matches.CheckedChanged += (_, _) => _pair.Enabled = _matches.Checked;
        _pair.Enabled = false;
        Shown += async (_, _) => await SearchAsync();
    }

    private static Control Spacer(int height) => new Panel { Height = height, Width = 1 };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 4) };
        foreach (var c in controls) { c.Margin = new Padding(0, 0, 10, 0); row.Controls.Add(c); }
        return row;
    }

    private void ResetVerification()
    {
        _candidate = null;
        _verifyStep.Visible = false;
        _matches.Checked = false;
    }

    private async Task SearchAsync()
    {
        _search.Enabled = false;
        _status.Text = "Searching the plant network…";
        try
        {
            var found = await CoreConnection.DiscoverAsync();
            _servers.Items.Clear();
            foreach (var core in found) _servers.Items.Add(core);
            if (found.Count > 0) _servers.SelectedIndex = 0;
            _status.Text = found.Count == 0
                ? "No Core answered. Enter its IP address, or ask IT to allow UDP 5081 on the network."
                : "Select your Core and click Show check code.";
        }
        catch (Exception ex) { _status.Text = $"Search failed: {ex.Message}"; }
        finally { _search.Enabled = true; }
    }

    private async Task CheckAsync()
    {
        ResetVerification();
        var host = _host.Text.Trim();
        if (!IPAddress.TryParse(host, out _) && Uri.CheckHostName(host) == UriHostNameType.Unknown) { _status.Text = "Enter a valid Core address."; return; }
        if (!int.TryParse(_port.Text, out var port) || port is < 1 or > 65535) { _status.Text = "Enter a valid HTTPS port (default 5443)."; return; }
        _check.Enabled = false;
        _status.Text = "Checking the Core certificate…";
        try
        {
            var expected = (_servers.SelectedItem as DiscoveredCore) is { } d && d.Address.ToString() == host ? d.ServerId : (Guid?)null;
            _candidate = await CoreConnection.InspectAsync(host, port, expected);
            _code.Text = _candidate.CheckCode;
            _verifyStep.Visible = true;
            _status.Text = $"Connected to {_candidate.DisplayName}. Compare the code, then enter the pairing code.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Secure connection failed: {ex.Message} — Is LAN access enabled in Core Settings › Mobile LAN access?";
        }
        finally { _check.Enabled = true; }
    }

    private async Task PairAsync()
    {
        if (_candidate is null || !_matches.Checked) return;
        if (_pairingCode.Text.Trim().Length != 6 || !_pairingCode.Text.Trim().All(char.IsDigit)) { _status.Text = "Enter the 6-digit pairing code from the Core screen."; return; }
        _pair.Enabled = false;
        _status.Text = "Pairing…";
        try
        {
            using var core = new CoreConnection(_candidate.Host, _candidate.Port, _candidate.CertificateSha256);
            var name = string.IsNullOrWhiteSpace(_name.Text) ? Environment.MachineName : _name.Text.Trim();
            var (clientId, token) = await core.PairAsync(_candidate.ServerId, name, _pairingCode.Text);
            Result = new PairingRecord(_candidate.ServerId, _candidate.DisplayName, _candidate.Host, _candidate.Port, _candidate.CertificateSha256, clientId, token);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            _pair.Enabled = _matches.Checked;
        }
    }
}
