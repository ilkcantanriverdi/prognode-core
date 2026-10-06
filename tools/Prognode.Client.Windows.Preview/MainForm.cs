using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Prognode.Client.Windows.Preview;

public sealed class MainForm : Form
{
    private const int DiscoveryPort = 5081;
    private const string DiscoveryRequest = "PROGNODE_DISCOVER_V1";
    private readonly ListBox _servers = new() { Dock = DockStyle.Fill };
    private readonly Button _discover = new() { Text = "Discover PROGNODE Servers", AutoSize = true };
    private readonly TextBox _pairingCode = new() { PlaceholderText = "6-digit pairing code", MaxLength = 6, Width = 190 };
    private readonly TextBox _clientName = new() { Width = 220, Text = $"{Environment.MachineName} Client" };
    private readonly Button _pair = new() { Text = "Pair", AutoSize = true, Enabled = false };
    private readonly Button _readOverview = new() { Text = "Read Overview", AutoSize = true, Enabled = false };
    private readonly TextBox _output = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly Label _status = new() { AutoSize = true, Text = "Not paired" };
    private readonly List<DiscoveredServer> _discovered = [];
    private string? _accessToken;
    private DiscoveredServer? _pairedServer;

    public MainForm()
    {
        Text = "PROGNODE Client — Connectivity Preview";
        Width = 940;
        Height = 680;
        MinimumSize = new Size(760, 540);
        StartPosition = FormStartPosition.CenterScreen;

        var title = new Label
        {
            Text = "PROGNODE Client",
            Font = new Font(Font.FontFamily, 20, FontStyle.Bold),
            AutoSize = true
        };
        var subtitle = new Label
        {
            Text = "LAN discovery + one-time pairing validation",
            AutoSize = true,
            ForeColor = SystemColors.GrayText
        };

        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        var pairRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 8, 0, 8) };
        pairRow.Controls.Add(new Label { Text = "Client name", AutoSize = true, Padding = new Padding(0, 7, 6, 0) });
        pairRow.Controls.Add(_clientName);
        pairRow.Controls.Add(new Label { Text = "Pairing code", AutoSize = true, Padding = new Padding(14, 7, 6, 0) });
        pairRow.Controls.Add(_pairingCode);
        pairRow.Controls.Add(_pair);
        pairRow.Controls.Add(_readOverview);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(18),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        table.Controls.Add(header, 0, 0);
        table.SetColumnSpan(header, 2);
        table.Controls.Add(_discover, 0, 1);
        table.Controls.Add(pairRow, 1, 1);
        table.Controls.Add(_servers, 0, 2);
        table.Controls.Add(_output, 1, 2);
        table.Controls.Add(_status, 0, 3);
        table.SetColumnSpan(_status, 2);
        Controls.Add(table);

        _discover.Click += async (_, _) => await DiscoverAsync();
        _servers.SelectedIndexChanged += (_, _) => _pair.Enabled = _servers.SelectedIndex >= 0;
        _pair.Click += async (_, _) => await PairAsync();
        _readOverview.Click += async (_, _) => await ReadOverviewAsync();
    }

    private async Task DiscoverAsync()
    {
        try
        {
            _discover.Enabled = false;
            _servers.Items.Clear();
            _discovered.Clear();
            SetStatus("Discovering on UDP 5081…");

            using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            var bytes = Encoding.UTF8.GetBytes(DiscoveryRequest);
            await udp.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var result = await udp.ReceiveAsync(cts.Token);
                    using var doc = JsonDocument.Parse(result.Buffer);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("type", out var type) || type.GetString() != "PROGNODE_SERVER_V1") continue;

                    var server = new DiscoveredServer(
                        root.GetProperty("serverId").GetGuid(),
                        root.GetProperty("displayName").GetString() ?? "PROGNODE Server",
                        root.GetProperty("apiVersion").GetString() ?? "v1",
                        root.GetProperty("apiPort").GetInt32(),
                        result.RemoteEndPoint.Address);

                    if (_discovered.Any(x => x.ServerId == server.ServerId)) continue;
                    _discovered.Add(server);
                    _servers.Items.Add(server);
                }
                catch (OperationCanceledException) { break; }
            }

            SetStatus(_discovered.Count == 0
                ? "No PROGNODE Server found. Check LAN/firewall and that Core is running."
                : $"Found {_discovered.Count} PROGNODE Server(s). Select one and enter the pairing code shown in Core Settings.");
        }
        catch (Exception ex) { SetStatus($"Discovery failed: {ex.Message}"); }
        finally { _discover.Enabled = true; }
    }

    private async Task PairAsync()
    {
        if (_servers.SelectedIndex < 0 || _servers.SelectedIndex >= _discovered.Count) return;
        var server = _discovered[_servers.SelectedIndex];
        var code = _pairingCode.Text.Trim();
        if (code.Length != 6) { SetStatus("Enter the 6-digit pairing code from Core > Settings."); return; }

        try
        {
            _pair.Enabled = false;
            using var client = new HttpClient { BaseAddress = server.BaseUri, Timeout = TimeSpan.FromSeconds(8) };
            var devicePublicKey = DeviceIdentity.GetOrCreatePublicKey();
            var payload = JsonSerializer.Serialize(new
            {
                serverId = server.ServerId,
                clientName = _clientName.Text.Trim(),
                pairingCode = code,
                platform = "Windows",
                devicePublicKey
            });
            using var response = await client.PostAsync("/api/client/pair", new StringContent(payload, Encoding.UTF8, "application/json"));
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ReadMessage(json) ?? $"Pairing failed ({(int)response.StatusCode}).");

            using var doc = JsonDocument.Parse(json);
            _accessToken = doc.RootElement.GetProperty("accessToken").GetString();
            _pairedServer = server;
            _readOverview.Enabled = !string.IsNullOrWhiteSpace(_accessToken);
            _output.Text = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            SetStatus($"Paired with {server.DisplayName} ({server.Address}). Token is kept in memory only in this preview.");
        }
        catch (Exception ex) { SetStatus(ex.Message); }
        finally { _pair.Enabled = true; }
    }

    private async Task ReadOverviewAsync()
    {
        if (_pairedServer is null || string.IsNullOrWhiteSpace(_accessToken)) return;
        try
        {
            using var client = new HttpClient { BaseAddress = _pairedServer.BaseUri, Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            var response = await client.GetAsync("/api/overview");
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ReadMessage(json) ?? $"Overview request failed ({(int)response.StatusCode}).");
            using var doc = JsonDocument.Parse(json);
            _output.Text = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            SetStatus("Paired API read passed. LAN discovery + pairing + Bearer access are working end-to-end.");
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private void SetStatus(string text) => _status.Text = text;

    private static string? ReadMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("message", out var message) ? message.GetString() : null;
        }
        catch { return null; }
    }

    private static class DeviceIdentity
    {
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PROGNODE", "ClientPreview");
        private static readonly string PrivateKeyPath = Path.Combine(Folder, "device-key.pk8");

        public static string GetOrCreatePublicKey()
        {
            Directory.CreateDirectory(Folder);
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            if (File.Exists(PrivateKeyPath))
            {
                var privateKey = File.ReadAllBytes(PrivateKeyPath);
                ecdsa.ImportPkcs8PrivateKey(privateKey, out _);
            }
            else
            {
                File.WriteAllBytes(PrivateKeyPath, ecdsa.ExportPkcs8PrivateKey());
            }

            return Base64Url(ecdsa.ExportSubjectPublicKeyInfo());
        }

        private static string Base64Url(byte[] value) =>
            Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed record DiscoveredServer(Guid ServerId, string DisplayName, string ApiVersion, int ApiPort, IPAddress Address)
    {
        public Uri BaseUri => new($"http://{Address}:{ApiPort}");
        public override string ToString() => $"{DisplayName}  •  {Address}:{ApiPort}  •  {ApiVersion}";
    }
}
