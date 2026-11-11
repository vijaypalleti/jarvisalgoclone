namespace Jarvis.Api.Services;

public record FyersPollerStatus(bool Enabled, bool Configured, string State, DateTime? LastPollUtc, DateTime? LastChangeUtc,
    long Changes, string? LastError, int PollSeconds, string QuoteSymbols);

/// <summary>
/// Polls the (read-only) Fyers account in the background and publishes every CHANGE to the real-time bus,
/// so Fyers data reaches Redis → SQL even when nobody has the Broker Live page open.
///   fyers.pnl · fyers.funds · fyers.positions · fyers.orders · fyers.trades · fyers.holdings · fyers.quotes
/// Only changed records are published (an order appears once per status change), which keeps history small.
/// Token missing/expired → it idles quietly and retries every minute. Never places, modifies or cancels orders.
/// </summary>
public class FyersPoller(IServiceScopeFactory scopes, RealtimeBus bus, IConfiguration config, ILogger<FyersPoller> log) : BackgroundService
{
    private readonly bool _enabled = config.GetValue("Fyers:Poll:Enabled", true);
    private readonly int _pollSeconds = Math.Clamp(config.GetValue("Fyers:Poll:Seconds", 10), 3, 300);
    private readonly string _quoteSymbols = config["Fyers:Poll:QuoteSymbols"] ?? "NSE:NIFTY50-INDEX,NSE:NIFTYBANK-INDEX,BSE:SENSEX-INDEX";

    private string _state = "starting";
    private bool _configured;
    private DateTime? _lastPoll, _lastChange;
    private long _changes;
    private string? _lastError;

    public FyersPollerStatus Status() => new(_enabled, _configured, _state, _lastPoll, _lastChange, _changes, _lastError, _pollSeconds, _quoteSymbols);

    private static readonly Dictionary<int, string> OrderStatus = new()
    { [1] = "CANCELLED", [2] = "TRADED", [3] = "NOT_USED", [4] = "TRANSIT", [5] = "REJECTED", [6] = "PENDING", [7] = "EXPIRED" };

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!_enabled) { _state = "disabled"; return; }
        await Task.Delay(TimeSpan.FromSeconds(3), stop);   // let the app finish starting

        while (!stop.IsCancellationRequested)
        {
            var wait = TimeSpan.FromSeconds(_pollSeconds);
            try
            {
                // FyersClient is a typed HttpClient (transient) → resolve a fresh one per poll
                using var scope = scopes.CreateScope();
                var fyers = scope.ServiceProvider.GetRequiredService<FyersClient>();
                _configured = fyers.Configured;
                if (!_configured)
                {
                    _state = "not configured (set Fyers:ClientId / Fyers:AccessToken)";
                    wait = TimeSpan.FromSeconds(60);
                }
                else
                {
                    await PollOnce(fyers, fyers.ClientId ?? "fyers");
                    _state = "polling";
                    _lastError = null;
                }
            }
            catch (FyersException ex) when (ex.Status == 401)
            {
                if (_state != "token invalid") log.LogWarning("Fyers poller: token invalid/expired ({Message}); retrying every minute", ex.Message);
                _state = "token invalid";
                _lastError = ex.Message;
                wait = TimeSpan.FromSeconds(60);
            }
            catch (Exception ex) when (!stop.IsCancellationRequested)
            {
                _state = "error";
                _lastError = ex.Message;
                log.LogDebug("Fyers poll failed: {Message}", ex.Message);
                wait = TimeSpan.FromSeconds(Math.Max(_pollSeconds, 30));
            }
            _lastPoll = DateTime.UtcNow;
            await Task.Delay(wait, stop);
        }
    }

    private async Task PollOnce(FyersClient fyers, string fyId)
    {
        var positions = await fyers.Positions();
        var o = positions.Overall;
        Count(bus.Publish("fyers.pnl", fyId, new
        {
            realized = o?.PlRealized ?? positions.NetPositions.Sum(p => p.RealizedProfit),
            unrealized = o?.PlUnrealized ?? positions.NetPositions.Sum(p => p.UnrealizedProfit),
            total = o?.PlTotal ?? positions.NetPositions.Sum(p => p.Pl),
            openPositions = o?.CountOpen ?? positions.NetPositions.Count(p => p.NetQty != 0),
            totalPositions = o?.CountTotal ?? positions.NetPositions.Count,
        }, onlyIfChanged: true));

        foreach (var p in positions.NetPositions)
            Count(bus.Publish("fyers.positions", p.Id ?? $"{p.Symbol}-{p.ProductType}", new
            {
                symbol = p.Symbol, productType = p.ProductType, side = p.Side, netQty = p.NetQty, netAvg = p.NetAvg,
                buyQty = p.BuyQty, buyAvg = p.BuyAvg, sellQty = p.SellQty, sellAvg = p.SellAvg, ltp = p.Ltp,
                realized = p.RealizedProfit, unrealized = p.UnrealizedProfit, pl = p.Pl,
            }, onlyIfChanged: true));

        var funds = await fyers.Funds();
        foreach (var f in funds.FundLimit)
            Count(bus.Publish("fyers.funds", f.Title ?? f.Id.ToString(), new { id = f.Id, title = f.Title, equity = f.EquityAmount, commodity = f.CommodityAmount },
                onlyIfChanged: true));

        var orders = await fyers.Orders();
        foreach (var x in orders.OrderBook)
            Count(bus.Publish("fyers.orders", x.Id ?? "?", new
            {
                symbol = x.Symbol, side = x.Side == 1 ? "BUY" : "SELL", qty = x.Qty, filledQty = x.FilledQty,
                remainingQty = x.RemainingQuantity, status = OrderStatus.GetValueOrDefault(x.Status, x.Status.ToString()),
                type = x.Type, productType = x.ProductType,
                price = x.TradedPrice != 0 ? x.TradedPrice : x.LimitPrice, stopPrice = x.StopPrice,
                message = x.Message, orderTag = x.OrderTag, orderTime = x.OrderDateTime, exchangeOrderId = x.ExchOrdId,
            }, onlyIfChanged: true));

        var trades = await fyers.Trades();
        foreach (var t in trades.TradeBook)
            Count(bus.Publish("fyers.trades", t.TradeNumber ?? $"{t.OrderNumber}-{t.OrderDateTime}", new
            {
                orderId = t.OrderNumber, symbol = t.Symbol, side = t.Side == 1 ? "BUY" : "SELL", qty = t.TradedQty,
                price = t.TradePrice, value = t.TradeValue, productType = t.ProductType, tradeTime = t.OrderDateTime,
            }, onlyIfChanged: true));

        var holdings = await fyers.Holdings();
        foreach (var h in holdings.Holdings)
            Count(bus.Publish("fyers.holdings", h.Symbol ?? "?", new
            { qty = h.Quantity, costPrice = h.CostPrice, ltp = h.Ltp, marketValue = h.MarketVal, pl = h.Pl },
                onlyIfChanged: true));

        if (!string.IsNullOrWhiteSpace(_quoteSymbols))
        {
            var quotes = await fyers.Quotes(_quoteSymbols);
            foreach (var q in quotes.D.Where(q => q.V is not null))
                Count(bus.Publish("fyers.quotes", q.N ?? "?", new
                {
                    name = q.V!.ShortName, ltp = q.V.Lp, change = q.V.Ch, changePct = q.V.Chp, open = q.V.Open,
                    high = q.V.High, low = q.V.Low, prevClose = q.V.PrevClose, volume = q.V.Volume,
                }, onlyIfChanged: true));
        }
    }

    private void Count(bool published)
    {
        if (!published) return;
        _changes++;
        _lastChange = DateTime.UtcNow;
    }
}
