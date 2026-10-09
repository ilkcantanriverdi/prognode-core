using Prognode.Contracts.Alarms;

namespace Prognode.Client.Windows;

/// <summary>Active alarms from PROGNODE Core with acknowledgement. Closing the window keeps the client in the tray.</summary>
public sealed class MainForm : Form
{
    private readonly ListView _alarms = new()
    {
        View = View.Details, FullRowSelect = true, MultiSelect = false, Dock = DockStyle.Fill, HideSelection = false,
        BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, HeaderStyle = ColumnHeaderStyle.Nonclickable,
    };
    private readonly Label _server = Theme.Label("", Theme.Title);
    private readonly Label _state = Theme.Label("", null, Theme.Muted);
    private readonly Label _permission = Theme.Label("", null, Theme.Muted);
    private readonly Button _ack = Theme.Primary("ACK selected alarm");
    private readonly CheckBox _autostart = new() { Text = "Start with Windows", AutoSize = true, ForeColor = Theme.Text };
    private readonly Button _disconnect = Theme.Secondary("Disconnect this PC");

    public event Func<Guid, Task>? AckRequested;
    public event Action? DisconnectRequested;

    public MainForm()
    {
        Theme.Apply(this);
        Text = "PROGNODE";
        MinimumSize = new Size(760, 460);
        Size = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(20);

        _alarms.Columns.Add("Priority", 90);
        _alarms.Columns.Add("Alarm", 320);
        _alarms.Columns.Add("Source", 170);
        _alarms.Columns.Add("Since", 140);
        _alarms.Columns.Add("State", 110);

        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        header.Controls.Add(_server);
        header.Controls.Add(_state);
        header.Controls.Add(_permission);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 12, 0, 0) };
        foreach (var c in new Control[] { _ack, _autostart, _disconnect }) { c.Margin = new Padding(0, 4, 16, 0); footer.Controls.Add(c); }

        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 0) };
        listHost.Controls.Add(_alarms);
        Controls.Add(listHost);
        Controls.Add(footer);
        Controls.Add(header);

        _autostart.Checked = ClientStore.StartsWithWindows;
        _autostart.CheckedChanged += (_, _) => ClientStore.StartsWithWindows = _autostart.Checked;
        _ack.Click += async (_, _) => { if (_alarms.SelectedItems.Count == 1 && _alarms.SelectedItems[0].Tag is Guid id && AckRequested is not null) await AckRequested(id); };
        _alarms.SelectedIndexChanged += (_, _) => UpdateAckButton();
        _disconnect.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "This PC stops receiving PROGNODE alarms. Pairing again needs a new code from the Core screen.", "Disconnect this PC?", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
                DisconnectRequested?.Invoke();
        };
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
    }

    private bool _canAck;

    public void ShowServer(string name, string host) { _server.Text = name; Text = $"PROGNODE · {name}"; _state.Text = $"Plant network · {host}"; }

    public void ShowConnection(bool online, string message)
    {
        _state.Text = message;
        _state.ForeColor = online ? Theme.Accent : Theme.Warning;
    }

    public void ShowAccess(DeviceAccess? access)
    {
        _canAck = access?.DeviceAck == true;
        _permission.Text = _canAck
            ? "This PC may acknowledge alarms."
            : "View only. A Core administrator can allow acknowledgement in Settings › Mobile Access › Paired devices.";
        UpdateAckButton();
    }

    public void ShowAlarms(IReadOnlyList<AlarmRuntimeSnapshot> alarms)
    {
        var selected = _alarms.SelectedItems.Count == 1 ? _alarms.SelectedItems[0].Tag as Guid? : null;
        _alarms.BeginUpdate();
        _alarms.Items.Clear();
        foreach (var a in alarms.OrderBy(x => x.Priority switch { AlarmPriority.Critical => 0, AlarmPriority.High => 1, AlarmPriority.Medium => 2, _ => 3 }).ThenByDescending(x => x.ActiveSince))
        {
            var item = new ListViewItem(a.Priority.ToString()) { Tag = a.OccurrenceId, UseItemStyleForSubItems = true };
            item.SubItems.Add(a.Text);
            item.SubItems.Add(a.SourceName);
            item.SubItems.Add(a.ActiveSince.ToLocalTime().ToString("dd.MM HH:mm:ss"));
            item.SubItems.Add(a.AcknowledgedAt is null ? "Awaiting ACK" : "Acknowledged");
            item.ForeColor = a.Priority switch { AlarmPriority.Critical => Theme.Critical, AlarmPriority.High => Theme.Warning, _ => Theme.Text };
            _alarms.Items.Add(item);
            if (selected == a.OccurrenceId) item.Selected = true;
        }
        if (_alarms.Items.Count == 0)
            _alarms.Items.Add(new ListViewItem("") { SubItems = { "No active alarms", "", "", "" }, ForeColor = Theme.Muted });
        _alarms.EndUpdate();
        UpdateAckButton();
    }

    public void SelectOccurrence(Guid occurrenceId)
    {
        foreach (ListViewItem item in _alarms.Items)
            item.Selected = item.Tag is Guid id && id == occurrenceId;
    }

    private void UpdateAckButton()
    {
        var item = _alarms.SelectedItems.Count == 1 ? _alarms.SelectedItems[0] : null;
        _ack.Enabled = _canAck && item?.Tag is Guid && item.SubItems[4].Text == "Awaiting ACK";
    }
}
