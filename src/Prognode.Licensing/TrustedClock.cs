using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Prognode.Licensing;

/// <summary>
/// Clock used for every license lifecycle decision. It never goes backwards: it is the later of the
/// system clock and a persisted high-water mark of time already observed. Turning the Windows clock
/// back therefore cannot extend a license or a trial.
///
/// The mark is stored in several places with an HMAC keyed to this machine, so an edited or copied
/// file is ignored. Only time reported by PROGNODE Cloud over the pinned HTTPS channel may move the
/// mark backwards (to recover from a clock that was set too far ahead by mistake). Signed documents
/// (license and activation issue times) can only raise it.
/// </summary>
public sealed class TrustedClock
{
    /// <summary>A system clock this far behind the mark is reported as a rollback.</summary>
    public static readonly TimeSpan RollbackTolerance = TimeSpan.FromHours(24);
    private static readonly TimeSpan PersistInterval = TimeSpan.FromMinutes(1);

    private readonly string[] _paths;
    private readonly byte[] _key;
    private readonly Func<DateTimeOffset> _system;
    private readonly object _gate = new();
    private DateTimeOffset _mark;
    private DateTimeOffset _persistedMark = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPersistAttempt = DateTimeOffset.MinValue;

    public TrustedClock(IEnumerable<string> paths, IMachineFingerprintProvider machine, Func<DateTimeOffset>? systemClock = null)
    {
        _paths = paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _system = systemClock ?? (() => DateTimeOffset.UtcNow);
        string fingerprint;
        try { fingerprint = machine.GetFingerprint(); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException) { fingerprint = "no-machine-identity"; }
        _key = SHA256.HashData(Encoding.UTF8.GetBytes("PROGNODE-CLOCK-V1|" + fingerprint));
        _mark = LoadMark() ?? DateTimeOffset.MinValue;
        _persistedMark = _mark;
    }

    /// <summary>The later of the system clock and the high-water mark; never decreases.</summary>
    public DateTimeOffset UtcNow
    {
        get
        {
            var system = _system();
            lock (_gate)
            {
                if (system > _mark)
                {
                    _mark = system;
                    PersistIfDue(system);
                }
                return _mark;
            }
        }
    }

    public DateTimeOffset HighWaterMark { get { lock (_gate) return _mark; } }

    /// <summary>True when the system clock is well behind time this machine has already seen.</summary>
    public bool RollbackDetected { get { lock (_gate) return _system() < _mark - RollbackTolerance; } }

    /// <summary>Raises the mark to a signed timestamp (license or activation issue time). Never lowers it.</summary>
    public void ObserveSigned(DateTimeOffset signedUtc)
    {
        lock (_gate)
        {
            if (signedUtc <= _mark)
                return;
            _mark = signedUtc;
            Persist(force: true);
        }
    }

    /// <summary>
    /// Trusted current time from PROGNODE Cloud. Raises the mark, and may also lower it when the PC's
    /// clock had been set into the future by mistake (never below the PC's own clock).
    /// </summary>
    public void SynchronizeFromCloud(DateTimeOffset serverUtc)
    {
        lock (_gate)
        {
            var target = serverUtc > _system() ? serverUtc : _system();
            if (target == _mark)
                return;
            _mark = target;
            Persist(force: true);
        }
    }

    private void PersistIfDue(DateTimeOffset system)
    {
        if (system - _lastPersistAttempt >= PersistInterval)
            Persist(force: false);
    }

    private void Persist(bool force)
    {
        if (!force && _mark == _persistedMark)
            return;
        _lastPersistAttempt = _system();
        var utc = _mark.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        var document = JsonSerializer.SerializeToUtf8Bytes(new ClockDocument(utc, Mac(utc)));
        var wrote = false;
        foreach (var path in _paths)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temp = path + ".tmp";
                File.WriteAllBytes(temp, document);
                File.Move(temp, path, overwrite: true);
                wrote = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Another location may still succeed; the in-memory mark keeps protecting this run.
            }
        }
        if (wrote)
            _persistedMark = _mark;
    }

    private DateTimeOffset? LoadMark()
    {
        DateTimeOffset? best = null;
        foreach (var path in _paths)
        {
            try
            {
                if (!File.Exists(path))
                    continue;
                var document = JsonSerializer.Deserialize<ClockDocument>(File.ReadAllBytes(path));
                if (document is null || string.IsNullOrEmpty(document.Utc) || string.IsNullOrEmpty(document.Mac))
                    continue;
                if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Mac(document.Utc)), Encoding.ASCII.GetBytes(document.Mac)))
                    continue; // edited, or copied from another machine
                if (DateTimeOffset.TryParse(document.Utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
                    best = best is null || value > best ? value.ToUniversalTime() : best;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Unreadable copy: ignore and use the others.
            }
        }
        return best;
    }

    private string Mac(string utc) => Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(utc))).ToLowerInvariant();

    private sealed record ClockDocument(string Utc, string Mac);
}
