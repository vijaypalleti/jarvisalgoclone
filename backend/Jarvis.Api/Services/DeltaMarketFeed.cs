using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;

namespace Jarvis.Api.Services;

// ---------------- DTOs pushed to the browser ----------------

public record DeltaTicker(
    string Symbol, decimal Last, decimal Mark, decimal Spot, decimal Open, decimal High, decimal Low,
    decimal ChangePct, decimal Volume, decimal TurnoverUsd, decimal Oi, decimal OiUsd, decimal FundingRate,
    decimal BestBid, decimal BestAsk, decimal BidSize, decimal AskSize, decimal ContractValue, long Time);

public record DeltaTrade(decimal Price, decimal Size, string Side, long Time);

public record BookLevel(decimal Price, decimal Size, decimal Total);

public record DeltaOrderBook(string Symbol, List<BookLevel> Bids, List<BookLevel> Asks, decimal Spread, long Time);

public record DeltaFeedStatus(bool Connected, string Symbol, DateTime? ConnectedAt, DateTime? LastMessageAt, int Reconnects, string? LastError);

public record DeltaSnapshot(DeltaFeedStatus Status, DeltaTicker? Ticker, DeltaOrderBook? OrderBook, List<DeltaTrade> Trades);

/// <summary>SignalR hub the Angular page connects to (/hubs/market). Server → client events: ticker, trade, orderbook, status.</summary>
public class MarketHub(DeltaMarketFeed feed) : Hub
{
    public DeltaSnapshot Snapshot() => feed.Snapshot();
}

/// <summary>
/// Keeps ONE public (no login) WebSocket connection to Delta Exchange India and fans updates out to
/// every browser over SignalR. Auto-reconnects with backoff. Market data only — no API key involved.
/// </summary>
public class DeltaMarketFeed(IHubContext<MarketHub> hub, RedisMarketStore redis, IConfiguration config, ILogger<DeltaMarketFeed> log) : BackgroundService
{
    private readonly string _url = config["Delta:SocketUrl"] ?? "wss://socket.india.delta.exchange";
    public string Symbol { get; } = config["Delta:Symbol"] ?? "BTCUSD";
    private const int BookDepth = 15;
    private const int TradeHistory = 60;

    private readonly object _lock = new();
    private DeltaTicker? _ticker;
    private DeltaOrderBook? _book;
    private readonly LinkedList<DeltaTrade> _trades = new();
    private bool _connected;
    private DateTime? _connectedAt, _lastMsgAt;
    private int _reconnects;
    private string? _lastError;
    private long _lastBookPush;

    public DeltaSnapshot Snapshot()
    {
        lock (_lock) return new DeltaSnapshot(Status(), _ticker, _book, _trades.ToList());
    }

    private DeltaFeedStatus Status() => new(_connected, Symbol, _connectedAt, _lastMsgAt, _reconnects, _lastError);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        // Warm start: show the last known state from Redis immediately, before Delta reconnects.
        try
        {
            var (t, b, trades) = await redis.LoadLatestAsync(Symbol, TradeHistory);
            lock (_lock)
            {
                _ticker ??= t; _book ??= b;
                if (_trades.Count == 0) foreach (var x in trades) _trades.AddLast(x);
            }
            if (t is not null) log.LogInformation("Warm-started {Symbol} from Redis ({Trades} trades)", Symbol, trades.Count);
        }
        catch (Exception ex) { log.LogDebug("Redis warm start skipped: {Message}", ex.Message); }

        var backoff = TimeSpan.FromSeconds(1);
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await RunOnce(stop);
                backoff = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                log.LogWarning("Delta feed disconnected: {Message}", ex.Message);
            }
            _connected = false;
            _reconnects++;
            await hub.Clients.All.SendAsync("status", Status(), stop);
            await Task.Delay(backoff, stop);
            backoff = TimeSpan.FromSeconds(Math.Min(30, backoff.TotalSeconds * 2));
        }
    }

    private async Task RunOnce(CancellationToken stop)
    {
        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await ws.ConnectAsync(new Uri(_url), stop);

        var sub = JsonSerializer.Serialize(new
        {
            type = "subscribe",
            payload = new
            {
                channels = new object[]
                {
                    new { name = "v2/ticker", symbols = new[] { Symbol } },
                    new { name = "all_trades", symbols = new[] { Symbol } },
                    new { name = "l2_orderbook", symbols = new[] { Symbol } },
                }
            }
        });
        await ws.SendAsync(Encoding.UTF8.GetBytes(sub), WebSocketMessageType.Text, true, stop);

        _connected = true; _connectedAt = DateTime.UtcNow; _lastError = null;
        log.LogInformation("Delta feed connected ({Symbol})", Symbol);
        await hub.Clients.All.SendAsync("status", Status(), stop);

        var buffer = new byte[64 * 1024];
        using var ms = new MemoryStream();
        while (ws.State == WebSocketState.Open && !stop.IsCancellationRequested)
        {
            ms.SetLength(0);
            WebSocketReceiveResult r;
            // a silent socket for 45s is treated as dead
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            do
            {
                r = await ws.ReceiveAsync(buffer, timeout.Token);
                if (r.MessageType == WebSocketMessageType.Close) throw new Exception($"closed by server: {r.CloseStatusDescription}");
                ms.Write(buffer, 0, r.Count);
            } while (!r.EndOfMessage);

            _lastMsgAt = DateTime.UtcNow;
            try { await Handle(ms.GetBuffer().AsMemory(0, (int)ms.Length), stop); }
            catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
            {
                log.LogDebug("Skipped Delta message: {Message}", ex.Message);
            }
        }
    }

    private async Task Handle(ReadOnlyMemory<byte> bytes, CancellationToken stop)
    {
        using var doc = JsonDocument.Parse(bytes);
        var m = doc.RootElement;
        var type = m.TryGetProperty("type", out var t) ? t.GetString() : null;

        switch (type)
        {
            case "v2/ticker":
            {
                var q = m.TryGetProperty("quotes", out var qq) ? qq : default;
                var tick = new DeltaTicker(
                    Symbol: Str(m, "symbol") ?? Symbol,
                    Last: Num(m, "close"), Mark: Num(m, "mark_price"), Spot: Num(m, "spot_price"),
                    Open: Num(m, "open"), High: Num(m, "high"), Low: Num(m, "low"),
                    ChangePct: Num(m, "ltp_change_24h"), Volume: Num(m, "volume"), TurnoverUsd: Num(m, "turnover_usd"),
                    Oi: Num(m, "oi"), OiUsd: Num(m, "oi_value_usd"), FundingRate: Num(m, "funding_rate"),
                    BestBid: Num(q, "best_bid"), BestAsk: Num(q, "best_ask"), BidSize: Num(q, "bid_size"), AskSize: Num(q, "ask_size"),
                    ContractValue: Num(m, "contract_value"), Time: Num(m, "timestamp") is var ts and > 0 ? (long)(ts / 1000) : Now());
                lock (_lock) _ticker = tick;
                redis.SaveTicker(tick);
                await hub.Clients.All.SendAsync("ticker", tick, stop);
                break;
            }
            case "all_trades":
            {
                var trade = ToTrade(m);
                lock (_lock) { _trades.AddFirst(trade); while (_trades.Count > TradeHistory) _trades.RemoveLast(); }
                var us = (long)Num(m, "timestamp");
                redis.AddTrade(Symbol, trade, us > 0 ? us : trade.Time * 1000);   // → Redis stream → SQL persister
                await hub.Clients.All.SendAsync("trade", trade, stop);
                break;
            }
            case "all_trades_snapshot":
            {
                if (!m.TryGetProperty("trades", out var arr)) break;
                var list = arr.EnumerateArray().Select(ToTrade).OrderByDescending(x => x.Time).Take(TradeHistory).ToList();
                lock (_lock) { _trades.Clear(); foreach (var x in list) _trades.AddLast(x); }
                break;
            }
            case "l2_orderbook":
            {
                var bids = Levels(m, "buy");
                var asks = Levels(m, "sell");
                var spread = bids.Count > 0 && asks.Count > 0 ? asks[0].Price - bids[0].Price : 0;
                var book = new DeltaOrderBook(Str(m, "symbol") ?? Symbol, bids, asks, spread, Now());
                lock (_lock) _book = book;
                // full-depth book arrives often and is large — push at most ~3x/second
                var now = Now();
                if (now - Interlocked.Read(ref _lastBookPush) >= 333)
                {
                    Interlocked.Exchange(ref _lastBookPush, now);
                    redis.SaveBook(book);
                    await hub.Clients.All.SendAsync("orderbook", book, stop);
                }
                break;
            }
        }
    }

    private static DeltaTrade ToTrade(JsonElement e)
    {
        // buyer is the taker → aggressive buy (green); otherwise aggressive sell (red)
        var side = Str(e, "buyer_role") == "taker" ? "buy" : "sell";
        var ts = Num(e, "timestamp");
        return new DeltaTrade(Num(e, "price"), Num(e, "size"), side, ts > 0 ? (long)(ts / 1000) : Now());
    }

    private static List<BookLevel> Levels(JsonElement m, string prop)
    {
        var list = new List<BookLevel>(BookDepth);
        if (!m.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        decimal total = 0;
        foreach (var l in arr.EnumerateArray())
        {
            if (list.Count == BookDepth) break;
            var size = Num(l, "size");
            total += size;
            list.Add(new BookLevel(Num(l, "limit_price"), size, total));
        }
        return list;
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static string? Str(JsonElement e, string p) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Delta mixes "123.4" strings and 123.4 numbers — accept both, default 0.</summary>
    private static decimal Num(JsonElement e, string p)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetDecimal(out var d) ? d : (decimal)v.GetDouble(),
            JsonValueKind.String => decimal.TryParse(v.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0,
            _ => 0
        };
    }
}
