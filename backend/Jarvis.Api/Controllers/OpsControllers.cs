using Jarvis.Api.Data;
using Jarvis.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace Jarvis.Api.Controllers;

[ApiController]
[Route("api/console/activity")]
public class ActivityController(MockDataStore store) : ControllerBase
{
    /// <summary>Overview → "Today's activity" (signals per hour + order funnel).</summary>
    [HttpGet("today")]
    public TodayActivity Today() => store.GetTodayActivity();
}

// ======================================================================= Positions & Exits

[ApiController]
[Route("api/console/positions")]
public class PositionsController(MockDataStore store) : ControllerBase
{
    [HttpGet("summary")]
    public PositionsTabSummary Summary()
    {
        lock (store.SyncRoot)
            return new(store.InFlightExits.Count, store.CleanupItems.Count, store.Hedges.Count, store.AutoCloseLog.Count, store.Accounts.Count);
    }

    [HttpGet("accounts")]
    public List<AccountListItem> Accounts(
        [FromQuery] string? search, [FromQuery] string? broker, [FromQuery] string? clientId, [FromQuery] string? partnerId,
        [FromQuery] bool openOnly = false, [FromQuery] string sort = "client")
    {
        lock (store.SyncRoot)
        {
            IEnumerable<BrokerAccount> q = store.Accounts;
            if (!string.IsNullOrWhiteSpace(search))
                q = q.Where(a => a.ClientId.Contains(search, StringComparison.OrdinalIgnoreCase)
                              || a.AccountId.Contains(search, StringComparison.OrdinalIgnoreCase)
                              || a.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                              || a.Positions.Any(p => p.Symbol.Contains(search, StringComparison.OrdinalIgnoreCase)));
            if (!string.IsNullOrWhiteSpace(broker)) q = q.Where(a => a.Broker == broker);
            if (!string.IsNullOrWhiteSpace(clientId)) q = q.Where(a => a.ClientId == clientId.Trim());
            if (!string.IsNullOrWhiteSpace(partnerId)) q = q.Where(a => a.AccountId.Contains(partnerId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (openOnly) q = q.Where(a => a.OpenPositions > 0);
            q = sort switch
            {
                "open" => q.OrderByDescending(a => a.OpenPositions),
                "broker" => q.OrderBy(a => a.Broker).ThenBy(a => a.ClientId, StringComparer.Ordinal),
                _ => q.OrderBy(a => a.ClientId, StringComparer.Ordinal),
            };
            return q.Select(MockDataStore.ToListItem).ToList();
        }
    }

    [HttpGet("accounts/{clientId}")]
    public ActionResult<AccountDetail> Account(
        string clientId, [FromQuery] string? status, [FromQuery] string? symbol, [FromQuery] int? strategyId,
        [FromQuery] string? correlation, [FromQuery] string? product)
    {
        lock (store.SyncRoot)
        {
            var a = store.Accounts.FirstOrDefault(x => x.ClientId == clientId);
            if (a is null) return NotFound();
            IEnumerable<PositionRecord> p = a.Positions;
            if (!string.IsNullOrWhiteSpace(status)) p = p.Where(x => x.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(symbol)) p = p.Where(x => x.Symbol.Contains(symbol, StringComparison.OrdinalIgnoreCase));
            if (strategyId.HasValue) p = p.Where(x => x.StrategyId == strategyId);
            if (!string.IsNullOrWhiteSpace(correlation)) p = p.Where(x => x.Correlation.Contains(correlation, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(product)) p = p.Where(x => x.Product == product);
            return new AccountDetail(MockDataStore.ToListItem(a), p.ToList(), a.Orders.ToList());
        }
    }

    [HttpGet("inflight")] public List<InFlightExit> InFlight() { lock (store.SyncRoot) return store.InFlightExits.OrderByDescending(x => x.Since).ToList(); }
    [HttpGet("cleanup")] public List<CleanupItem> Cleanup() { lock (store.SyncRoot) return store.CleanupItems.OrderBy(x => x.Since).ToList(); }
    [HttpGet("hedges")] public List<HedgeRecord> Hedges() { lock (store.SyncRoot) return store.Hedges.OrderBy(x => x.Status == "PROTECTED").ToList(); }
    [HttpGet("auto-close")] public List<AutoCloseEntry> AutoClose() { lock (store.SyncRoot) return store.AutoCloseLog.ToList(); }

    /// <summary>Mock force-exit of one position (squares it off at LTP).</summary>
    [HttpPost("accounts/{clientId}/{positionId}/exit")]
    public IActionResult ExitPosition(string clientId, string positionId)
    {
        lock (store.SyncRoot)
        {
            var p = store.Accounts.FirstOrDefault(x => x.ClientId == clientId)?.Positions.FirstOrDefault(x => x.Id == positionId);
            if (p is null) return NotFound();
            if (p.NetQty == 0) return NoContent();
            if (p.NetQty > 0) { p.Realized += (p.Ltp - p.AvgBuy) * p.NetQty; p.AvgSell = p.Ltp; p.SellQty = p.BuyQty; }
            else { p.Realized += (p.AvgSell - p.Ltp) * -p.NetQty; p.AvgBuy = p.Ltp; p.BuyQty = p.SellQty; }
            p.Unrealized = 0; p.PnlPct = null; p.Side = "FLAT"; p.Status = "CLOSED";
            p.ClosedAt = MockDataStore.NowIst; p.LatestExitId = $"FORCE-EXIT-{Guid.NewGuid():N}"[..21];
            return NoContent();
        }
    }
}

// ======================================================================= Performance

[ApiController]
[Route("api/console/performance")]
public class PerformanceController(MockDataStore store) : ControllerBase
{
    [HttpGet("summary")]
    public PerformanceSummary Summary(
        [FromQuery] string? basket, [FromQuery] string? partnerId, [FromQuery] string? name, [FromQuery] string? broker,
        [FromQuery] string sort = "name", [FromQuery] int pageSize = 25)
    {
        lock (store.SyncRoot)
        {
            IEnumerable<BrokerAccount> q = store.Accounts;
            if (!string.IsNullOrWhiteSpace(basket)) q = q.Where(a => a.Basket == basket);
            if (!string.IsNullOrWhiteSpace(partnerId)) q = q.Where(a => a.AccountId.Contains(partnerId, StringComparison.OrdinalIgnoreCase) || a.ClientId == partnerId);
            if (!string.IsNullOrWhiteSpace(name)) q = q.Where(a => a.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(broker)) q = q.Where(a => a.Broker == broker);
            var scope = q.ToList();
            var page = (sort switch
            {
                "pnl" => scope.OrderByDescending(a => a.StrategyPnl),
                "pnl-asc" => scope.OrderBy(a => a.StrategyPnl),
                _ => scope.OrderBy(a => a.Name),
            }).Take(pageSize).ToList();
            return new PerformanceSummary(
                page.Sum(a => a.StrategyPnl), page.Count, scope.Count,
                page.Count(a => a.Status == "Active"), page.Count(a => a.SessionValid),
                page.Sum(a => a.OpenPositions), page.Select(MockDataStore.ToListItem).ToList());
        }
    }

    [HttpGet("users/{clientId}")]
    public ActionResult<UserPerformanceDetail> User(string clientId, [FromQuery] int trades = 45)
    {
        lock (store.SyncRoot)
        {
            var a = store.Accounts.FirstOrDefault(x => x.ClientId == clientId);
            if (a is null) return NotFound();
            var closed = a.Positions.Where(p => p.NetQty == 0).ToList();
            return new UserPerformanceDetail(
                MockDataStore.ToListItem(a), a.StrategyPnl, a.Realized, a.Unrealized,
                closed.Count, a.OpenPositions, closed.Count(p => p.Realized > 0), closed.Count(p => p.Realized < 0),
                MockDataStore.NowIst.AddMinutes(-2),
                a.Positions.OrderBy(p => p.NetQty == 0).ThenByDescending(p => p.CreatedAt).ToList(),
                a.Trades.Take(trades).ToList(), a.Trades.Count);
        }
    }
}

// ======================================================================= Infrastructure

[ApiController]
[Route("api/console/infrastructure")]
public class InfrastructureController(MockDataStore store) : ControllerBase
{
    [HttpGet("containers")]
    public ContainerSummary Containers() => store.GetContainers();

    [HttpPost("containers/{name}/restart")]
    public IActionResult Restart(string name)
    {
        lock (store.SyncRoot)
        {
            var c = store.Containers.FirstOrDefault(x => x.Name == name);
            if (c is null) return NotFound();
            c.Status = "Running"; c.Health = "Healthy"; c.Heartbeat = true; c.Restarts++; c.RestartsToday++; c.StreamMsgs = 0;
            return NoContent();
        }
    }

    [HttpPost("containers/{name}/stop")]
    public IActionResult Stop(string name)
    {
        lock (store.SyncRoot)
        {
            var c = store.Containers.FirstOrDefault(x => x.Name == name);
            if (c is null) return NotFound();
            c.Status = "Stopped"; c.Heartbeat = false; c.CpuPct = 0;
            return NoContent();
        }
    }

    [HttpPost("containers/provision-all")]
    public IActionResult ProvisionAll()
    {
        lock (store.SyncRoot)
            foreach (var c in store.Containers.Where(c => c.Status != "Running")) { c.Status = "Running"; c.Health = "Healthy"; c.Heartbeat = true; c.CpuPct = 2; }
        return NoContent();
    }

    [HttpPost("containers/stop-all")]
    public IActionResult StopAll()
    {
        lock (store.SyncRoot)
            foreach (var c in store.Containers) { c.Status = "Stopped"; c.Heartbeat = false; c.CpuPct = 0; }
        return NoContent();
    }

    [HttpPost("contract-master/reload")]
    public IActionResult ReloadContractMaster() => NoContent();
}
