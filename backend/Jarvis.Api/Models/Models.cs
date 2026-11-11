namespace Jarvis.Api.Models;

// ---------- Overview ----------

public record CountCard(int Value, Dictionary<string, int> Tags);

public record OverviewSummary(
    DateOnly RangeFrom,
    DateOnly RangeTo,
    int DeadLettersInRange,
    int FailedExecutionsInRange,
    RelayStatus Relay,
    StrategyPerformanceCard StrategyPerformance,
    UserPerformanceCard UserPerformance,
    ContainersCard Containers,
    InFlightExitsCard InFlightExits,
    OmsSignalsCard OmsSignals,
    PositionTrackingCard PositionTracking,
    LogsErrorsCard LogsErrors,
    BrokersCard Brokers,
    HealthCard Health,
    SessionsCard Sessions,
    SubscriptionsCard Subscriptions);

public record SessionsCard(int Valid, int Total, int NotRefreshed, int NoToken);
public record SubscriptionsCard(int Ending7d, int Today, int Within3d, int Within30d);

public record RelayStatus(bool Live, double DispatchPct, double ExecPct, double DlqPct, string? Lag);
public record StrategyPerformanceCard(int Total, int Profitable, int Losing);
public record UserPerformanceCard(int Users, int Accounts);
public record ContainersCard(int Running, int Total, int Healthy, int Unhealthy);
public record InFlightExitsCard(int Total, int Pending, int Accounts);
public record OmsSignalsCard(int Today, int InProgress, int Failed, int Accounts);
public record PositionTrackingCard(int Total, int OpenedToday, int ClosedToday, int LoadedAccounts);
public record LogsErrorsCard(int Total, int Errors, int DeadLetters);
public record BrokersCard(int Active, int Total);
public record HealthCard(double SuccessRate);

// ---------- Alerts ----------

public class AlertRejection
{
    public required string Id { get; init; }
    public DateTime Time { get; init; }
    public required string Order { get; init; }
    public int StrategyId { get; init; }
    public string Status { get; init; } = "REJECTED";
    public required string Reason { get; init; }
    public int Qty { get; init; }
    public int Attempts { get; init; }
    public bool Acknowledged { get; set; }
}

public class AlertAccount
{
    public required string Id { get; init; }
    public required string Broker { get; init; }
    public required string AccountId { get; init; }
    public int Open => Rejections.Count(r => !r.Acknowledged);
    public int Total => Rejections.Count;
    public required string Symbol { get; init; }
    public required string ReasonCode { get; init; }   // MARGIN_SHORTFALL / BROKER_REJECTION
    public int ExtraSymbols { get; init; }
    public required string Message { get; init; }
    public DateTime LastTime { get; set; }
    public List<AlertRejection> Rejections { get; init; } = new();
}

public record BrokerAlertSummary(string Broker, int Open, int Accounts, int Rejections);

public record AlertsResponse(
    DateTime CheckedAt,
    int OpenCount,
    int CriticalCount,
    List<BrokerAlertSummary> ByBroker,
    int TotalAccounts,
    List<AlertAccount> Accounts);

// ---------- Subscriptions / Sessions ----------

public record SubscriptionItem(
    string Name, string Broker, string AccountId, string Plan, DateTime EndsAt, int DaysLeft);

public record PagedResult<T>(List<T> Items, int Total, int Page, int PageSize);

public record BrokerSessionHealth(
    int LiveToday, int TotalSessions, int ExcludedDelta, List<SessionIssue> NotRefreshed);

public record SessionIssue(string Name, string Broker, string AccountId, DateTime? LastRefreshed);

// ---------- Signals ----------

public class Signal
{
    public long Id { get; init; }
    public DateTime ReachedApiAt { get; init; }
    public required string Symbol { get; init; }
    public required string Exchange { get; init; }      // BFO / NFO
    public required string Segment { get; init; }       // S1EN
    public long StrategyRef { get; init; }              // #1134486
    public required string StrategyName { get; init; }  // real strategy name, e.g. B6_BK_STBT (Data/Catalog.cs)
    public required string Basket { get; init; }
    public required string Side { get; init; }          // SHORT / LONG
    public required string Type { get; init; }          // entry / exit
    public required string Status { get; set; }         // Open / Closed
    public required string ExitStatus { get; set; }     // EXIT PENDING / EXIT COMPLETED / PARTIAL EXIT
    public bool Chunked { get; set; }
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString();
    public List<SignalAccountExecution> Accounts { get; init; } = new();

    // Derived aggregates
    public int Requested => Accounts.Sum(a => a.Req);
    public int Filled => Accounts.Sum(a => a.Fill);
    public int Exited => Accounts.Sum(a => a.Exit);
    public int RejectedOrUnfilled => Accounts.Sum(a => a.Req - a.Fill);
    public int Errors => Accounts.Sum(a => a.Hedge?.ShortOrders.Count(s => s.Status == "FAILED") ?? 0)
                         + Accounts.Count(a => a.OrderStatus == "REJECTED");
    public int OpenAccounts => Accounts.Count(a => a.NetOpen > 0);
    public int ClosedAccounts => Accounts.Count(a => a.NetOpen == 0 && a.Exit > 0);
    public int TotalAccounts => Accounts.Count;

    // ---- truth derived from the accounts (the recorded Status/ExitStatus can be wrong)
    public string? ExitCorrelationId { get; set; }
    /// <summary>An exit was sent for this signal (exit-type signal, or the OMS recorded the exit as done).</summary>
    public bool ExitSent => Type == "exit" || ExitStatus == "EXIT COMPLETED";
    /// <summary>Qty still open after an exit was sent — the OMS believes these are flat.</summary>
    public int ResidualQty => ExitSent ? Accounts.Sum(a => a.NetOpen) : 0;
    public int ResidualAccounts => ExitSent ? Accounts.Count(a => a.NetOpen > 0) : 0;
    public int AnomalyCount => Accounts.Sum(a => a.Anomalies.Count);
    public int ChunksTotal => Accounts.Sum(a => a.ChunkOrders.Count);
    public int ChunksDone => Accounts.Sum(a => a.ChunkOrders.Count(c => c.Fill >= c.Req));
    /// <summary>
    /// Problems a human still has to act on. Hedge short orders that failed once but were re-placed and filled
    /// are counted in <see cref="Errors"/> (history) but are NOT unresolved.
    /// </summary>
    public int UnresolvedIssues => Accounts.Count(a => a.OrderStatus is "REJECTED" or "FAILED" or "PARTIAL") + AnomalyCount;
    public bool NeedsAttention => ResidualQty > 0 || UnresolvedIssues > 0;
    /// <summary>Status follows the worst account: any residual ⇒ partial, never "completed".</summary>
    public string EffectiveStatus => ResidualQty > 0 ? "Partial" : Status;
    public string EffectiveExitStatus => ResidualQty > 0 ? "EXIT PARTIAL" : ExitStatus;
    public string Health => ResidualQty > 0 ? "residual" : UnresolvedIssues > 0 ? "error" : Status == "Open" ? "open" : "ok";
    public HedgeRollup? HedgeRollup
    {
        get
        {
            var h = Accounts.Where(a => a.Hedge != null).Select(a => a.Hedge!).ToList();
            if (h.Count == 0) return null;
            var buyReq = h.SelectMany(x => x.HedgeOrders).Sum(o => o.Req);
            var buyFill = h.SelectMany(x => x.HedgeOrders).Sum(o => o.Fill);
            var sellReq = h.Sum(x => x.Squared);
            return new HedgeRollup(buyFill, buyReq, sellReq, sellReq, buyFill - sellReq);
        }
    }
}

public record HedgeRollup(int BuyFilled, int BuyRequested, int SellFilled, int SellRequested, int Open);

public class SignalAccountExecution
{
    public required string ClientId { get; init; }
    public required string Broker { get; init; }
    public required string BrokerAccount { get; init; }
    public required string BrokerOrderId { get; init; }
    public string OrderStatus { get; set; } = "COMPLETED";
    public int? Chunks { get; init; }
    public int Req { get; init; }
    public int Fill { get; set; }
    public int Pending { get; set; }
    public int Rejected { get; set; }
    public int Cancelled { get; set; }
    public int Exit { get; set; }
    public int NetOpen => Fill - Exit;
    public decimal AvgEntry { get; init; }
    public decimal? AvgExit { get; set; }
    public int Retry { get; init; }
    public int? Modify { get; init; }
    public string? RejectReason { get; init; }
    public int? Attempt { get; init; }
    public HedgeDetail? Hedge { get; init; }

    /// <summary>Exit chunk orders (large quantities are split into freeze-limit sized chunks).</summary>
    public List<ChunkOrder> ChunkOrders { get; set; } = new();
    public string? PartialReason { get; set; }

    /// <summary>Records that contradict each other — shown with ⚠ instead of a green pill.</summary>
    public List<string> Anomalies => ChunkOrders.SelectMany(c => c.Anomalies(ChunkOrders.Count)).ToList();
    /// <summary>0 = worst. Partial → failed/rejected → pending → completed.</summary>
    public int Severity => OrderStatus switch { "PARTIAL" => 0, "FAILED" or "REJECTED" => 1, "PENDING" => 2, _ => Anomalies.Count > 0 ? 2 : 3 };
}

public class ChunkOrder
{
    public int Index { get; init; }
    public string TaskStatus { get; set; } = "EXECUTED";   // OMS task state
    public string BrokerStatus { get; set; } = "COMPLETED"; // what the broker reported
    public int Req { get; init; }
    public int Fill { get; set; }
    public int Unfilled => Req - Fill;
    public int Attempts { get; set; } = 1;
    public int? Modify { get; set; }
    public string? BrokerOrder { get; set; }
    public string? Reason { get; set; }

    public IEnumerable<string> Anomalies(int total)
    {
        if (BrokerStatus == "COMPLETED" && Fill == 0 && Req > 0)
            yield return $"Chunk {Index}/{total}: broker says COMPLETED but 0 filled";
        if (TaskStatus == "EXECUTED" && BrokerOrder is null)
            yield return $"Chunk {Index}/{total}: no broker order id — never reached the broker";
    }
}

public class HedgeDetail
{
    public required string Summary { get; init; }
    public int Squared { get; init; }
    public List<ShortOrderLeg> ShortOrders { get; init; } = new();
    public List<HedgeOrderLeg> HedgeOrders { get; init; } = new();
}

public record ShortOrderLeg(
    string Label, string Status, int Req, int Fill, int Unfilled, int Attempts,
    string BrokerOrder, string PremiumBought, DateTime Completed);

public record HedgeOrderLeg(
    string Leg, string Side, string Instrument, string Status, int Req, int Fill,
    string BrokerOrder, string? Error);

public record SignalListItem(
    long Id, DateTime ReachedApiAt, string Symbol, string Exchange, string Segment,
    long StrategyRef, string StrategyName, string Side, string Type, string Status,
    string ExitStatus, int Errors, int Requested, int Filled, int Exited,
    int RejectedOrUnfilled, HedgeRollup? Hedge, int OpenAccounts, int ClosedAccounts,
    int TotalAccounts, string CorrelationId,
    // derived truth
    string? ExitCorrelationId, string RecordedExitStatus, string Health, bool NeedsAttention,
    int ResidualQty, int ResidualAccounts, int AnomalyCount, int ChunksDone, int ChunksTotal, int OpenQty, int UnresolvedIssues);

public record SignalListResponse(int Total, int OpenEntrySignals, int AttentionCount, List<SignalListItem> Items);

public record AttentionItem(string ClientId, string Broker, string BrokerAccount, string Issue, int Qty);
public record StatusCount(string Status, int Count);

public record SignalDetailResponse(
    SignalListItem Signal, int Success, int Partial, int Failed, int Pending, int OpenAccounts, int OpenQty, int Closed,
    List<StatusCount> StatusCounts, List<AttentionItem> Attention,
    PagedResult<SignalAccountExecution> Accounts, List<string> Brokers);
