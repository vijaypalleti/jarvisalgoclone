using System.Globalization;
using System.Text.Json;
using StackExchange.Redis;

namespace Jarvis.Api.Services;

/// <summary>
/// Real-time market data in Redis.
///   delta:{SYM}:ticker  string  latest ticker JSON (overwritten)
///   delta:{SYM}:book    string  latest top-15 order book JSON (overwritten)
///   delta:{SYM}:trades  stream  every live trade (capped ~MaxLength), consumed by the SQL persister group
/// Writes are fire-and-forget so the live feed never waits on Redis; if Redis is down they are skipped.
/// </summary>
public class RedisMarketStore(IConnectionMultiplexer mux, IConfiguration config, ILogger<RedisMarketStore> log)
{
    public const string PersisterGroup = "sql-persister";
    private readonly int _maxLen = config.GetValue("Delta:RedisTradeStreamMaxLength", 100_000);
    private IDatabase Db => mux.GetDatabase();

    public bool Connected => mux.IsConnected;
    public string Endpoint => string.Join(",", mux.GetEndPoints().Select(e => e.ToString()));

    public static string TickerKey(string sym) => $"delta:{sym}:ticker";
    public static string BookKey(string sym) => $"delta:{sym}:book";
    public static string TradesKey(string sym) => $"delta:{sym}:trades";

    // ------------------------------------------------------------------ writes (hot path)

    public void SaveTicker(DeltaTicker t) => Try(() =>
        Db.StringSet(TickerKey(t.Symbol), JsonSerializer.Serialize(t), flags: CommandFlags.FireAndForget));

    public void SaveBook(DeltaOrderBook b) => Try(() =>
        Db.StringSet(BookKey(b.Symbol), JsonSerializer.Serialize(b), flags: CommandFlags.FireAndForget));

    public void AddTrade(string symbol, DeltaTrade t, long timeUs) => Try(() =>
        Db.StreamAdd(TradesKey(symbol),
            [
                new NameValueEntry("ts", timeUs),
                new NameValueEntry("p", t.Price.ToString(CultureInfo.InvariantCulture)),
                new NameValueEntry("s", t.Size.ToString(CultureInfo.InvariantCulture)),
                new NameValueEntry("side", t.Side),
            ],
            maxLength: _maxLen, useApproximateMaxLength: true, flags: CommandFlags.FireAndForget));

    private void Try(Action write)
    {
        if (!mux.IsConnected) return;
        try { write(); }
        catch (Exception ex) { log.LogDebug("Redis write skipped: {Message}", ex.Message); }
    }

    // ------------------------------------------------------------------ reads

    public async Task<(DeltaTicker? Ticker, DeltaOrderBook? Book, List<DeltaTrade> Trades)> LoadLatestAsync(string symbol, int trades)
    {
        if (!mux.IsConnected) return (null, null, []);
        var db = Db;
        var tickerJson = await db.StringGetAsync(TickerKey(symbol));
        var bookJson = await db.StringGetAsync(BookKey(symbol));
        var entries = await db.StreamRangeAsync(TradesKey(symbol), "-", "+", trades, Order.Descending);
        return (
            tickerJson.HasValue ? JsonSerializer.Deserialize<DeltaTicker>(tickerJson.ToString()) : null,
            bookJson.HasValue ? JsonSerializer.Deserialize<DeltaOrderBook>(bookJson.ToString()) : null,
            entries.Select(e => ParseTrade(e).Trade).ToList());
    }

    public async Task<DeltaTicker?> GetTickerAsync(string symbol)
    {
        if (!mux.IsConnected) return null;
        var json = await Db.StringGetAsync(TickerKey(symbol));
        return json.HasValue ? JsonSerializer.Deserialize<DeltaTicker>(json.ToString()) : null;
    }

    // ------------------------------------------------------------------ stream consumer (persister)

    public async Task EnsureGroupAsync(string symbol)
    {
        try { await Db.StreamCreateConsumerGroupAsync(TradesKey(symbol), PersisterGroup, StreamPosition.NewMessages, createStream: true); }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP")) { /* already exists */ }
    }

    /// <param name="pendingFirst">true = re-read entries delivered to this consumer but never acked (after a crash)</param>
    public Task<StreamEntry[]> ReadBatchAsync(string symbol, string consumer, int count, bool pendingFirst) =>
        Db.StreamReadGroupAsync(TradesKey(symbol), PersisterGroup, consumer, pendingFirst ? "0" : ">", count);

    public Task<long> AckAsync(string symbol, RedisValue[] ids) => Db.StreamAcknowledgeAsync(TradesKey(symbol), PersisterGroup, ids);

    public async Task<(long Length, long Pending)> StreamInfoAsync(string symbol)
    {
        if (!mux.IsConnected) return (0, 0);
        var len = await Db.StreamLengthAsync(TradesKey(symbol));
        long pending = 0;
        try { pending = (await Db.StreamPendingAsync(TradesKey(symbol), PersisterGroup)).PendingMessageCount; } catch (RedisServerException) { }
        return (len, pending);
    }

    public static (DeltaTrade Trade, long TimeUs) ParseTrade(StreamEntry e)
    {
        long us = (long)e["ts"];
        var price = decimal.Parse(e["p"].ToString(), CultureInfo.InvariantCulture);
        var size = decimal.Parse(e["s"].ToString(), CultureInfo.InvariantCulture);
        return (new DeltaTrade(price, size, e["side"].ToString(), us / 1000), us);
    }
}
