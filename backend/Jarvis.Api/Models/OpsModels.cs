namespace Jarvis.Api.Models;

// ---------------- Accounts / positions (Positions & Exits, Performance) ----------------

public class BrokerAccount
{
    public required string ClientId { get; init; }
    public required string Name { get; init; }
    public required string Broker { get; init; }
    public required string AccountId { get; init; }
    public required string Basket { get; init; }
    public string Status { get; init; } = "Active";
    public bool SessionValid { get; set; } = true;
    public bool TokenStale { get; init; }
    public int Strategies { get; init; }
    public int ExcludedStrategies { get; init; }
    public List<PositionRecord> Positions { get; init; } = new();
    public List<OrderRecord> Orders { get; init; } = new();
    public List<TradeRecord> Trades { get; init; } = new();

    public int OpenPositions => Positions.Count(p => p.NetQty != 0);
    public int ClosedToday => Positions.Count(p => p.NetQty == 0);
    public decimal Realized => Positions.Sum(p => p.Realized);
    public decimal Unrealized => Positions.Sum(p => p.Unrealized);
    public decimal StrategyPnl => Realized + Unrealized;
}

public class PositionRecord
{
    public required string Id { get; init; }
    public required string Side { get; set; }          // BUY / SELL / FLAT (current net direction)
    public required string EntrySide { get; init; }    // BUY / SELL (how the position was opened)
    public string Exchange { get; init; } = "NFO";
    public required string Symbol { get; init; }        // NIFTY09NOV26P24950
    public required string DisplaySymbol { get; init; } // NIFTY 09 Nov 2026 PE 24950.00
    public int StrategyId { get; init; }
    public required string Strategy { get; init; }      // S1EN
    public string Product { get; init; } = "I";         // I = intraday, D = delivery/NRML
    public int BuyQty { get; set; }
    public int SellQty { get; set; }
    public int NetQty => BuyQty - SellQty;
    public decimal AvgBuy { get; set; }
    public decimal AvgSell { get; set; }
    public decimal Ltp { get; set; }
    public decimal Realized { get; set; }
    public decimal Unrealized { get; set; }
    public decimal? PnlPct { get; set; }
    public required string Status { get; set; }         // OPEN / CLOSED / RISK
    public bool Stale { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ClosedAt { get; set; }
    public required string EntrySignalId { get; init; }
    public required string Correlation { get; init; }
    public string? LatestExitId { get; set; }
    public required string Token { get; init; }
    public required string OrderTag { get; init; }
}

public record OrderRecord(
    string OrderId, DateTime Time, string Symbol, string Side, string Kind, int Qty, int FilledQty,
    decimal Price, string Status, string Correlation, string? Message);

public record TradeRecord(
    string TradeId, string Symbol, string Strategy, string EntrySide, string ExitSide, int EntryQty, int ExitQty,
    int NetQty, string Duration, string Broker, string AccountId, DateTime EntryTime, DateTime? ExitTime,
    decimal? Pnl, string Status, string Correlation);

public record InFlightExit(
    string Id, string ClientId, string Broker, string AccountId, string Symbol, int Qty, string Stage,
    int Attempts, DateTime Since, string Reason, string Correlation);

public record CleanupItem(
    string ClientId, string Broker, string AccountId, string Symbol, string Issue, int OmsQty, int BrokerQty, DateTime Since);

public record HedgeRecord(
    string ClientId, string Broker, string AccountId, string MainSymbol, int MainQty,
    string CeLeg, int CeQty, string PeLeg, int PeQty, string Status);

public record AutoCloseEntry(
    DateTime Time, string ClientId, string Broker, string AccountId, string Symbol, int Qty, string Rule, string Result, string Message);

public record AccountListItem(
    string ClientId, string Name, string Broker, string AccountId, string Status, bool SessionValid, bool TokenStale,
    int OpenPositions, int ClosedToday, int Strategies, int ExcludedStrategies,
    decimal StrategyPnl, decimal Realized, decimal Unrealized);

public record PositionsTabSummary(int InFlightExits, int NeedsCleanup, int Hedges, int AutoClosed, int AccountsLoaded);

public record AccountDetail(AccountListItem Account, List<PositionRecord> Positions, List<OrderRecord> Orders);

// ---------------- Performance ----------------

public record PerformanceSummary(
    decimal StrategyPnl, int AccountsOnPage, int AccountsInScope, int ActiveAccounts, int ConnectedAccounts,
    int OpenPositions, List<AccountListItem> Users, decimal ScopePnl = 0);   // ScopePnl = all accounts in scope, not just this page

public record UserPerformanceDetail(
    AccountListItem Account, decimal Total, decimal Realized, decimal Unrealized, int Closed, int Open,
    int Wins, int Losses, DateTime MarkedAt, List<PositionRecord> Positions, List<TradeRecord> Trades, int TradesTotal);

// ---------------- Infrastructure ----------------

public class ContainerInfo
{
    public required string Name { get; init; }
    public string Status { get; set; } = "Running";     // Running / Stopped
    public string Health { get; set; } = "Healthy";     // Healthy / Unhealthy / Degraded
    public required string PrivateIp { get; init; }
    public required string StaticPublicIp { get; init; }
    public bool NicMappingValid { get; init; } = true;
    public bool RoutingValid { get; init; } = true;
    public required string ClientId { get; init; }
    public required string Broker { get; init; }
    public required string Accounts { get; init; }
    public double CpuPct { get; set; }
    public double MemPct { get; set; }
    public int MemMb { get; set; }
    public double RedisLatencyMs { get; set; }
    public double MongoLatencyMs { get; set; }
    public bool RedisOk { get; set; } = true;
    public bool MongoOk { get; set; } = true;
    public bool LtpFeed { get; set; } = true;
    public bool Streams { get; set; } = true;
    public int StreamMsgs { get; set; }
    public int Restarts { get; set; }
    public int RestartsToday { get; set; }
    public bool Heartbeat { get; set; } = true;
}

public record ContainerSummary(
    int Total, int Running, int Stopped, int Healthy, int Unhealthy, int NoHeartbeat, int MongoConnected,
    int RedisConnected, int RoutingOk, double AvgCpu, double AvgMemory,
    int ContractMasterRows, DateTime ContractMasterLoadedAt, DateTime CheckedAt, List<ContainerInfo> Containers);

// ---------------- Overview additions ----------------

public record HourBucket(int Hour, int Signals);
public record ActivityFunnel(int Signals, int Orders, int Filled, int Rejected, int Working);
public record TodayActivity(List<HourBucket> Hours, ActivityFunnel Funnel);
