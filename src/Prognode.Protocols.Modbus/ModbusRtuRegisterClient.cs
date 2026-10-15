using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Ports;
using System.Text.RegularExpressions;

namespace Prognode.Protocols.Modbus;

/// <summary>Reads Modbus registers and bits; implemented over TCP and over a serial RS-485 line.</summary>
public interface IModbusRegisterClient
{
    Task<bool[]> ReadCoilsAsync(string host, int port, int unitId, int startAddress, int quantity, int timeoutMs, CancellationToken cancellationToken);
    Task<bool[]> ReadDiscreteInputsAsync(string host, int port, int unitId, int startAddress, int quantity, int timeoutMs, CancellationToken cancellationToken);
    Task<ushort[]> ReadInputRegistersAsync(string host, int port, int unitId, int startAddress, int quantity, int timeoutMs, CancellationToken cancellationToken);
    Task<ushort[]> ReadHoldingRegistersAsync(string host, int port, int unitId, int startAddress, int quantity, int timeoutMs, CancellationToken cancellationToken);
}

/// <summary>
/// A serial line as stored in a Modbus RTU device's Host: <c>COM3;9600;8E1</c> (port, baud rate,
/// data bits, parity N/E/O, stop bits).
/// </summary>
public sealed record ModbusSerialSettings(string PortName, int BaudRate, int DataBits, Parity Parity, StopBits StopBits)
{
    public static readonly int[] BaudRates = [1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200];
    private static readonly Regex Format = new(@"^(COM\d{1,3}|/dev/[A-Za-z0-9_]+);(\d{3,6});([78])([NEO])([12])$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static ModbusSerialSettings Parse(string? text)
    {
        var match = Format.Match((text ?? "").Trim());
        if (!match.Success)
            throw new ArgumentException("Modbus RTU serial settings must look like COM3;9600;8E1 (port, baud rate, data bits, parity N/E/O, stop bits).");
        var baud = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        if (!BaudRates.Contains(baud))
            throw new ArgumentException($"Baud rate {baud} is not supported. Use {string.Join(", ", BaudRates)}.");
        return new(match.Groups[1].Value.ToUpperInvariant().StartsWith("COM") ? match.Groups[1].Value.ToUpperInvariant() : match.Groups[1].Value,
            baud, match.Groups[3].Value[0] - '0',
            char.ToUpperInvariant(match.Groups[4].Value[0]) switch { 'E' => Parity.Even, 'O' => Parity.Odd, _ => Parity.None },
            match.Groups[5].Value == "2" ? StopBits.Two : StopBits.One);
    }

    public override string ToString() =>
        $"{PortName};{BaudRate};{DataBits}{(Parity == Parity.Even ? 'E' : Parity == Parity.Odd ? 'O' : 'N')}{(StopBits == StopBits.Two ? 2 : 1)}";

    /// <summary>Modbus RTU silent interval between frames: 3.5 characters, at least 1.75 ms above 19200 baud.</summary>
    public TimeSpan FrameGap => BaudRate > 19200
        ? TimeSpan.FromMilliseconds(1.75)
        : TimeSpan.FromMilliseconds(3.5 * (1 + DataBits + (Parity == Parity.None ? 0 : 1) + (StopBits == StopBits.Two ? 2 : 1)) * 1000.0 / BaudRate);
}

/// <summary>An open serial line; replaced by an in-memory device in tests.</summary>
public interface IModbusSerialLine : IDisposable
{
    void Write(byte[] frame);
    /// <summary>Reads exactly <paramref name="count"/> bytes or throws <see cref="TimeoutException"/>.</summary>
    byte[] Read(int count, int timeoutMs);
    void DiscardInput();
}

/// <summary>
/// Modbus RTU master over RS-485 (USB adapter or COM port). Devices on one line share the port, so
/// requests on a line are serialized; each line is opened once and kept open while polled.
/// </summary>
public sealed class ModbusRtuRegisterClient(Func<ModbusSerialSettings, IModbusSerialLine>? openLine = null)
    : IModbusRegisterClient, IDisposable
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);
    private readonly Func<ModbusSerialSettings, IModbusSerialLine> _openLine = openLine ?? (settings => new SerialLine(settings));
    private readonly ConcurrentDictionary<string, Line> _lines = new(StringComparer.OrdinalIgnoreCase);

    public ModbusRtuRegisterClient() : this(null) { }

    public Task<bool[]> ReadCoilsAsync(string host, int port, int unitId, int startAddress, int quantity, int timeoutMs, CancellationToken cancellationToken) =>
        ReadBitsAsync(host, unitId, 0x01, startAddress, quantity, timeoutMs, cancellationToken);

    public Task<bool[]> ReadDiscreteInputsAsync(string host, int port, int unitId, int startAddress, int quantity, int timeoutMs, CancellationToken cancellationToken) =>
        ReadBitsAsync(host, unitId, 0x02, startAddress, quantity, timeoutMs, cancellationToken);

    public Task<ushort[]> ReadInputRegistersAsync(string host, int port, int unitId, int startAddress, int quantity, int timeoutMs, CancellationToken cancellationToken) =>
        ReadRegistersAsync(host, unitId, 0x04, startAddress, quantity, timeoutMs, cancellationToken);

    public Task<ushort[]> ReadHoldingRegistersAsync(string host, int port, int unitId, int startAddress, int quantity, int timeoutMs, CancellationToken cancellationToken) =>
        ReadRegistersAsync(host, unitId, 0x03, startAddress, quantity, timeoutMs, cancellationToken);

    /// <summary>True when this device answered on its line within <paramref name="window"/>.</summary>
    public bool HasRecentSuccess(string host, int unitId, TimeSpan window) =>
        _lines.TryGetValue(Key(host), out var line) && line.LastAnswer.TryGetValue(unitId, out var last) &&
        DateTimeOffset.UtcNow - last <= window;

    private async Task<bool[]> ReadBitsAsync(string host, int unitId, byte function, int start, int quantity, int timeoutMs, CancellationToken ct)
    {
        if (quantity is < 1 or > 2000) throw new ArgumentOutOfRangeException(nameof(quantity));
        var pdu = await ExecuteAsync(host, unitId, function, start, quantity, timeoutMs, ct);
        if (pdu[1] != (quantity + 7) / 8 || pdu.Length != 2 + pdu[1]) throw new IOException("Invalid Modbus RTU bit payload length.");
        var bits = new bool[quantity];
        for (var i = 0; i < quantity; i++) bits[i] = (pdu[2 + i / 8] & (1 << (i % 8))) != 0;
        return bits;
    }

    private async Task<ushort[]> ReadRegistersAsync(string host, int unitId, byte function, int start, int quantity, int timeoutMs, CancellationToken ct)
    {
        if (quantity is < 1 or > 125) throw new ArgumentOutOfRangeException(nameof(quantity));
        var pdu = await ExecuteAsync(host, unitId, function, start, quantity, timeoutMs, ct);
        if (pdu[1] != quantity * 2 || pdu.Length != 2 + pdu[1]) throw new IOException("Invalid Modbus RTU register payload length.");
        var registers = new ushort[quantity];
        for (var i = 0; i < quantity; i++) registers[i] = (ushort)((pdu[2 + i * 2] << 8) | pdu[3 + i * 2]);
        return registers;
    }

    /// <summary>Sends one request and returns the response PDU (function code onwards, without CRC).</summary>
    private async Task<byte[]> ExecuteAsync(string host, int unitId, byte function, int start, int quantity, int timeoutMs, CancellationToken ct)
    {
        if (unitId is < 1 or > 247) throw new ArgumentOutOfRangeException(nameof(unitId), "Modbus RTU unit ID must be 1..247.");
        if (start is < 0 or > 65535 || (long)start + quantity > 65536) throw new ArgumentOutOfRangeException(nameof(start));
        var settings = ModbusSerialSettings.Parse(host);
        CloseIdleLines();
        var line = _lines.GetOrAdd(Key(host), _ => new Line(settings));
        await line.Gate.WaitAsync(ct);
        try
        {
            line.LastUsed = DateTimeOffset.UtcNow;
            var since = DateTimeOffset.UtcNow - line.LastFrame;
            if (since < settings.FrameGap) await Task.Delay(settings.FrameGap - since, ct);
            byte[] request = [(byte)unitId, function, (byte)(start >> 8), (byte)start, (byte)(quantity >> 8), (byte)quantity, 0, 0];
            WriteCrc(request);
            try
            {
                line.Port ??= _openLine(settings);
                var response = await Task.Run(() => Transact(line.Port, request, function, timeoutMs), ct);
                line.LastAnswer[unitId] = DateTimeOffset.UtcNow;
                if ((response[1] & 0x80) != 0) throw new ModbusExceptionResponseException(function, response[2]);
                return response[1..^2];
            }
            catch (TimeoutException ex)
            {
                throw new IOException($"Modbus RTU device {unitId} on {settings.PortName} did not answer within {timeoutMs} ms.", ex);
            }
            catch (IOException ex) when (ex is ModbusExceptionResponseException || ex.Message.StartsWith("Modbus RTU", StringComparison.Ordinal))
            {
                throw; // The device answered with an exception, or a garbled/foreign answer: the line itself is fine.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                // The port itself failed (adapter unplugged, port missing or taken): reopen next time.
                line.Port?.Dispose();
                line.Port = null;
                throw new IOException(ex is UnauthorizedAccessException
                    ? $"{settings.PortName} is in use by another program."
                    : $"{settings.PortName} could not be used: {ex.Message}", ex);
            }
            finally { line.LastFrame = DateTimeOffset.UtcNow; }
        }
        finally { line.Gate.Release(); }
    }

    /// <summary>
    /// Writes the request and reads one complete response frame (with CRC) for it. Some RS-485
    /// adapters echo what they send: an exact copy of the request is skipped. Bytes are only read as
    /// far as a genuine answer is known to reach, so telling the two apart never loses answer bytes.
    /// </summary>
    private static byte[] Transact(IModbusSerialLine port, byte[] request, byte function, int timeoutMs)
    {
        port.DiscardInput();
        port.Write(request);
        var head = port.Read(3, timeoutMs);
        byte[]? frame = null;
        if (head.AsSpan().SequenceEqual(request.AsSpan(0, 3)))
        {
            // Either an echo (5 more request bytes follow) or an answer whose byte count happens to
            // equal the start address high byte (head[2] + 2 more bytes follow).
            var answerRest = head[2] + 2;
            var probe = port.Read(Math.Min(5, answerRest), timeoutMs);
            byte[] candidate = [.. head, .. probe];
            if (probe.Length == answerRest && CrcMatches(candidate))
                frame = candidate;
            else if (probe.AsSpan().SequenceEqual(request.AsSpan(3, probe.Length)))
            {
                if (probe.Length < 5 && !port.Read(5 - probe.Length, timeoutMs).AsSpan().SequenceEqual(request.AsSpan(3 + probe.Length)))
                    throw new IOException("Modbus RTU answer is garbled (partial echo of the request).");
                head = port.Read(3, timeoutMs); // the echo is gone; the answer follows
            }
            else
                frame = probe.Length == answerRest ? candidate : [.. candidate, .. port.Read(answerRest - probe.Length, timeoutMs)];
        }
        if (frame is null)
        {
            // A late answer to an earlier request (another unit, or this unit's previous request that
            // timed out) can still be on the line: drop that frame once and keep waiting for ours.
            if ((head[0] != request[0] || head[1] != function) && head[1] is >= 0x01 and <= 0x04)
            {
                port.Read(head[2] + 2, timeoutMs);
                head = port.Read(3, timeoutMs);
            }
            if (head[0] != request[0]) throw new IOException($"Modbus RTU answer came from unit {head[0]}, not {request[0]}.");
            if (head[1] == (function | 0x80)) frame = [.. head, .. port.Read(2, timeoutMs)];
            else if (head[1] == function) frame = [.. head, .. port.Read(head[2] + 2, timeoutMs)];
            else throw new IOException($"Modbus RTU answer has function 0x{head[1]:X2}, expected 0x{function:X2}.");
        }
        if (!CrcMatches(frame)) throw new IOException("Modbus RTU answer failed the CRC check (noise or wrong serial settings).");
        return frame;
    }

    private static bool CrcMatches(byte[] frame)
    {
        var crc = Crc(frame.AsSpan(0, frame.Length - 2));
        return frame[^2] == (byte)crc && frame[^1] == (byte)(crc >> 8);
    }

    public static ushort Crc(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }

    private static void WriteCrc(byte[] frame)
    {
        var crc = Crc(frame.AsSpan(0, frame.Length - 2));
        frame[^2] = (byte)crc;
        frame[^1] = (byte)(crc >> 8);
    }

    private static string Key(string host) => ModbusSerialSettings.Parse(host).PortName;

    private void CloseIdleLines()
    {
        foreach (var (key, line) in _lines)
            if (DateTimeOffset.UtcNow - line.LastUsed > IdleTimeout && line.Gate.Wait(0))
            {
                try { line.Port?.Dispose(); line.Port = null; _lines.TryRemove(key, out _); }
                finally { line.Gate.Release(); }
            }
    }

    public void Dispose()
    {
        foreach (var (_, line) in _lines) line.Port?.Dispose();
        _lines.Clear();
    }

    private sealed class Line(ModbusSerialSettings settings)
    {
        public ModbusSerialSettings Settings { get; } = settings;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public IModbusSerialLine? Port { get; set; }
        public DateTimeOffset LastUsed { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastFrame { get; set; } = DateTimeOffset.MinValue;
        public ConcurrentDictionary<int, DateTimeOffset> LastAnswer { get; } = new();
    }

    private sealed class SerialLine : IModbusSerialLine
    {
        private readonly SerialPort _port;

        public SerialLine(ModbusSerialSettings settings)
        {
            _port = new SerialPort(settings.PortName, settings.BaudRate, settings.Parity, settings.DataBits, settings.StopBits)
            {
                Handshake = Handshake.None,
                ReadTimeout = 1000,
                WriteTimeout = 1000,
            };
            _port.Open();
        }

        public void Write(byte[] frame) => _port.Write(frame, 0, frame.Length);

        public byte[] Read(int count, int timeoutMs)
        {
            var buffer = new byte[count];
            var read = 0;
            var deadline = Environment.TickCount64 + timeoutMs;
            while (read < count)
            {
                var left = deadline - Environment.TickCount64;
                if (left <= 0) throw new TimeoutException();
                _port.ReadTimeout = (int)Math.Max(1, left);
                try { read += _port.Read(buffer, read, count - read); }
                catch (TimeoutException) when (Environment.TickCount64 < deadline) { }
            }
            return buffer;
        }

        public void DiscardInput() => _port.DiscardInBuffer();

        public void Dispose()
        {
            try { if (_port.IsOpen) _port.Close(); } catch (IOException) { }
            _port.Dispose();
        }
    }
}
