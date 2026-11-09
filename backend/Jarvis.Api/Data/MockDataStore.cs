using Jarvis.Api.Models;

namespace Jarvis.Api.Data;

/// <summary>
/// In-memory, deterministic (seeded) fake OMS data. Registered as a singleton,
/// so mutations (ack, force-exit, new signals) persist until the API restarts.
/// Replace with a real repository/EF Core later without touching the controllers' contracts.
/// </summary>
public partial class MockDataStore
{
    private readonly Random _rng = new(42);
    private readonly object _lock = new();

    public static DateTime NowIst => DateTime.SpecifyKind(DateTime.UtcNow.AddHours(5.5), DateTimeKind.Unspecified);

    public List<Signal> Signals { get; } = new();
    public List<AlertAccount> AlertAccounts { get; } = new();
    public List<SubscriptionItem> Subscriptions { get; } = new();
    public BrokerSessionHealth Sessions { get; private set; } = null!;
    public object SyncRoot => _lock;

    private static readonly string[] Brokers = ["5PAISA", "ANGELONE", "FYERS", "ZERODHA", "ANT", "INDIABULL", "UPSTOX", "DELTA"];
    private static readonly string[] FirstNames = ["Arjun", "Kavya", "Rohan", "Meera", "Vikram", "Ananya", "Siddharth", "Priya", "Karthik", "Divya", "Rahul", "Sneha", "Aditya", "Lakshmi", "Nikhil"];
    private static readonly string[] LastNames = ["Rao", "Sharma", "Iyer", "Reddy", "Nair", "Gupta", "Menon", "Verma", "Kulkarni", "Joshi"];
    private static readonly string[] Plans = ["DTR", "Quad Instrument Plan", "Spider Man", "Index Pro", "Weekly Hedge"];
    private static readonly string[] Baskets = ["SWL_SENSEX", "MS_SENSEX", "SWL_NIFTY", "MS_NIFTY", "BNF_WEEKLY", "FIN_HEDGE"];

    public MockDataStore()
    {
        SeedSignals();
        SeedAlerts();
        SeedSubscriptions();
        SeedSessions();
        SeedOps();
    }

    // ------------------------------------------------------------------ seeding

    private string Pick(string[] arr) => arr[_rng.Next(arr.Length)];
    private string FakeName() => $"{Pick(FirstNames)} {Pick(LastNames)}";
    private string FakeAccountNo(string broker) => broker switch
    {
        "5PAISA" => $"{_rng.Next(52_000_000, 56_000_000)}",
        "ANGELONE" => $"SIPV{_rng.Next(1000, 9999)}",
        "FYERS" => $"FA{_rng.Next(10000, 99999)}",
        "ZERODHA" => $"PNN{_rng.Next(100, 999)}",
        "ANT" => $"{_rng.Next(100000, 1999999)}",
        "INDIABULL" => $"{_rng.Next(300000, 399999)}",
        "UPSTOX" => $"{_rng.Next(1, 9)}BLAT{_rng.Next(1, 9)}",
        _ => $"DEFAULT{_rng.Next(500000, 599999)}"
    };
    /// <summary>Far OTM protective leg for a short option: strike ±~3% with the given option type.</summary>
    private static string HedgeInstrument(string symbol, string optType)
    {
        var m = System.Text.RegularExpressions.Regex.Match(symbol, @"^(?<pre>.*?)(?<strike>\d{5})(?<t>PE|CE)$");
        if (!m.Success) return $"{symbol}-{optType}-HEDGE";
        var strike = int.Parse(m.Groups["strike"].Value);
        var hedgeStrike = optType == "CE" ? strike + 2200 : strike - 4300;
        return $"{m.Groups["pre"].Value}{hedgeStrike}{optType}";
    }

    private string OrderId() => $"2611050001{_rng.Next(80000, 99999)}";

    private void SeedSignals()
    {
        var today = NowIst.Date;
        var instruments = new (string sym, string exch, decimal px)[]
        {
            ("SENSEX26N0578900PE", "BFO", 152.6m),
            ("SENSEX26N0578900CE", "BFO", 148.2m),
            ("NIFTY09NOV26P24400", "NFO", 88.4m),
            ("NIFTY09NOV26C24600", "NFO", 92.1m),
            ("NIFTY17NOV26C25100", "NFO", 61.3m),
            ("BANKNIFTY26NOV52000PE", "NFO", 210.5m),
        };

        // 24 fixed client accounts reused across signals (like real baskets)
        var clients = Enumerable.Range(0, 24).Select(i =>
        {
            var broker = i == 1 ? "ANGELONE" : i == 9 ? "FYERS" : i % 7 == 3 ? "ZERODHA" : "5PAISA";
            return (clientId: (i switch { 0 => 1360, 1 => 2, 2 => 328, _ => 8600 + i * 3 }).ToString(),
                    broker, account: FakeAccountNo(broker), lot: i == 9 ? 180 : (i % 3 == 0 ? 40 : 20));
        }).ToList();

        long id = 1;
        // 3 days of signals, ~186/day
        for (int d = 2; d >= 0; d--)
        {
            var day = today.AddDays(-d);
            int count = d == 0 ? 116 : 221; // 558 total over 3 days
            for (int n = 0; n < count; n++)
            {
                var inst = instruments[_rng.Next(instruments.Length)];
                var t = day.AddHours(9).AddMinutes(15).AddSeconds(_rng.Next(0, 6 * 3600 + 15 * 60));
                var basket = inst.sym.StartsWith("SENSEX") ? Pick(["SWL_SENSEX", "MS_SENSEX"]) : Pick(Baskets);
                var isEntry = _rng.NextDouble() < 0.8;
                var withHedgeIssues = _rng.NextDouble() < 0.3;

                var accounts = new List<SignalAccountExecution>();
                foreach (var c in clients)
                {
                    var px = inst.px + (decimal)Math.Round(_rng.NextDouble() * 1.5 - 0.5, 2);
                    var failedShort = withHedgeIssues && _rng.NextDouble() < 0.5;
                    HedgeDetail? hedge = null;
                    int fill = c.lot;
                    if (failedShort)
                    {
                        var completed = t.AddSeconds(_rng.Next(1, 30));
                        var replaced = _rng.NextDouble() < 0.7;
                        fill = replaced ? c.lot : 0;
                        hedge = new HedgeDetail
                        {
                            Summary = replaced
                                ? $"SHORT {c.lot} / {c.lot} filled · Unfilled {c.lot} · Re-placed after hedge {c.lot} · IncludesFailedChunkOrTask"
                                : $"SHORT 0 / {c.lot} filled · Unfilled {c.lot} · Re-place failed",
                            Squared = 0,
                            ShortOrders =
                            [
                                new("SHORT order", "FAILED", c.lot, 0, c.lot, 1, OrderId(), $"{c.lot} / {c.lot}", completed),
                                .. replaced
                                    ? new[] { new ShortOrderLeg("Re-placed after hedge", "COMPLETED", c.lot, c.lot, 0, 1, OrderId(), "0 / 0", completed.AddSeconds(3)) }
                                    : Array.Empty<ShortOrderLeg>()
                            ],
                            HedgeOrders =
                            [
                                new("CE", "BUY", HedgeInstrument(inst.sym, "CE"), "COMPLETED", c.lot, c.lot, OrderId(), null),
                                new("PE", "BUY", HedgeInstrument(inst.sym, "PE"), "COMPLETED", c.lot, c.lot, OrderId(), null),
                            ]
                        };
                    }
                    var exited = !isEntry || d > 0 ? fill : 0;
                    accounts.Add(new SignalAccountExecution
                    {
                        ClientId = c.clientId,
                        Broker = c.broker,
                        BrokerAccount = c.account,
                        BrokerOrderId = c.broker == "ANGELONE" ? Guid.NewGuid().ToString() : OrderId(),
                        OrderStatus = fill == 0 ? "REJECTED" : "COMPLETED",
                        Req = c.lot,
                        Fill = fill,
                        Rejected = c.lot - fill,
                        Exit = exited,
                        AvgEntry = px,
                        AvgExit = exited > 0 ? px - (decimal)Math.Round(_rng.NextDouble() * 20 - 8, 2) : null,
                        Retry = failedShort ? 1 : 0,
                        RejectReason = fill == 0 ? "Margin Shortfall: insufficient funds" : null,
                        Attempt = failedShort ? 2 : null,
                        Hedge = hedge
                    });
                }

                var allExited = accounts.All(a => a.NetOpen == 0);
                Signals.Add(new Signal
                {
                    Id = id++,
                    ReachedApiAt = t,
                    Symbol = inst.sym,
                    Exchange = inst.exch,
                    Segment = "S1EN",
                    StrategyRef = 1134400 + _rng.Next(0, 100),
                    StrategyName = basket + "_" + (inst.sym.Contains("PE") ? "PE" : "CE"),
                    Basket = basket,
                    Side = "SHORT",
                    Type = isEntry ? "entry" : "exit",
                    Status = allExited ? "Closed" : "Open",
                    ExitStatus = allExited ? "EXIT COMPLETED" : "EXIT PENDING",
                    Chunked = _rng.NextDouble() < 0.1,
                    Accounts = accounts
                });
            }
        }
    }

    private void SeedAlerts()
    {
        var reasons = new (string code, string msg)[]
        {
            ("MARGIN_SHORTFALL", "OptionsHedgeRetryFailed: REJECTED | RED:Margin Shortfall:INR {0:N2} Available:INR {1:N2}"),
            ("BROKER_REJECTION", "OptionsHedgeRetryFailed: REJECTED | RED:Margin Shortfall:INR {0:N2} Available:INR {1:N2}"),
            ("MARGIN_SHORTFALL", "OptionsHedgeRetryFailed: Rejected | OEMS:RMS : Margin Exceeds : - Set Limit:[{0:F2}] Total Required"),
            ("MARGIN_SHORTFALL", "OptionsHedgeRetryFailed: Insufficient funds. Margin required: {0:F2}. Margin available: {1:F2}"),
            ("BROKER_REJECTION", "17070 The Price is out of the current LPP range"),
            ("BROKER_REJECTION", "Trading is not allowed on this scrip due to illiquidity."),
        };
        var brokers = new[] { "ANT", "ANT", "INDIABULL", "ZERODHA", "FYERS", "5PAISA", "UPSTOX", "ANT", "FYERS", "ZERODHA" };
        var symbols = new[] { "NIFTY09NOV26P24400", "SENSEX26N0578900PE", "NIFTY17NOV26C25100" };
        var now = NowIst;

        for (int i = 0; i < 28; i++)
        {
            var broker = i < brokers.Length ? brokers[i] : Pick(["ANT", "FYERS", "ZERODHA", "5PAISA", "UPSTOX", "ANT"]);
            var reason = reasons[i < reasons.Length ? i : _rng.Next(reasons.Length)];
            var total = i switch { 0 => 17, 1 => 5, 2 => 4, 3 => 3, 4 => 6, _ => _rng.Next(1, 6) };
            var open = i switch { 0 => 17, 1 => 5, 2 => 4, 3 => 1, 4 => 1, _ => i < 18 ? _rng.Next(0, 2) : 0 };
            var last = i < 5 ? now.Date.AddHours(11).AddMinutes(36).AddSeconds(8 - i)
                             : now.Date.AddHours(9).AddMinutes(15).AddMinutes(_rng.Next(0, 140));
            var sym = i < 5 ? symbols[0] : Pick(symbols);
            var msg = string.Format(reason.msg, _rng.Next(50_000, 10_600_000) + _rng.NextDouble(),
                                                _rng.Next(100_000, 7_000_000) + _rng.NextDouble());

            var rejections = Enumerable.Range(0, total).Select(k =>
            {
                var attempts = _rng.Next(2, 4);
                return new AlertRejection
                {
                    Id = $"{i}-{k}",
                    Time = last.AddMinutes(-k * _rng.Next(5, 40)),
                    Order = $"ENTRY SHORT {sym}",
                    StrategyId = 3100 + _rng.Next(0, 20),
                    Reason = $"Attempt {attempts}: Broker rejection ({reason.code}) {msg}",
                    Qty = sym.StartsWith("SENSEX") ? 20 : 65,
                    Attempts = attempts,
                    Acknowledged = k >= open
                };
            }).ToList();

            AlertAccounts.Add(new AlertAccount
            {
                Id = $"alert-{i}",
                Broker = broker,
                AccountId = FakeAccountNo(broker),
                Symbol = sym,
                ReasonCode = reason.code,
                ExtraSymbols = _rng.Next(1, 17),
                Message = msg,
                LastTime = last,
                Rejections = rejections
            });
        }
        AlertAccounts.Sort((a, b) => b.Open != a.Open ? b.Open.CompareTo(a.Open) : b.LastTime.CompareTo(a.LastTime));
    }

    private void SeedSubscriptions()
    {
        var now = NowIst;
        for (int i = 0; i < 10; i++)
        {
            var broker = Pick(["DELTA", "ANT", "5PAISA", "FYERS", "ZERODHA"]);
            var days = i < 2 ? 1 : i < 5 ? 2 : _rng.Next(3, 8);
            var ends = now.Date.AddDays(days).AddHours(i < 2 ? 15 + i * 2 : 0).AddMinutes(i < 2 ? 44 - i * 12 : 0);
            Subscriptions.Add(new SubscriptionItem(FakeName(), broker, FakeAccountNo(broker), Pick(Plans), ends, days));
        }
        Subscriptions.Sort((a, b) => a.EndsAt.CompareTo(b.EndsAt));
    }

    private void SeedSessions()
    {
        Sessions = new BrokerSessionHealth(35, 35, 395, new List<SessionIssue>());
    }

    // ------------------------------------------------------------------ queries

    public OverviewSummary GetOverview()
    {
        lock (_lock)
        {
            var today = NowIst.Date;
            var todays = Signals.Where(s => s.ReachedApiAt.Date == today).ToList();
            var errors = AlertAccounts.Sum(a => a.Open);
            var positions = Accounts.SelectMany(a => a.Positions).ToList();
            var running = Containers.Count(c => c.Status == "Running");
            var healthy = Containers.Count(c => c.Status == "Running" && c.Health == "Healthy");
            var sessionsValid = Accounts.Count(a => a.SessionValid);
            var subsNow = NowIst;
            var accountsTraded = todays.SelectMany(s => s.Accounts.Select(a => a.ClientId)).Distinct().Count();
            return new OverviewSummary(
                RangeFrom: DateOnly.FromDateTime(today.AddDays(-13)),
                RangeTo: DateOnly.FromDateTime(today),
                DeadLettersInRange: 705,
                FailedExecutionsInRange: 4702,
                Relay: new RelayStatus(true, 100, 83, 2.4, null),
                StrategyPerformance: new(31, 13, 18),
                UserPerformance: new(Accounts.Count * 2, Accounts.Count),
                Containers: new(running, Containers.Count, healthy, Containers.Count - healthy),
                InFlightExits: new(InFlightExits.Count, InFlightExits.Count(e => e.Stage != "SENT"), InFlightExits.Select(e => e.ClientId).Distinct().Count()),
                OmsSignals: new(todays.Count, todays.Count(s => s.Status == "Open" && s.Type == "entry" && s.Filled < s.Requested), todays.Count(s => s.Filled == 0), accountsTraded),
                PositionTracking: new(positions.Count, positions.Count(p => p.CreatedAt.Date == today), positions.Count(p => p.ClosedAt?.Date == today), Accounts.Count),
                LogsErrors: new(errors, errors, 23),
                Brokers: new(0, 0),
                Health: new(93),
                Sessions: new(sessionsValid, Accounts.Count, Accounts.Count - sessionsValid, 0),
                Subscriptions: new(
                    Subscriptions.Count(s => s.EndsAt <= subsNow.AddDays(7)),
                    Subscriptions.Count(s => s.EndsAt.Date == today),
                    Subscriptions.Count(s => s.EndsAt <= subsNow.AddDays(3)),
                    Subscriptions.Count));
        }
    }

    public static SignalListItem ToListItem(Signal s) => new(
        s.Id, s.ReachedApiAt, s.Symbol, s.Exchange, s.Segment, s.StrategyRef, s.StrategyName, s.Side, s.Type,
        s.Status, s.ExitStatus, s.Errors, s.Requested, s.Filled, s.Exited, s.RejectedOrUnfilled, s.HedgeRollup,
        s.OpenAccounts, s.ClosedAccounts, s.TotalAccounts, s.CorrelationId);
}
