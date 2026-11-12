using System.Collections.Concurrent;

namespace Jarvis.Api.Services.Assistant;

public record GroqKeyStatus(int Index, string Hint, bool Invalid, List<string> Cooling, long Requests, long RateLimited);

/// <summary>
/// Groq API keys with failover, tracked per (key, model) because Groq limits are per organization AND per model:
///   • 429 rate limit → that key+model is parked until Groq's "try again in …", the next key (or the next model) is used
///   • 401 invalid key → the key is parked for an hour for every model
/// Keys: user-secrets Groq:ApiKeys:0 … :N (or "k1,k2"), or Groq:ApiKey. Keys from the same Groq account share one
/// limit, so extra keys add resilience (revoked/invalid key) rather than capacity; model fallback adds capacity.
/// </summary>
public sealed class GroqKeyPool
{
    private readonly List<string> _keys;
    private readonly ConcurrentDictionary<(int key, string model), (DateTime until, string why)> _cooling = new();
    private readonly ConcurrentDictionary<int, DateTime> _invalid = new();
    private readonly ConcurrentDictionary<string, DateTime> _modelDown = new();   // model not available on Groq
    private readonly long[] _requests, _limited;
    private int _next = -1;

    public GroqKeyPool(IConfiguration config, ILogger<GroqKeyPool> log)
    {
        var section = config.GetSection("Groq:ApiKeys");
        var list = section.GetChildren().Select(c => c.Value).ToList();
        if (list.Count == 0 && !string.IsNullOrWhiteSpace(section.Value)) list.AddRange(section.Value.Split(',', ';'));
        list.Add(config["Groq:ApiKey"]);
        _keys = list.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k!.Trim()).Distinct().ToList();
        _requests = new long[_keys.Count];
        _limited = new long[_keys.Count];
        log.LogInformation("Groq key pool: {Count} key(s) configured", _keys.Count);
    }

    public int Count => _keys.Count;
    public bool AllInvalid => _keys.Count > 0 && Enumerable.Range(0, _keys.Count).All(i => _invalid.TryGetValue(i, out var u) && u > DateTime.UtcNow);

    public bool ModelAvailable(string model) => !(_modelDown.TryGetValue(model, out var u) && u > DateTime.UtcNow);
    public void MarkModelDown(string model) => _modelDown[model] = DateTime.UtcNow.AddHours(6);

    /// <summary>Next usable key for this model (round-robin), or null + when the soonest one frees up.</summary>
    public (int index, string key)? Next(string model, out DateTime? soonestUtc)
    {
        soonestUtc = null;
        var now = DateTime.UtcNow;
        var start = Interlocked.Increment(ref _next);
        for (var i = 0; i < _keys.Count; i++)
        {
            var idx = (int)((uint)(start + i) % (uint)_keys.Count);
            if (_invalid.TryGetValue(idx, out var bad) && bad > now) continue;
            if (_cooling.TryGetValue((idx, model), out var c) && c.until > now) { if (soonestUtc is null || c.until < soonestUtc) soonestUtc = c.until; continue; }
            Interlocked.Increment(ref _requests[idx]);
            return (idx, _keys[idx]);
        }
        return null;
    }

    public void MarkRateLimited(int index, string model, TimeSpan retryAfter, string why)
    {
        Interlocked.Increment(ref _limited[index]);
        _cooling[(index, model)] = (DateTime.UtcNow + retryAfter, why);
    }

    public void MarkInvalid(int index) => _invalid[index] = DateTime.UtcNow.AddHours(1);

    /// <summary>"key 2 (…oHtS)": never exposes the key itself.</summary>
    public string Hint(int index) => $"key {index + 1} (…{_keys[index][^4..]})";

    public List<GroqKeyStatus> Status()
    {
        var now = DateTime.UtcNow;
        return _keys.Select((_, i) => new GroqKeyStatus(i + 1, Hint(i),
            _invalid.TryGetValue(i, out var u) && u > now,
            _cooling.Where(kv => kv.Key.key == i && kv.Value.until > now)
                    .Select(kv => $"{kv.Key.model}: {kv.Value.why} until {kv.Value.until.AddHours(5.5):HH:mm:ss} IST").ToList(),
            _requests[i], _limited[i])).ToList();
    }
}
