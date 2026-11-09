using Jarvis.Api.Models;

namespace Jarvis.Api.Data;

/// <summary>Fake data for Positions & Exits, Performance and Infrastructure (seeded, deterministic).</summary>
public partial class MockDataStore
{
    public List<BrokerAccount> Accounts { get; } = new();
    public List<InFlightExit> InFlightExits { get; } = new();
    public List<CleanupItem> CleanupItems { get; } = new();
    public List<HedgeRecord> Hedges { get; } = new();
    public List<AutoCloseEntry> AutoCloseLog { get; } = new();
    public List<ContainerInfo> Containers { get; } = new();
    public DateTime ContractMasterLoadedAt { get; private set; }

    private static readonly string[] OpsBrokers = ["5PAISA", "ANT", "ANT", "ANGELONE", "ZERODHA", "UPSTOX", "FYERS", "SHAREINDIA", "NUBRA"];
    private static readonly string[] Expiries = ["09NOV26", "17NOV26"];
    private static readonly string[] Strategies = ["S1EN", "S2HD", "DTR", "QIP", "SWL"];

    private string Hex(int n) => string.Concat(Enumerable.Range(0, n).Select(_ => "0123456789abcdef"[_rng.Next(16)]));

    private void SeedOps()
    {
        var now = NowIst;
        var day = now.Date;
        var marketOpen = day.AddHours(9).AddMinutes(15);
        var latest = now < marketOpen ? marketOpen.AddHours(1) : (now > day.AddHours(15).AddMinutes(30) ? day.AddHours(15).AddMinutes(30) : now);

        var clientIds = new[] { "1", "1023", "1202", "1203", "1303", "1360", "15", "2", "27", "328", "8611", "8613", "8614", "8616", "8618",
                                "8619", "8620", "8621", "8623", "8625", "8627", "8631", "8632", "8634", "8640", "102", "117", "140", "163", "179",
                                "201", "226", "240", "251", "268", "301", "322", "345", "377", "402", "419" };

        foreach (var (cid, idx) in clientIds.Select((c, i) => (c, i)))
        {
            var broker = OpsBrokers[idx % OpsBrokers.Length];
            var acct = new BrokerAccount
            {
                ClientId = cid,
                Name = FakeName(),
                Broker = broker,
                AccountId = FakeAccountNo(broker),
                Basket = Pick(Baskets),
                TokenStale = idx % 13 == 5,
                Strategies = _rng.Next(4, 14),
                ExcludedStrategies = idx % 9 == 1 ? 3 : 0,
            };
            acct.SessionValid = !acct.TokenStale;

            int count = idx % 5 == 0 ? _rng.Next(30, 46) : _rng.Next(3, 30);
            for (int p = 0; p < count; p++)
            {
                var expiry = Expiries[_rng.Next(Expiries.Length)];
                var isCall = _rng.NextDouble() < 0.5;
                var strike = 24400 + _rng.Next(0, 60) * 50;
                var symbol = $"NIFTY{expiry}{(isCall ? 'C' : 'P')}{strike}";
                var display = $"NIFTY {expiry[..2]} {char.ToUpper(expiry[2])}{expiry[3..5].ToLower()} 20{expiry[5..]} {(isCall ? "CE" : "PE")} {strike}.00";
                var lots = _rng.Next(1, 7);
                var qty = lots * 65;
                var shortSide = _rng.NextDouble() < 0.6;
                var entry = Math.Round((decimal)(_rng.NextDouble() * 180 + 2), 2);
                var ltp = Math.Max(0.05m, Math.Round(entry * (decimal)(0.6 + _rng.NextDouble() * 0.8), 2));
                var created = marketOpen.AddSeconds(_rng.Next(0, Math.Max(60, (int)(latest - marketOpen).TotalSeconds)));
                var closed = _rng.NextDouble() < 0.3;
                var exitPx = Math.Max(0.05m, Math.Round(entry * (decimal)(0.6 + _rng.NextDouble() * 0.8), 2));
                var strategy = Pick(Strategies);
                var corr = $"{strategy}_{(isCall ? "CE" : "PE")}_{day:yyyyMMdd}_{Hex(6)}";

                var pos = new PositionRecord
                {
                    Id = $"{cid}-{p}",
                    Side = closed ? "FLAT" : shortSide ? "SELL" : "BUY",
                    EntrySide = shortSide ? "SELL" : "BUY",
                    Symbol = symbol,
                    DisplaySymbol = display,
                    StrategyId = 3080 + _rng.Next(0, 40),
                    Strategy = strategy,
                    Product = _rng.NextDouble() < 0.7 ? "I" : "D",
                    BuyQty = shortSide ? (closed ? qty : 0) : qty,
                    SellQty = shortSide ? qty : (closed ? qty : 0),
                    AvgBuy = shortSide ? (closed ? exitPx : 0) : entry,
                    AvgSell = shortSide ? entry : (closed ? exitPx : 0),
                    Ltp = ltp,
                    Status = "OPEN",
                    Stale = !closed && _rng.NextDouble() < 0.25,
                    CreatedAt = created,
                    EntrySignalId = Hex(24),
                    Correlation = corr,
                    Token = $"{_rng.Next(35000, 65000)}",
                    OrderTag = $"JV{_rng.Next(100000, 999999)}",
                };
                if (closed)
                {
                    pos.Realized = Math.Round((pos.AvgSell - pos.AvgBuy) * qty, 2);
                    pos.ClosedAt = created.AddMinutes(_rng.Next(3, 180));
                    if (pos.ClosedAt > latest) pos.ClosedAt = latest;
                    pos.LatestExitId = $"FORCE-EXIT-{Hex(10)}";
                    pos.Status = "CLOSED";
                }
                else
                {
                    pos.Unrealized = Math.Round((shortSide ? entry - ltp : ltp - entry) * qty, 2);
                    pos.PnlPct = entry == 0 ? null : Math.Round((shortSide ? entry - ltp : ltp - entry) / entry * 100, 2);
                    if (pos.PnlPct < -20) pos.Status = "RISK";
                }
                acct.Positions.Add(pos);

                // orders & trades derived from the position
                acct.Orders.Add(new OrderRecord($"{2611090000 + _rng.Next(100000, 999999)}", created, symbol, shortSide ? "SELL" : "BUY", "ENTRY",
                    qty, qty, entry, "COMPLETED", corr, null));
                if (closed)
                    acct.Orders.Add(new OrderRecord($"{2611090000 + _rng.Next(100000, 999999)}", pos.ClosedAt!.Value, symbol, shortSide ? "BUY" : "SELL", "EXIT",
                        qty, qty, exitPx, "COMPLETED", pos.LatestExitId!, null));
                if (_rng.NextDouble() < 0.08)
                    acct.Orders.Add(new OrderRecord($"{2611090000 + _rng.Next(100000, 999999)}", created.AddSeconds(2), symbol, shortSide ? "SELL" : "BUY", "ENTRY",
                        qty, 0, entry, "REJECTED", corr, Pick(["Margin Shortfall", "RMS: Margin Exceeds", "Price out of LPP range"])));

                var dur = (pos.ClosedAt ?? latest) - created;
                acct.Trades.Add(new TradeRecord(
                    Hex(32).ToUpper(), symbol, strategy, shortSide ? "SELL" : "BUY", shortSide ? "BUY" : "SELL",
                    qty, closed ? qty : 0, closed ? 0 : qty, $"{(int)dur.TotalHours}h {dur.Minutes}m", broker, acct.AccountId,
                    created, pos.ClosedAt, closed ? pos.Realized : null, closed ? "CLOSED" : "OPEN", corr));
            }
            acct.Orders.Sort((a, b) => b.Time.CompareTo(a.Time));
            acct.Trades.Sort((a, b) => b.EntryTime.CompareTo(a.EntryTime));
            acct.Positions.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
            Accounts.Add(acct);
        }

        // ---- in-flight exits / cleanup / hedges / auto-close
        var openPositions = Accounts.SelectMany(a => a.Positions.Where(p => p.NetQty != 0).Select(p => (a, p))).ToList();
        foreach (var (a, p) in openPositions.OrderBy(_ => _rng.Next()).Take(42))
            InFlightExits.Add(new InFlightExit(Hex(8), a.ClientId, a.Broker, a.AccountId, p.Symbol, Math.Abs(p.NetQty),
                Pick(["QUEUED", "SENT", "PARTIAL", "RETRYING"]), _rng.Next(1, 4), latest.AddMinutes(-_rng.Next(1, 40)),
                Pick(["Strategy exit signal", "SL hit", "Target hit", "Manual force exit", "MIS square-off"]), p.Correlation));

        foreach (var (a, p) in openPositions.OrderBy(_ => _rng.Next()).Take(18))
        {
            var issue = Pick(["Qty mismatch (OMS vs broker)", "Orphan position at broker", "Stale > 30 min, no LTP", "Hedge leg open after main closed"]);
            var brokerQty = issue.StartsWith("Qty") ? Math.Abs(p.NetQty) - 65 : issue.StartsWith("Orphan") ? Math.Abs(p.NetQty) : Math.Abs(p.NetQty);
            CleanupItems.Add(new CleanupItem(a.ClientId, a.Broker, a.AccountId, p.Symbol, issue,
                issue.StartsWith("Orphan") ? 0 : Math.Abs(p.NetQty), Math.Max(0, brokerQty), latest.AddMinutes(-_rng.Next(5, 120))));
        }

        foreach (var (a, p) in openPositions.Where(x => x.p.Side == "SELL").OrderBy(_ => _rng.Next()).Take(24))
        {
            var q = Math.Abs(p.NetQty);
            var status = Pick(["PROTECTED", "PROTECTED", "PROTECTED", "PARTIAL", "NAKED"]);
            var underlying = p.Symbol[..^6];                 // NIFTY09NOV26
            var strike = int.Parse(p.Symbol[^5..]);          // 24950
            Hedges.Add(new HedgeRecord(a.ClientId, a.Broker, a.AccountId, p.Symbol, q,
                $"{underlying}C{strike + 1000}", status == "NAKED" ? 0 : q,
                $"{underlying}P{strike - 1000}", status == "PROTECTED" ? q : 0, status));
        }

        foreach (var (a, p) in Accounts.SelectMany(a => a.Positions.Where(p => p.NetQty == 0).Select(p => (a, p))).OrderBy(_ => _rng.Next()).Take(30))
        {
            var ok = _rng.NextDouble() < 0.85;
            AutoCloseLog.Add(new AutoCloseEntry(p.ClosedAt ?? latest, a.ClientId, a.Broker, a.AccountId, p.Symbol, p.BuyQty,
                Pick(["MIS auto square-off 15:15", "Max loss per strategy", "Target reached", "Expiry-day close"]),
                ok ? "SUCCESS" : "FAILED", ok ? "Exit order completed" : "Broker rejected: RMS"));
        }
        AutoCloseLog.Sort((x, y) => y.Time.CompareTo(x.Time));

        // ---- containers (one per account-bearing client)
        foreach (var (a, i) in Accounts.Take(34).Select((a, i) => (a, i)))
        {
            var ip = $"20.{_rng.Next(190, 230)}.{_rng.Next(10, 250)}.{_rng.Next(2, 250)}";
            var restarts = i % 7 == 1 ? 4 : 1;
            Containers.Add(new ContainerInfo
            {
                Name = $"oms-client-{(int.TryParse(a.ClientId, out var n) && n > 8000 ? n : 8600 + i)}",
                PrivateIp = $"10.2.0.{i + 5}",
                StaticPublicIp = ip,
                ClientId = a.ClientId,
                Broker = a.Broker,
                Accounts = a.AccountId,
                CpuPct = Math.Round(i % 9 == 1 ? 40 + _rng.NextDouble() * 10 : 1.5 + _rng.NextDouble() * 4, 1),
                MemPct = Math.Round(1.1 + _rng.NextDouble() * 0.6, 1),
                MemMb = _rng.Next(360, 520),
                RedisLatencyMs = Math.Round(0.22 + _rng.NextDouble() * 0.12, 4),
                MongoLatencyMs = Math.Round(0.75 + _rng.NextDouble() * 0.4, 4),
                StreamMsgs = _rng.Next(300, 5500),
                Restarts = restarts,
                RestartsToday = restarts,
                Heartbeat = true,
            });
        }
        ContractMasterLoadedAt = latest.AddMinutes(-_rng.Next(60, 110));
    }

    // ------------------------------------------------------------------ queries

    public static AccountListItem ToListItem(BrokerAccount a) => new(
        a.ClientId, a.Name, a.Broker, a.AccountId, a.Status, a.SessionValid, a.TokenStale, a.OpenPositions, a.ClosedToday,
        a.Strategies, a.ExcludedStrategies, a.StrategyPnl, a.Realized, a.Unrealized);

    public TodayActivity GetTodayActivity()
    {
        lock (_lock)
        {
            var today = NowIst.Date;
            var todays = Signals.Where(s => s.ReachedApiAt.Date == today).ToList();
            var hours = Enumerable.Range(9, 7).Select(h => new HourBucket(h, todays.Count(s => s.ReachedApiAt.Hour == h))).ToList();
            var accts = todays.SelectMany(s => s.Accounts).ToList();
            return new TodayActivity(hours, new ActivityFunnel(
                todays.Count, accts.Count, accts.Count(a => a.Fill > 0), accts.Count(a => a.OrderStatus == "REJECTED"),
                accts.Count(a => a.Pending > 0) + todays.Count(s => s.Status == "Open" && s.Type == "entry") / 4));
        }
    }

    public ContainerSummary GetContainers()
    {
        lock (_lock)
        {
            // small jitter so the dashboard "feels" live on refresh
            foreach (var c in Containers.Where(c => c.Status == "Running"))
            {
                c.CpuPct = Math.Round(Math.Clamp(c.CpuPct + (_rng.NextDouble() - 0.5) * 1.2, 0.5, 95), 1);
                c.StreamMsgs += _rng.Next(0, 25);
                c.RedisLatencyMs = Math.Round(0.22 + _rng.NextDouble() * 0.12, 4);
                c.MongoLatencyMs = Math.Round(0.75 + _rng.NextDouble() * 0.4, 4);
            }
            var running = Containers.Where(c => c.Status == "Running").ToList();
            return new ContainerSummary(
                Containers.Count, running.Count, Containers.Count - running.Count,
                Containers.Count(c => c.Health == "Healthy" && c.Status == "Running"),
                Containers.Count(c => c.Health == "Unhealthy" || c.Status != "Running"),
                Containers.Count(c => !c.Heartbeat),
                running.Count(c => c.MongoOk), running.Count(c => c.RedisOk), running.Count(c => c.RoutingValid),
                running.Count == 0 ? 0 : Math.Round(running.Average(c => c.CpuPct), 3),
                running.Count == 0 ? 0 : Math.Round(running.Average(c => c.MemPct), 2),
                88163, ContractMasterLoadedAt, NowIst, Containers.ToList());
        }
    }
}
