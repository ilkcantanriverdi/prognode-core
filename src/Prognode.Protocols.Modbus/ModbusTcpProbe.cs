using System.Diagnostics;
using System.Net.Sockets;
using Prognode.Contracts.Protocols;

namespace Prognode.Protocols.Modbus;

/// <summary>
/// Minimal production-shaped Modbus TCP protocol probe.
/// It performs a real FC03 request against Holding Register 40001
/// (PDU start address 0, quantity 1) and validates the Modbus response.
/// </summary>
public sealed class ModbusTcpProbe
{
    private int _transactionId;

    public async Task<ModbusTcpTestResult> TestAsync(
        string host,
        int port,
        int unitId,
        int timeoutMs,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(timeoutMs);

            using var client = new TcpClient();

            await client.ConnectAsync(host, port, timeout.Token);

            var stream = client.GetStream();

            var transactionId = (ushort)Interlocked.Increment(ref _transactionId);

            // MBAP + PDU:
            // Transaction ID 2
            // Protocol ID    2 = 0
            // Length         2 = UnitId(1) + PDU(5) = 6
            // Unit ID        1
            // Function       1 = 0x03
            // Start Address  2 = 0 -> user-facing 40001
            // Quantity       2 = 1
            var request = new byte[]
            {
                (byte)(transactionId >> 8),
                (byte)(transactionId & 0xFF),
                0x00, 0x00,
                0x00, 0x06,
                (byte)unitId,
                0x03,
                0x00, 0x00,
                0x00, 0x01
            };

            await stream.WriteAsync(request, timeout.Token);

            // Read MBAP header first (7 bytes).
            var header = new byte[7];
            await ReadExactlyAsync(stream, header, timeout.Token);

            var responseTransactionId = (ushort)((header[0] << 8) | header[1]);
            var protocolId = (ushort)((header[2] << 8) | header[3]);
            var length = (ushort)((header[4] << 8) | header[5]);
            var responseUnitId = header[6];

            if (responseTransactionId != transactionId)
            {
                return Failure(
                    "Modbus",
                    $"Transaction ID mismatch. Expected {transactionId}, received {responseTransactionId}.",
                    stopwatch,
                    host, port, unitId);
            }

            if (protocolId != 0)
            {
                return Failure(
                    "Modbus",
                    $"Invalid Protocol ID {protocolId}. Expected 0.",
                    stopwatch,
                    host, port, unitId);
            }

            if (responseUnitId != (byte)unitId)
            {
                return Failure(
                    "Modbus",
                    $"Unit ID mismatch. Expected {unitId}, received {responseUnitId}.",
                    stopwatch,
                    host, port, unitId);
            }

            // Length includes Unit ID, which we already consumed.
            var remaining = length - 1;

            if (remaining <= 0 || remaining > 260)
            {
                return Failure(
                    "Modbus",
                    $"Invalid Modbus response length {length}.",
                    stopwatch,
                    host, port, unitId);
            }

            var pdu = new byte[remaining];
            await ReadExactlyAsync(stream, pdu, timeout.Token);

            var function = pdu[0];

            if ((function & 0x80) != 0)
            {
                var exceptionCode = pdu.Length > 1 ? pdu[1] : (byte)0;
                return Failure(
                    "Modbus",
                    $"Modbus exception response. Function 0x{function:X2}, exception 0x{exceptionCode:X2}.",
                    stopwatch,
                    host, port, unitId);
            }

            if (function != 0x03)
            {
                return Failure(
                    "Modbus",
                    $"Unexpected function code 0x{function:X2}. Expected 0x03.",
                    stopwatch,
                    host, port, unitId);
            }

            if (pdu.Length < 4)
            {
                return Failure(
                    "Modbus",
                    "Response PDU is too short.",
                    stopwatch,
                    host, port, unitId);
            }

            var byteCount = pdu[1];

            if (byteCount != 2 || pdu.Length < 4)
            {
                return Failure(
                    "Modbus",
                    $"Unexpected byte count {byteCount}. Expected 2.",
                    stopwatch,
                    host, port, unitId);
            }

            var registerValue = (pdu[2] << 8) | pdu[3];

            stopwatch.Stop();

            return new ModbusTcpTestResult(
                Success: true,
                Stage: "Modbus",
                Message: "TCP connected and valid Modbus FC03 response received.",
                ResponseTimeMs: Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1),
                Host: host,
                Port: port,
                UnitId: unitId,
                HoldingRegister40001: registerValue
            );
        }
        catch (OperationCanceledException)
        {
            return Failure(
                "TCP",
                $"Connection or Modbus response timed out after {timeoutMs} ms.",
                stopwatch,
                host, port, unitId);
        }
        catch (SocketException ex)
        {
            return Failure(
                "TCP",
                $"TCP connection failed: {ex.SocketErrorCode}.",
                stopwatch,
                host, port, unitId);
        }
        catch (Exception ex)
        {
            return Failure(
                "Runtime",
                ex.Message,
                stopwatch,
                host, port, unitId);
        }
    }

    private static ModbusTcpTestResult Failure(
        string stage,
        string message,
        Stopwatch stopwatch,
        string host,
        int port,
        int unitId)
    {
        stopwatch.Stop();

        return new ModbusTcpTestResult(
            Success: false,
            Stage: stage,
            Message: message,
            ResponseTimeMs: Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1),
            Host: host,
            Port: port,
            UnitId: unitId,
            HoldingRegister40001: null
        );
    }

    private static async Task ReadExactlyAsync(
        NetworkStream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;

        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(offset, buffer.Length - offset),
                cancellationToken);

            if (read == 0)
                throw new IOException("Remote endpoint closed the connection.");

            offset += read;
        }
    }
}
