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
        var items = store.Subscriptions.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PagedResult<SubscriptionItem>(items, store.Subscriptions.Count, page, pageSize);
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
    /// <param name="filter">all | entry | exit | exit-pending | exit-completed | partial-exit | chunked</param>
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
            if (!string.IsNullOrWhiteSpace(strategy)) q = q.Where(s => s.StrategyName == strategy);
            q = filter switch
            {
                "entry" => q.Where(s => s.Type == "entry"),
                "exit" => q.Where(s => s.Type == "exit"),
                "exit-pending" => q.Where(s => s.ExitStatus == "EXIT PENDING"),
                "exit-completed" => q.Where(s => s.ExitStatus == "EXIT COMPLETED"),
                "partial-exit" => q.Where(s => s.Exited > 0 && s.Exited < s.Filled),
                "chunked" => q.Where(s => s.Chunked),
                _ => q
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
            return new SignalListResponse(list.Count, openEntry, list.Take(take).Select(MockDataStore.ToListItem).ToList());
        }
    }

    [HttpGet("filters")]
    public object Filters()
    {
        lock (store.SyncRoot)
        {
            return new
            {
                baskets = store.Signals.Select(s => s.Basket).Distinct().Order().ToList(),
                strategies = store.Signals.Select(s => s.StrategyName).Distinct().Order().ToList()
            };
        }
    }

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
            var filtered = q.ToList();

            var failed = s.Accounts.Count(a => a.Hedge != null || a.OrderStatus != "COMPLETED");
            return new SignalDetailResponse(
                MockDataStore.ToListItem(s),
                Success: s.Accounts.Count - failed,
                Failed: failed,
                Pending: s.Accounts.Count(a => a.Pending > 0),
                Open: s.OpenAccounts,
                Closed: s.ClosedAccounts,
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
            s.Status = "Closed";
            s.ExitStatus = "EXIT COMPLETED";
            return NoContent();
        }
    }
}
