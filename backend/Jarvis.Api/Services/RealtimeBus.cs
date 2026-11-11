using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace Jarvis.Api.Services;

/// <summary>A topic on the real-time bus (e.g. "sim.pnl", "fyers.orders").</summary>
public record RtTopic(string Name, string Source, string Description, string KeyMeaning);

public record RtMessage(string Topic, string Key, DateTime TimeUtc, JsonElement Data);

public record RtTopicStats(
    string Topic, string Source, string Description, string KeyMeaning,
    long PublishedThisRun, DateTime? LastPublishUtc, int LatestKeys);

/// <summary>
/// One pipeline for every real-time source (simulator, Fyers, future features):
///
///   Publish(topic, key, data)
///     ├─► Redis  rt:{topic}:latest   HASH   key → latest JSON          (overwritten; "what is it now?")
///     ├─► Redis  rt:{topic}:stream   STREAM every change (capped)      (consumed by RealtimePersister → SQL)
///     └─► SignalR group "rt:{topic}"  event "rt"                       (browsers that subscribed)
///
/// Writes are fire-and-forget: a slow or stopped Redis never blocks the caller. The last value per key is also
/// kept in memory, so "latest" still answers while Redis is down (only history pauses).
/// To add a feature: pick a topic name, add it to <see cref="Topics"/>, call Publish. It is stored automatically.
/// </summary>
public class RealtimeBus(IConnectionMultiplexer mux, IHubContext<MarketHub> hub, IConfiguration config, ILogger<RealtimeBus> log)
{
    public const string PersisterGroup = "sql-persister";

    public static readonly IReadOnlyList<RtTopic> Topics =
    [
        new("sim.spot",        "Simulator", "Simulated NIFTY spot, every tick",                    "underlying (NIFTY)"),
        new("sim.pnl",         "Simulator", "Per-account P&L snapshot (every Realtime:SimPnlSeconds)", "client id"),
        new("sim.event",       "Simulator", "Lifecycle events: entry, exit, reject, auto-close, signal…", "client id or 'system'"),
        new("fyers.pnl",       "Fyers",     "Account P&L summary (realized / unrealized / total)",  "Fyers client id"),
        new("fyers.funds",     "Fyers",     "Fund limits (available, used margin…)",               "fund line title"),
        new("fyers.positions", "Fyers",     "Net positions (qty, avg, LTP, P&L)",                  "position id"),
        new("fyers.orders",    "Fyers",     "Order book; every status change is kept",             "order id"),
        new("fyers.trades",    "Fyers",     "Trade book (fills)",                                  "trade number"),
        new("fyers.holdings",  "Fyers",     "Holdings (qty, cost, LTP, P&L)",                      "symbol"),
        new("fyers.quotes",    "Fyers",     "Index / symbol quotes",                               "symbol"),
    ];

    public static bool IsKnown(string topic) => Topics.Any(t => t.Name == topic);
    public static string StreamKey(string topic) => $"rt:{topic}:stream";
    public static string LatestKey(string topic) => $"rt:{topic}:latest";
    public static string HubGroup(string topic) => $"rt:{topic}";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly int _maxLen = config.GetValue("Realtime:StreamMaxLength", 50_000);
    private readonly ConcurrentDictionary<(string Topic, string Key), string> _latest = new();
    private readonly ConcurrentDictionary<string, (long Count, DateTime Last)> _stats = new();

    public bool RedisConnected => mux.IsConnected;

    /// <param name="onlyIfChanged">skip when the payload is identical to the last one for this key (polled sources)</param>
    /// <returns>true if it was published</returns>
    public bool Publish(string topic, string key, object payload, bool onlyIfChanged = false, DateTime? timeUtc = null)
    {
        var json = JsonSerializer.Serialize(payload, Json);
        if (onlyIfChanged && _latest.TryGetValue((topic, key), out var prev) && prev == json) return false;
        _latest[(topic, key)] = json;

        var t = timeUtc ?? DateTime.UtcNow;
        _stats.AddOrUpdate(topic, _ => (1, t), (_, s) => (s.Count + 1, t));

        if (mux.IsConnected)
        {
            try
            {
                var db = mux.GetDatabase();
                db.HashSet(LatestKey(topic), key, json, flags: CommandFlags.FireAndForget);
                db.StreamAdd(StreamKey(topic),
                    [new NameValueEntry("k", key), new NameValueEntry("t", new DateTimeOffset(t).ToUnixTimeMilliseconds()), new NameValueEntry("d", json)],
                    maxLength: _maxLen, useApproximateMaxLength: true, flags: CommandFlags.FireAndForget);
            }
            catch (Exception ex) { log.LogDebug("Realtime Redis write skipped: {Message}", ex.Message); }
        }

        _ = hub.Clients.Group(HubGroup(topic)).SendAsync("rt", new { topic, key, timeUtc = t, data = payload });
        return true;
    }

    /// <summary>Latest value for every key of a topic: Redis first, in-memory copy if Redis is unavailable.</summary>
    public async Task<List<RtMessage>> LatestAsync(string topic)
    {
        if (mux.IsConnected)
        {
            try
            {
                var entries = await mux.GetDatabase().HashGetAllAsync(LatestKey(topic));
                if (entries.Length > 0)
                    return entries.Select(e => new RtMessage(topic, e.Name.ToString(), default, Parse(e.Value.ToString())))
                                  .OrderBy(m => m.Key).ToList();
            }
            catch (RedisException) { /* fall back to memory */ }
        }
        return _latest.Where(kv => kv.Key.Topic == topic)
                      .Select(kv => new RtMessage(topic, kv.Key.Key, default, Parse(kv.Value)))
                      .OrderBy(m => m.Key).ToList();
    }

    /// <summary>Most recent entries straight from the Redis stream (not yet necessarily in SQL).</summary>
    public async Task<List<RtMessage>> RecentAsync(string topic, int count)
    {
        if (!mux.IsConnected) return [];
        var entries = await mux.GetDatabase().StreamRangeAsync(StreamKey(topic), "-", "+", count, Order.Descending);
        return entries.Select(ToMessage(topic)).ToList();
    }

    public List<RtTopicStats> Stats() => Topics.Select(t =>
    {
        var s = _stats.TryGetValue(t.Name, out var v) ? v : (0, default);
        return new RtTopicStats(t.Name, t.Source, t.Description, t.KeyMeaning, s.Item1,
            s.Item1 == 0 ? null : s.Item2, _latest.Keys.Count(k => k.Topic == t.Name));
    }).ToList();

    // ------------------------------------------------------------------ stream consumer helpers (persister)

    public async Task EnsureGroupsAsync()
    {
        var db = mux.GetDatabase();
        foreach (var t in Topics)
        {
            try { await db.StreamCreateConsumerGroupAsync(StreamKey(t.Name), PersisterGroup, StreamPosition.Beginning, createStream: true); }
            catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP")) { }
        }
    }

    public Task<StreamEntry[]> ReadBatchAsync(string topic, string consumer, int count, bool pendingFirst) =>
        mux.GetDatabase().StreamReadGroupAsync(StreamKey(topic), PersisterGroup, consumer, pendingFirst ? "0" : ">", count);

    public Task<long> AckAsync(string topic, RedisValue[] ids) =>
        mux.GetDatabase().StreamAcknowledgeAsync(StreamKey(topic), PersisterGroup, ids);

    public async Task<(long Length, long Pending)> StreamInfoAsync(string topic)
    {
        if (!mux.IsConnected) return (0, 0);
        var db = mux.GetDatabase();
        var len = await db.StreamLengthAsync(StreamKey(topic));
        long pending = 0;
        try { pending = (await db.StreamPendingAsync(StreamKey(topic), PersisterGroup)).PendingMessageCount; } catch (RedisServerException) { }
        return (len, pending);
    }

    public static Func<StreamEntry, RtMessage> ToMessage(string topic) => e => new RtMessage(
        topic, e["k"].ToString(), DateTimeOffset.FromUnixTimeMilliseconds((long)e["t"]).UtcDateTime, Parse(e["d"].ToString()));

    private static JsonElement Parse(string json) { using var d = JsonDocument.Parse(json); return d.RootElement.Clone(); }
}
