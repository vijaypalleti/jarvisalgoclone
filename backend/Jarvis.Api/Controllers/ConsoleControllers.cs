using Jarvis.Api.Data;
using Jarvis.Api.Models;
using Jarvis.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jarvis.Api.Controllers;

[ApiController]
[Route("api/console/overview")]
public class OverviewController(MockDataStore store, FyersClient fyers) : ControllerBase
{
    [HttpGet]
    public async Task<OverviewSummary> Get()
    {
        var o = store.GetOverview();
        if (!fyers.Configured) return o;
        var active = 0;
        try { await fyers.Profile(); active = 1; } catch (Exception) { /* token expired / unreachable */ }
        return o with { Brokers = new BrokersCard(active, 1) };
    }
}

[ApiController]
[Route("api/console/alerts")]
public class AlertsController(MockDataStore store, FyersClient fyers, FyersAlertState fyersAcks) : ControllerBase
{
    /// <summary>Rejected orders from the live Fyers order book, shaped like the console's alert accounts.</summary>
    private async Task<AlertAccount?> LiveFyersAlerts()
    {
        if (!fyers.Configured) return null;
        try
        {
            var orders = (await fyers.Orders()).OrderBook.Where(o => o.Status == 5).ToList();
            if (orders.Count == 0) return null;
            var fyId = (await fyers.Profile()).Data?.FyId ?? fyers.ClientId ?? "FYERS";
            DateTime Parse(string? s) => DateTime.TryParse(s, out var d) ? d : MockDataStore.NowIst;
            var latest = orders.OrderByDescending(o => Parse(o.OrderDateTime)).First();
            var msg = latest.Message ?? "Rejected";
            return new AlertAccount
            {
                Id = "fyers-live",
                Broker = "FYERS",
                AccountId = $"{fyId} · LIVE",
                Symbol = latest.Symbol ?? "",
                ReasonCode = msg.Contains("margin", StringComparison.OrdinalIgnoreCase) || msg.Contains("fund", StringComparison.OrdinalIgnoreCase)
                    ? "MARGIN_SHORTFALL" : "BROKER_REJECTION",
                ExtraSymbols = Math.Max(0, orders.Select(o => o.Symbol).Distinct().Count() - 1),
                Message = msg,
                LastTime = Parse(latest.OrderDateTime),
                Rejections = orders.Select(o => new AlertRejection
                {
                    Id = $"fyers-{o.Id}",
                    Time = Parse(o.OrderDateTime),
                    Order = $"{FyersController.Side(o.Side)} {FyersController.OrderType(o.Type)} {o.Symbol}",
                    StrategyId = 0,
                    Reason = o.Message ?? "Rejected",
                    Qty = o.Qty,
                    Attempts = 1,
                    Acknowledged = fyersAcks.Acked.ContainsKey($"fyers-{o.Id}")
                }).ToList()
            };
        }
        catch (Exception) { return null; }
    }

    [HttpGet]
    public async Task<AlertsResponse> Get([FromQuery] string? search, [FromQuery] int take = 20)
    {
        var live = await LiveFyersAlerts();
        lock (store.SyncRoot)
        {
            IEnumerable<AlertAccount> q = live is null ? store.AlertAccounts : store.AlertAccounts.Prepend(live);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                q = q.Where(a => a.AccountId.Contains(s, StringComparison.OrdinalIgnoreCase)
                              || a.Broker.Contains(s, StringComparison.OrdinalIgnoreCase)
                              || a.Symbol.Contains(s, StringComparison.OrdinalIgnoreCase));
            }
            var list = q.ToList();
            var all = live is null ? store.AlertAccounts : store.AlertAccounts.Prepend(live).ToList();
            var byBroker = all
                .GroupBy(a => a.Broker)
                .Select(g => new BrokerAlertSummary(g.Key, g.Sum(a => a.Open), g.Count(), g.Sum(a => a.Total)))
                .OrderByDescending(b => b.Open).ThenByDescending(b => b.Accounts)
                .ToList();
            var open = all.Sum(a => a.Open);
            return new AlertsResponse(MockDataStore.NowIst, open, open, byBroker, list.Count, list.Take(take).ToList());
        }
    }

    [HttpPost("{accountId}/ack")]
    public async Task<IActionResult> AckAccount(string accountId)
    {
        if (accountId == "fyers-live")
        {
            var live = await LiveFyersAlerts();
            live?.Rejections.ForEach(r => fyersAcks.Acked[r.Id] = true);
            return NoContent();
        }
        lock (store.SyncRoot)
        {
            var acc = store.AlertAccounts.FirstOrDefault(a => a.Id == accountId);
            if (acc is null) return NotFound();
            acc.Rejections.ForEach(r => r.Acknowledged = true);
            return NoContent();
        }
    }

    [HttpPost("rejections/{rejectionId}/ack")]
    public IActionResult AckRejection(string rejectionId)
    {
        if (rejectionId.StartsWith("fyers-")) { fyersAcks.Acked[rejectionId] = true; return NoContent(); }
        lock (store.SyncRoot)
        {
            var r = store.AlertAccounts.SelectMany(a => a.Rejections).FirstOrDefault(x => x.Id == rejectionId);
            if (r is null) return NotFound();
            r.Acknowledged = true;
            return NoContent();
        }
    }

    [HttpPost("ack-all")]
    public async Task<IActionResult> AckAll()
    {
        (await LiveFyersAlerts())?.Rejections.ForEach(r => fyersAcks.Acked[r.Id] = true);
        lock (store.SyncRoot)
        {
            foreach (var r in store.AlertAccounts.SelectMany(a => a.Rejections)) r.Acknowledged = true;
            return NoContent();
        }
    }
}

[ApiController]
[Route("api/console/subscriptions")]
public class SubscriptionsController(MockDataStore store) : ControllerBase
{
    [HttpGet("expiring")]
    public PagedResult<SubscriptionItem> Expiring([FromQuery] int page = 1, [FromQuery] int pageSize = 5)
    {
        var all = store.ExpiringSubscriptions(7);    // "ending within 7 days" (Overview → Subscription Watch / Needs attention)
        return new PagedResult<SubscriptionItem>(all.Skip((page - 1) * pageSize).Take(pageSize).ToList(), all.Count, page, pageSize);
    }
}

[ApiController]
[Route("api/console/broker-sessions")]
public class BrokerSessionsController(MockDataStore store) : ControllerBase
{
    [HttpGet]
    public BrokerSessionHealth Get() => store.Sessions;
}

[ApiController]
[Route("api/console/signals")]
public class SignalsController(MockDataStore store) : ControllerBase
{
    /// <param name="filter">all | attention | entry | exit | exit-pending | exit-completed | partial-exit | chunked</param>
    [HttpGet]
    public SignalListResponse List(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? symbol,
        [FromQuery] string? correlationId, [FromQuery] string? client, [FromQuery] string? basket,
        [FromQuery] string? strategy, [FromQuery] string filter = "all",
        [FromQuery] string sort = "time", [FromQuery] int take = 50)
    {
        lock (store.SyncRoot)
        {
            IEnumerable<Signal> q = store.Signals;
            if (from.HasValue) q = q.Where(s => s.ReachedApiAt.Date >= from.Value.Date);
            if (to.HasValue) q = q.Where(s => s.ReachedApiAt.Date <= to.Value.Date);
            if (!string.IsNullOrWhiteSpace(symbol)) q = q.Where(s => s.Symbol.Contains(symbol, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(correlationId)) q = q.Where(s => s.CorrelationId.Contains(correlationId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(client)) q = q.Where(s => s.Accounts.Any(a => a.ClientId == client || a.BrokerAccount.Contains(client, StringComparison.OrdinalIgnoreCase)));
            if (!string.IsNullOrWhiteSpace(basket)) q = q.Where(s => s.Basket == basket);
            var strat = MockDataStore.ParseStrategies(strategy);   // one or several, comma-separated
            if (strat is not null) q = q.Where(s => strat.Contains(s.StrategyName));
            var scoped = q.ToList();   // date/symbol/client/basket scope — quick-filter counts are computed on this
            q = filter switch
            {
                "attention" => scoped.Where(s => s.NeedsAttention),
                "entry" => scoped.Where(s => s.Type == "entry"),
                "exit" => scoped.Where(s => s.Type == "exit"),
                "exit-pending" => scoped.Where(s => s.EffectiveExitStatus == "EXIT PENDING"),
                "exit-completed" => scoped.Where(s => s.EffectiveExitStatus == "EXIT COMPLETED"),
                "partial-exit" => scoped.Where(s => s.ResidualQty > 0 || (s.Exited > 0 && s.Exited < s.Filled)),
                "chunked" => scoped.Where(s => s.Chunked),
                _ => scoped
            };
            q = sort switch
            {
                "symbol" => q.OrderBy(s => s.Symbol).ThenByDescending(s => s.ReachedApiAt),
                "exchange" => q.OrderBy(s => s.Exchange).ThenByDescending(s => s.ReachedApiAt),
                "side" => q.OrderBy(s => s.Side).ThenByDescending(s => s.ReachedApiAt),
                _ => q.OrderByDescending(s => s.ReachedApiAt)
            };
            var list = q.ToList();
            var openEntry = list.Count(s => s.Type == "entry" && s.Status == "Open");
            return new SignalListResponse(list.Count, openEntry, scoped.Count(s => s.NeedsAttention),
                list.Take(take).Select(MockDataStore.ToListItem).ToList());
        }
    }

    [HttpGet("filters")]
    /// <summary>Full production catalog (Data/Catalog.cs), not just the values that happen to appear in signals.</summary>
    public object Filters() => new
    {
        baskets = Catalog.BasketNames,
        strategies = Catalog.Strategies.Select(s => s.Name).ToList(),
        strategyCatalog = Catalog.Strategies.Select(s => new { s.Name, s.Code, s.Basket }),
        basketCatalog = Catalog.Baskets,
    };

    [HttpGet("{id:long}")]
    public ActionResult<SignalDetailResponse> Detail(
        long id, [FromQuery] string? client, [FromQuery] string? account, [FromQuery] string? broker,
        [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        lock (store.SyncRoot)
        {
            var s = store.Signals.FirstOrDefault(x => x.Id == id);
            if (s is null) return NotFound();

            IEnumerable<SignalAccountExecution> q = s.Accounts;
            if (!string.IsNullOrWhiteSpace(client)) q = q.Where(a => a.ClientId.Contains(client));
            if (!string.IsNullOrWhiteSpace(account)) q = q.Where(a => a.BrokerAccount.Contains(account, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(broker)) q = q.Where(a => a.Broker == broker);
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(a => a.OrderStatus == status);
            // problems first: partial → failed/rejected → pending/anomalies → completed; then by open qty
            var filtered = q.OrderBy(a => a.Severity).ThenByDescending(a => a.NetOpen).ThenBy(a => a.ClientId, StringComparer.Ordinal).ToList();

            var partial = s.Accounts.Count(a => a.OrderStatus == "PARTIAL");
            var failed = s.Accounts.Count(a => a.OrderStatus is "REJECTED" or "FAILED");
            var pending = s.Accounts.Count(a => a.Pending > 0 || a.OrderStatus == "PENDING");

            var attention = s.Accounts
                .Where(a => a.Severity < 3 || (s.ExitSent && a.NetOpen > 0))
                .OrderBy(a => a.Severity)
                .Select(a => new AttentionItem(a.ClientId, a.Broker, a.BrokerAccount,
                    a.PartialReason ?? a.RejectReason ?? (a.Anomalies.Count > 0 ? a.Anomalies[0] : a.OrderStatus),
                    s.ExitSent ? a.NetOpen : a.Rejected))
                .ToList();

            return new SignalDetailResponse(
                MockDataStore.ToListItem(s),
                Success: s.Accounts.Count - partial - failed - pending,
                Partial: partial, Failed: failed, Pending: pending,
                OpenAccounts: s.OpenAccounts, OpenQty: s.Accounts.Sum(a => a.NetOpen),
                Closed: s.ClosedAccounts,
                StatusCounts: s.Accounts.GroupBy(a => a.OrderStatus).Select(g => new StatusCount(g.Key, g.Count()))
                                .OrderBy(c => c.Status == "COMPLETED").ToList(),
                Attention: attention,
                Accounts: new PagedResult<SignalAccountExecution>(
                    filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList(), filtered.Count, page, pageSize),
                Brokers: s.Accounts.Select(a => a.Broker).Distinct().Order().ToList());
        }
    }

    [HttpPost("{id:long}/force-exit")]
    public IActionResult ForceExit(long id)
    {
        lock (store.SyncRoot)
        {
            var s = store.Signals.FirstOrDefault(x => x.Id == id);
            if (s is null) return NotFound();
            foreach (var a in s.Accounts)
            {
                a.Exit = a.Fill;
                a.AvgExit ??= a.AvgEntry;
            }
            foreach (var a in s.Accounts) { a.ChunkOrders.ForEach(c => { c.Fill = c.Req; c.BrokerOrder ??= "FORCE-EXIT"; }); if (a.OrderStatus == "PARTIAL") a.OrderStatus = "COMPLETED"; a.PartialReason = null; }
            s.Status = "Closed";
            s.ExitStatus = "EXIT COMPLETED";
            s.ExitCorrelationId ??= Guid.NewGuid().ToString("N");
            return NoContent();
        }
    }

    /// <summary>Square off one account's residual (all unfilled chunks).</summary>
    [HttpPost("{id:long}/accounts/{clientId}/exit-remaining")]
    public IActionResult ExitRemaining(long id, string clientId)
    {
        lock (store.SyncRoot)
        {
            var s = store.Signals.FirstOrDefault(x => x.Id == id);
            var a = s?.Accounts.FirstOrDefault(x => x.ClientId == clientId);
            if (s is null || a is null) return NotFound();
            return store.ExitRemaining(s, a) ? NoContent() : Conflict(new { error = "Nothing open on this account" });
        }
    }

    /// <summary>Re-place a single unfilled chunk.</summary>
    [HttpPost("{id:long}/accounts/{clientId}/chunks/{index:int}/retry")]
    public IActionResult RetryChunk(long id, string clientId, int index)
    {
        lock (store.SyncRoot)
        {
            var s = store.Signals.FirstOrDefault(x => x.Id == id);
            var a = s?.Accounts.FirstOrDefault(x => x.ClientId == clientId);
            if (s is null || a is null) return NotFound();
            return store.RetryChunk(s, a, index) ? NoContent() : Conflict(new { error = "Chunk is already filled" });
        }
    }
}
