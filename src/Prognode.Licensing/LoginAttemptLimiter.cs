namespace Prognode.Licensing;

/// <summary>Thrown when sign-in is temporarily blocked after repeated failures.</summary>
public sealed class LoginThrottledException(TimeSpan retryAfter)
    : InvalidOperationException($"Too many failed sign-in attempts. Try again in {Math.Max(1, (int)Math.Ceiling(retryAfter.TotalMinutes))} minute(s).")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

/// <summary>
/// Slows down password guessing against the offline credential. The Argon2id verifier is already
/// expensive per attempt; this caps attempts per source address and across all sources, so a LAN
/// attacker cannot try passwords indefinitely. In-memory: a restart resets it, which still keeps
/// online guessing far below a useful rate.
/// </summary>
public sealed class LoginAttemptLimiter
{
    public const int MaxFailuresPerSource = 5;
    public const int MaxFailuresTotal = 20;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly object _gate = new();
    private readonly Dictionary<string, List<DateTimeOffset>> _bySource = new(StringComparer.Ordinal);
    private readonly List<DateTimeOffset> _all = [];
    private readonly Func<DateTimeOffset> _clock;

    public LoginAttemptLimiter(Func<DateTimeOffset>? clock = null) => _clock = clock ?? (() => DateTimeOffset.UtcNow);

    /// <summary>Throws <see cref="LoginThrottledException"/> when the source or the system is locked.</summary>
    public void EnsureAllowed(string source)
    {
        lock (_gate)
        {
            var now = _clock();
            Prune(now);
            var mine = _bySource.GetValueOrDefault(source);
            if (mine is { Count: >= MaxFailuresPerSource })
                throw new LoginThrottledException(mine[0] + Window - now);
            if (_all.Count >= MaxFailuresTotal)
                throw new LoginThrottledException(_all[0] + Window - now);
        }
    }

    public void RecordFailure(string source)
    {
        lock (_gate)
        {
            var now = _clock();
            if (!_bySource.TryGetValue(source, out var list))
                _bySource[source] = list = [];
            list.Add(now);
            _all.Add(now);
        }
    }

    public void RecordSuccess(string source)
    {
        lock (_gate)
            _bySource.Remove(source);
    }

    private void Prune(DateTimeOffset now)
    {
        var cutoff = now - Window;
        _all.RemoveAll(t => t <= cutoff);
        foreach (var key in _bySource.Keys.ToList())
        {
            _bySource[key].RemoveAll(t => t <= cutoff);
            if (_bySource[key].Count == 0)
                _bySource.Remove(key);
        }
    }
}
