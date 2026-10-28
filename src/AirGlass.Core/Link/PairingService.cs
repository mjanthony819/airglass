using System.Security.Cryptography;
using System.Text;

namespace AirGlass.Core.Link;

public enum PairResult { Pending, BadCode, Expired, LockedOut }

public sealed class PendingDevice
{
    public string Id { get; init; } = "";
    public string DeviceName { get; init; } = "";
}

/// <summary>
/// Pairing flow: PC shows a short code, the phone submits it, the PC user approves the
/// request, and the phone receives a long random token. Only token hashes are kept.
/// Transport security is NOT provided here; callers must only expose this over a
/// confidential, server-authenticated channel.
/// </summary>
public sealed class PairingService
{
    private readonly Func<DateTimeOffset> _now;
    private readonly object _lock = new();
    private readonly Dictionary<string, string> _tokenHashes = new(); // hash -> device name
    private readonly Dictionary<string, (PendingDevice Dev, string? Token)> _pending = new();
    private string? _code;
    private DateTimeOffset _codeExpires;
    private int _failures;
    private DateTimeOffset _lockedUntil;

    public TimeSpan CodeLifetime { get; init; } = TimeSpan.FromMinutes(2);
    public int MaxFailures { get; init; } = 5;
    public TimeSpan LockoutDuration { get; init; } = TimeSpan.FromMinutes(1);

    public PairingService(Func<DateTimeOffset>? now = null) => _now = now ?? (() => DateTimeOffset.UtcNow);

    public string NewCode()
    {
        lock (_lock)
        {
            _code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            _codeExpires = _now() + CodeLifetime;
            _failures = 0;
            return _code;
        }
    }

    public void CancelCode() { lock (_lock) _code = null; }

    public PairResult Submit(string code, string deviceName, out PendingDevice? device)
    {
        device = null;
        lock (_lock)
        {
            var now = _now();
            if (now < _lockedUntil) return PairResult.LockedOut;
            if (_code is null || now > _codeExpires) return PairResult.Expired;
            if (!FixedEquals(code ?? "", _code))
            {
                if (++_failures >= MaxFailures)
                {
                    _code = null; // a guessed-at code is burned, not retried
                    _lockedUntil = now + LockoutDuration;
                    return PairResult.LockedOut;
                }
                return PairResult.BadCode;
            }
            _code = null; // single use
            device = new PendingDevice
            {
                Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),
                DeviceName = CleanName(deviceName),
            };
            _pending[device.Id] = (device, null);
            return PairResult.Pending;
        }
    }

    public IReadOnlyList<PendingDevice> PendingList()
    {
        lock (_lock) return _pending.Values.Where(p => p.Token is null).Select(p => p.Dev).ToList();
    }

    /// <summary>Called from the PC UI when the user clicks Approve.</summary>
    public bool Approve(string pendingId)
    {
        lock (_lock)
        {
            if (!_pending.TryGetValue(pendingId, out var p) || p.Token is not null) return false;
            var token = Base64Url(RandomNumberGenerator.GetBytes(32));
            _tokenHashes[Hash(token)] = p.Dev.DeviceName;
            _pending[pendingId] = (p.Dev, token);
            return true;
        }
    }

    public bool Deny(string pendingId) { lock (_lock) return _pending.Remove(pendingId); }

    /// <summary>The phone polls this; the token is handed out exactly once.</summary>
    public (string Status, string? Token) Poll(string pendingId)
    {
        lock (_lock)
        {
            if (!_pending.TryGetValue(pendingId, out var p)) return ("denied", null);
            if (p.Token is null) return ("pending", null);
            _pending.Remove(pendingId);
            return ("approved", p.Token);
        }
    }

    public string? Validate(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128) return null;
        var h = Hash(token);
        lock (_lock)
        {
            string? found = null;
            foreach (var (k, v) in _tokenHashes)
                if (FixedEquals(k, h)) found = v;
            return found;
        }
    }

    public void RevokeAll() { lock (_lock) { _tokenHashes.Clear(); _pending.Clear(); } }

    public int PairedCount { get { lock (_lock) return _tokenHashes.Count; } }

    private static string CleanName(string? n)
    {
        var s = new string((n ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (s.Length == 0) s = "iPhone";
        return s.Length > 40 ? s[..40] : s;
    }

    private static string Hash(string t) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(t)));
    private static string Base64Url(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static bool FixedEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
