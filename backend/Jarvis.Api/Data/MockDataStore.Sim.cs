using Jarvis.Api.Models;

namespace Jarvis.Api.Data;

public enum SimScenario { Calm, Normal, Volatile, Rally, Crash }

public record SimEvent(DateTime Time, string Kind, string Text, string? ClientId = null, string? Symbol = null, decimal? Amount = null);

/// <summary>What one tick produced, handed to the real-time bus (Redis → SQL) outside the store lock.</summary>
public record SimAccountPnl(string ClientId, string Name, string Broker, decimal Realized, decimal Unrealized, decimal Total, int OpenPositions);
public record SimPublishBatch(double Spot, double ChangePct, string Scenario, List<SimAccountPnl>? Pnl, List<SimEvent> Events);

public record SimStatus(
    bool Running, string Scenario, int TickMs, long Ticks, DateTime? LastTick,
    double Spot, double SpotOpen, double SpotChangePct, decimal TotalPnl, int OpenPositions, List<SimEvent> Events);

/// <summary>
/// Demo market simulator. Every tick it random-walks a NIFTY spot, re-prices every option from it, and so moves
/// unrealized P&amp;L for every account. It also produces a light stream of lifecycle events (new entries, exits,
/// in-flight exit stages, signals, rejections, container load) so the console behaves like a live system.
/// Uses its own unseeded Random so the deterministic seed data is unchanged.
/// </summary>
public partial class MockDataStore
{
    private readonly Random _sim = new();
    private readonly Dictionary<string, decimal> _optPx = new();
    private readonly LinkedList<SimEvent> _events = new();
    private double _spot = 24850, _spotOpen = 24850;
    private long _ticks;
    private DateTime? _lastTick;

    public SimScenario Scenario { get; private set; } = SimScenario.Normal;
    public bool SimPaused { get; set; }

    // live-ish overview numbers (were constants)
    private double _execPct = 83, _dlqPct = 2.4;
    private int _successRate = 93;

    private (double vol, double drift, double rate) Params => Scenario switch
    {
        SimScenario.Calm => (0.00012, 0, 0.4),
        SimScenario.Volatile => (0.0011, 0, 1.8),
        SimScenario.Rally => (0.0004, 0.00030, 1.2),
        SimScenario.Crash => (0.0007, -0.00035, 1.6),
        _ => (0.00035, 0, 1.0),
    };

    public void SetScenario(SimScenario s)
    {
        lock (_lock) { Scenario = s; Log("scenario", $"Scenario switched to {s}"); }
    }

    public SimStatus GetSimStatus(int tickMs)
    {
        lock (_lock)
        {
            var open = Accounts.SelectMany(a => a.Positions).Count(p => p.NetQty != 0);
            return new SimStatus(!SimPaused, Scenario.ToString(), tickMs, _ticks, _lastTick,
                Math.Round(_spot, 2), _spotOpen, Math.Round((_spot - _spotOpen) / _spotOpen * 100, 2),
                Math.Round(Accounts.Sum(a => a.StrategyPnl), 2), open, _events.ToList());
        }
    }

    // ------------------------------------------------------------------ tick

    public void SimTick()
    {
        lock (_lock)
        {
            var (vol, drift, rate) = Params;
            var now = NowIst;
            _ticks++; _lastTick = now;

            // 1. spot: drift + noise, mean-reverting to the open in neutral scenarios, bounded to ±5%
            var reversion = drift == 0 ? (_spotOpen - _spot) / _spotOpen * 0.02 : 0;
            var prev = _spot;
            _spot *= 1 + drift + reversion + vol * Gauss();
            _spot = Math.Clamp(_spot, _spotOpen * 0.95, _spotOpen * 1.05);
            var dSpot = _spot - prev;

            // 2. re-price every option symbol once (shared across accounts), then mark positions
            foreach (var sym in Accounts.SelectMany(a => a.Positions).Where(p => p.NetQty != 0).Select(p => p.Symbol).Distinct().ToList())
                _optPx[sym] = NextPrice(sym, dSpot, vol);

            var autoClose = new List<(BrokerAccount a, PositionRecord p)>();
            foreach (var a in Accounts)
                foreach (var p in a.Positions.Where(p => p.NetQty != 0))
                {
                    MarkToMarket(p);
                    if (p.PnlPct < -60) autoClose.Add((a, p));
                }
            foreach (var (a, p) in autoClose.Take(1))   // at most one per tick so it reads as a stream
            {
                ClosePosition(a, p, "AUTO-CLOSE");
                AutoCloseLog.Insert(0, new AutoCloseEntry(now, a.ClientId, a.Broker, a.AccountId, p.Symbol, p.BuyQty,
                    "Max loss per strategy", "SUCCESS", "Exit order completed"));
                Log("auto-close", $"Max-loss auto close · {a.ClientId} {p.Symbol} ({p.Realized:N0})", a.ClientId, p.Symbol, p.Realized);
            }

            // 3. lifecycle events (rate scales with scenario)
            if (Chance(0.10 * rate)) OpenRandomPosition(now);
            if (Chance(0.09 * rate)) CloseRandomPosition();
            StepInFlight(now, rate);
            if (Chance(0.035 * rate)) NewSignal(now);
            if (Chance(0.03 * rate)) ExitOldestOpenSignal(now);
            if (Chance(0.015 * rate)) NewRejection(now);
            StepContainers(rate);

            _execPct = Math.Clamp(_execPct + Gauss() * 0.6, 76, 92);
            _dlqPct = Math.Clamp(_dlqPct + Gauss() * 0.08 * rate, 0.8, 6);
            if (Chance(0.05)) _successRate = Math.Clamp(_successRate + (_sim.NextDouble() < 0.5 ? -1 : 1), 88, 97);
        }
    }

    // ------------------------------------------------------------------ pricing

    /// <summary>NIFTY09NOV26C24600 → (isCall, strike). Returns null for symbols it can't parse.</summary>
    private static (bool call, int strike)? ParseOption(string sym)
    {
        if (sym.Length < 7 || !int.TryParse(sym[^5..], out var k)) return null;
        var t = sym[^6];
        return t is 'C' or 'P' ? (t == 'C', k) : null;
    }

    private decimal NextPrice(string sym, double dSpot, double vol)
    {
        var last = _optPx.TryGetValue(sym, out var v) ? (double)v
                 : (double)(Accounts.SelectMany(a => a.Positions).FirstOrDefault(p => p.Symbol == sym)?.Ltp ?? 50m);
        var o = ParseOption(sym);
        double delta = 0;
        if (o is { } opt)
        {
            var x = (_spot - opt.strike) / 220.0;
            var callDelta = 1 / (1 + Math.Exp(-1.7 * x));
            delta = opt.call ? callDelta : callDelta - 1;
        }
        var px = last + delta * dSpot                    // move with the underlying
               + last * vol * 3 * Gauss()                // option-specific noise (IV wiggle)
               - last * 0.00004;                         // a little theta every tick
        return (decimal)Math.Round(Math.Max(0.05, px), 2);
    }

    private double TheoreticalPrice(bool call, int strike)
    {
        var intrinsic = Math.Max(0, call ? _spot - strike : strike - _spot);
        return Math.Round(intrinsic + 140 * Math.Exp(-Math.Abs(_spot - strike) / 380.0) + 2, 2);
    }

    private void MarkToMarket(PositionRecord p)
    {
        if (!_optPx.TryGetValue(p.Symbol, out var ltp)) return;
        p.Ltp = ltp;
        var qty = Math.Abs(p.NetQty);
        var entry = p.NetQty > 0 ? p.AvgBuy : p.AvgSell;
        var perUnit = p.NetQty > 0 ? ltp - entry : entry - ltp;
        p.Unrealized = Math.Round(perUnit * qty, 2);
        p.PnlPct = entry == 0 ? null : Math.Round(perUnit / entry * 100, 2);
        p.Status = p.PnlPct < -20 ? "RISK" : "OPEN";
    }

    // ------------------------------------------------------------------ positions

    /// <summary>Square off a position at its LTP; records the exit order and closes the matching trade.</summary>
    public void ClosePosition(BrokerAccount a, PositionRecord p, string exitPrefix = "FORCE-EXIT")
    {
        if (p.NetQty == 0) return;
        var qty = Math.Abs(p.NetQty);
        var now = NowIst;
        if (p.NetQty > 0) { p.Realized += Math.Round((p.Ltp - p.AvgBuy) * qty, 2); p.AvgSell = p.Ltp; p.SellQty = p.BuyQty; }
        else { p.Realized += Math.Round((p.AvgSell - p.Ltp) * qty, 2); p.AvgBuy = p.Ltp; p.BuyQty = p.SellQty; }
        p.Unrealized = 0; p.PnlPct = null; p.Side = "FLAT"; p.Status = "CLOSED";
        p.ClosedAt = now; p.LatestExitId = $"{exitPrefix}-{Hex(10)}";

        a.Orders.Insert(0, new OrderRecord($"{2611090000 + _sim.Next(100000, 999999)}", now, p.Symbol,
            p.EntrySide == "SELL" ? "BUY" : "SELL", "EXIT", qty, qty, p.Ltp, "COMPLETED", p.LatestExitId, null));

        var i = a.Trades.FindIndex(t => t.Correlation == p.Correlation && t.Symbol == p.Symbol && t.Status == "OPEN");
        if (i >= 0)
        {
            var dur = now - a.Trades[i].EntryTime;
            a.Trades[i] = a.Trades[i] with
            {
                ExitQty = qty, NetQty = 0, ExitTime = now, Pnl = p.Realized, Status = "CLOSED",
                Duration = $"{(int)dur.TotalHours}h {dur.Minutes}m",
            };
        }
    }

    private void CloseRandomPosition()
    {
        var open = Accounts.SelectMany(a => a.Positions.Where(p => p.NetQty != 0).Select(p => (a, p))).ToList();
        if (open.Count < 120) return;                       // keep the book from draining
        var (acct, pos) = open[_sim.Next(open.Count)];
        ClosePosition(acct, pos, Chance(0.5) ? "TARGET-EXIT" : "SIGNAL-EXIT");
        Log("exit", $"Exit filled · {acct.ClientId} {pos.Symbol} · P&L {pos.Realized:N0}", acct.ClientId, pos.Symbol, pos.Realized);
    }

    private void OpenRandomPosition(DateTime now)
    {
        var live = Accounts.Where(a => a.SessionValid && a.Positions.Count < 60).ToList();
        if (live.Count == 0) return;
        var a = live[_sim.Next(live.Count)];
        var call = Chance(0.5);
        var atm = (int)Math.Round(_spot / 50) * 50;
        var strike = atm + (call ? 1 : -1) * _sim.Next(0, 8) * 50;
        var expiry = Expiries[_sim.Next(Expiries.Length)];
        var symbol = $"NIFTY{expiry}{(call ? 'C' : 'P')}{strike}";
        var px = _optPx.TryGetValue(symbol, out var known) ? known : (decimal)TheoreticalPrice(call, strike);
        _optPx[symbol] = px;
        var sell = Chance(0.6);
        var qty = _sim.Next(1, 6) * 65;
        var strategy = Strategies[_sim.Next(Strategies.Length)];
        var corr = $"{strategy}_{(call ? "CE" : "PE")}_{now:yyyyMMdd}_{Hex(6)}";

        if (Chance(0.06))
        {
            a.Orders.Insert(0, new OrderRecord($"{2611090000 + _sim.Next(100000, 999999)}", now, symbol, sell ? "SELL" : "BUY", "ENTRY",
                qty, 0, px, "REJECTED", corr, "RMS: Margin Exceeds"));
            Log("reject", $"Entry rejected · {a.ClientId} {symbol} · Margin", a.ClientId, symbol, qty);
            return;
        }

        var pos = new PositionRecord
        {
            Id = $"{a.ClientId}-s{_ticks}",
            Side = sell ? "SELL" : "BUY",
            EntrySide = sell ? "SELL" : "BUY",
            Symbol = symbol,
            DisplaySymbol = $"NIFTY {expiry[..2]} {char.ToUpper(expiry[2])}{expiry[3..5].ToLower()} 20{expiry[5..]} {(call ? "CE" : "PE")} {strike}.00",
            StrategyId = 3080 + _sim.Next(0, 40),
            Strategy = strategy,
            Product = Chance(0.7) ? "I" : "D",
            BuyQty = sell ? 0 : qty,
            SellQty = sell ? qty : 0,
            AvgBuy = sell ? 0 : px,
            AvgSell = sell ? px : 0,
            Ltp = px,
            Status = "OPEN",
            CreatedAt = now,
            EntrySignalId = Hex(24),
            Correlation = corr,
            Token = $"{_sim.Next(35000, 65000)}",
            OrderTag = $"JV{_sim.Next(100000, 999999)}",
        };
        a.Positions.Insert(0, pos);
        a.Orders.Insert(0, new OrderRecord($"{2611090000 + _sim.Next(100000, 999999)}", now, symbol, pos.EntrySide, "ENTRY",
            qty, qty, px, "COMPLETED", corr, null));
        a.Trades.Insert(0, new TradeRecord(Hex(32).ToUpper(), symbol, strategy, pos.EntrySide, sell ? "BUY" : "SELL",
            qty, 0, qty, "0h 0m", a.Broker, a.AccountId, now, null, null, "OPEN", corr));
        Log("entry", $"New {pos.EntrySide} · {a.ClientId} {symbol} × {qty} @ {px:N2}", a.ClientId, symbol, px);
    }

    // ------------------------------------------------------------------ in-flight exits

    private void StepInFlight(DateTime now, double rate)
    {
        for (int i = InFlightExits.Count - 1; i >= 0; i--)
        {
            var e = InFlightExits[i];
            if (!Chance(0.03 * rate)) continue;
            string? next = e.Stage switch
            {
                "QUEUED" => "SENT",
                "RETRYING" => "SENT",
                "SENT" => Chance(0.25) ? "PARTIAL" : Chance(0.1) ? "RETRYING" : null,   // null = done
                "PARTIAL" => null,
                _ => null,
            };
            if (next is null)
            {
                InFlightExits.RemoveAt(i);
                var a = Accounts.FirstOrDefault(x => x.ClientId == e.ClientId);
                var p = a?.Positions.FirstOrDefault(x => x.Symbol == e.Symbol && x.NetQty != 0);
                if (a is not null && p is not null) ClosePosition(a, p, "EXIT");
                Log("exit", $"In-flight exit done · {e.ClientId} {e.Symbol}", e.ClientId, e.Symbol, p?.Realized);
            }
            else InFlightExits[i] = e with { Stage = next, Attempts = next == "RETRYING" ? e.Attempts + 1 : e.Attempts };
        }

        if (InFlightExits.Count < 45 && Chance(0.3 * rate))
        {
            var open = Accounts.SelectMany(a => a.Positions.Where(p => p.NetQty != 0).Select(p => (a, p)))
                               .Where(x => !InFlightExits.Any(e => e.ClientId == x.a.ClientId && e.Symbol == x.p.Symbol)).ToList();
            if (open.Count == 0) return;
            var (a, p) = open[_sim.Next(open.Count)];
            var reason = p.Status == "RISK" ? "SL hit" : new[] { "Strategy exit signal", "Target hit", "MIS square-off" }[_sim.Next(3)];
            InFlightExits.Add(new InFlightExit(Hex(8), a.ClientId, a.Broker, a.AccountId, p.Symbol, Math.Abs(p.NetQty),
                "QUEUED", 1, now, reason, p.Correlation));
            Log("inflight", $"Exit queued · {a.ClientId} {p.Symbol} · {reason}", a.ClientId, p.Symbol, Math.Abs(p.NetQty));
        }
    }

    // ------------------------------------------------------------------ signals / alerts

    /// <summary>A new entry signal fanned out to a basket, cloned from a real template so it looks like the rest.</summary>
    private void NewSignal(DateTime now)
    {
        var template = Signals[_sim.Next(Signals.Count)];
        var accounts = template.Accounts.Select(t =>
        {
            var rejected = Chance(0.04);
            return new SignalAccountExecution
            {
                ClientId = t.ClientId, Broker = t.Broker, BrokerAccount = t.BrokerAccount,
                BrokerOrderId = t.Broker == "ANGELONE" ? Guid.NewGuid().ToString() : $"2611050001{_sim.Next(80000, 99999)}",
                OrderStatus = rejected ? "REJECTED" : "COMPLETED",
                Req = t.Req, Fill = rejected ? 0 : t.Req, Rejected = rejected ? t.Req : 0,
                AvgEntry = t.AvgEntry + (decimal)Math.Round(_sim.NextDouble() * 4 - 2, 2),
                RejectReason = rejected ? "Margin Shortfall: insufficient funds" : null,
            };
        }).ToList();

        var s = new Signal
        {
            Id = Signals.Max(x => x.Id) + 1,
            ReachedApiAt = now,
            Symbol = template.Symbol, Exchange = template.Exchange, Segment = template.Segment,
            StrategyRef = template.StrategyRef, StrategyName = template.StrategyName, Basket = template.Basket,
            Side = template.Side, Type = "entry", Status = "Open", ExitStatus = "EXIT PENDING",
            Accounts = accounts,
        };
        Signals.Add(s);
        Log("signal", $"Signal #{s.Id} {s.Symbol} → {accounts.Count} accounts", null, s.Symbol, s.Id);
    }

    /// <summary>Exit an older open entry signal; occasionally leaves a stuck chunk so the residual flow can be demoed.</summary>
    private void ExitOldestOpenSignal(DateTime now)
    {
        var s = Signals.Where(x => x.Type == "entry" && x.ReachedApiAt.Date == now.Date && !x.ExitSent && x.Filled > 0
                                   && (now - x.ReachedApiAt).TotalSeconds > 60)
                       .OrderBy(x => x.ReachedApiAt).FirstOrDefault();
        if (s is null) return;
        foreach (var a in s.Accounts.Where(a => a.Fill > 0))
        {
            a.Exit = a.Fill;
            a.AvgExit = a.AvgEntry - (decimal)Math.Round(_sim.NextDouble() * 20 - 8, 2);
        }
        s.ExitCorrelationId = Guid.NewGuid().ToString("N");
        s.Status = "Closed"; s.ExitStatus = "EXIT COMPLETED";

        var stuck = Chance(0.12) ? s.Accounts.FirstOrDefault(a => a.Exit >= 40 && a.Hedge is null) : null;
        if (stuck is not null)
        {
            s.Chunked = true;
            var chunks = SplitChunks(stuck.Exit, OrderId());
            var last = chunks[^1];
            last.Fill = 0; last.BrokerOrder = null; last.Modify = null;
            last.Reason = "Order closed at broker without fills (IOC expired / RMS cancel)";
            stuck.ChunkOrders = chunks;
            stuck.Exit -= last.Req;
            stuck.OrderStatus = "PARTIAL";
            stuck.PartialReason = $"Exit chunk {chunks.Count}/{chunks.Count} not filled — {last.Req} qty still open at broker";
            Log("residual", $"Signal #{s.Id} exit left {last.Req} qty open for {stuck.ClientId}", stuck.ClientId, s.Symbol, last.Req);
        }
        else Log("signal", $"Signal #{s.Id} exited", null, s.Symbol, s.Id);
    }

    private void NewRejection(DateTime now)
    {
        if (AlertAccounts.Count == 0) return;
        var acct = AlertAccounts[_sim.Next(Math.Min(10, AlertAccounts.Count))];
        var attempts = _sim.Next(2, 4);
        acct.Rejections.Insert(0, new AlertRejection
        {
            Id = $"sim-{_ticks}",
            Time = now,
            Order = $"ENTRY SHORT {acct.Symbol}",
            StrategyId = 3100 + _sim.Next(0, 20),
            Reason = $"Attempt {attempts}: Broker rejection ({acct.ReasonCode}) {acct.Message}",
            Qty = acct.Symbol.StartsWith("SENSEX") ? 20 : 65,
            Attempts = attempts,
        });
        acct.LastTime = now;
        Log("reject", $"Broker rejection · {acct.Broker} {acct.AccountId}", null, acct.Symbol, null);
    }

    // ------------------------------------------------------------------ containers

    private void StepContainers(double rate)
    {
        foreach (var c in Containers.Where(c => c.Status == "Running"))
        {
            c.CpuPct = Math.Round(Math.Clamp(c.CpuPct + Gauss() * 0.8 * rate, 0.5, 95), 1);
            c.MemPct = Math.Round(Math.Clamp(c.MemPct + Gauss() * 0.03, 0.8, 4), 1);
            c.MemMb = Math.Clamp(c.MemMb + _sim.Next(-4, 5), 300, 700);
            c.StreamMsgs += _sim.Next(0, (int)(12 * rate) + 2);
            c.RedisLatencyMs = Math.Round(0.22 + _sim.NextDouble() * 0.12 * rate, 4);
            c.MongoLatencyMs = Math.Round(0.75 + _sim.NextDouble() * 0.4 * rate, 4);
            if (c.Health == "Healthy" && Chance(0.002 * rate))
            {
                c.Health = "Degraded"; c.CpuPct = Math.Round(55 + _sim.NextDouble() * 30, 1);
                Log("infra", $"{c.Name} degraded (CPU {c.CpuPct}%)");
            }
            else if (c.Health == "Degraded" && Chance(0.08))
            {
                c.Health = "Healthy"; c.CpuPct = Math.Round(2 + _sim.NextDouble() * 3, 1);
                Log("infra", $"{c.Name} recovered");
            }
        }
    }

    // ------------------------------------------------------------------ helpers

    private bool Chance(double p) => _sim.NextDouble() < p;

    private double Gauss()
    {
        var u1 = 1.0 - _sim.NextDouble();
        var u2 = _sim.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    private readonly List<SimEvent> _unpublished = new();

    private void Log(string kind, string text, string? clientId = null, string? symbol = null, decimal? amount = null)
    {
        var e = new SimEvent(NowIst, kind, text, clientId, symbol, amount);
        _events.AddFirst(e);
        while (_events.Count > 40) _events.RemoveLast();
        _unpublished.Add(e);
        if (_unpublished.Count > 5000) _unpublished.RemoveRange(0, 1000);   // nobody draining (bus off) → cap memory
    }

    /// <summary>Takes what changed since the last call (events always; per-account P&amp;L when asked).</summary>
    public SimPublishBatch DrainForPublish(bool includePnl)
    {
        lock (_lock)
        {
            var events = _unpublished.ToList();
            _unpublished.Clear();
            var pnl = includePnl
                ? Accounts.Select(a => new SimAccountPnl(a.ClientId, a.Name, a.Broker, Math.Round(a.Realized, 2),
                    Math.Round(a.Unrealized, 2), Math.Round(a.StrategyPnl, 2), a.OpenPositions)).ToList()
                : null;
            return new SimPublishBatch(Math.Round(_spot, 2), Math.Round((_spot - _spotOpen) / _spotOpen * 100, 3), Scenario.ToString(), pnl, events);
        }
    }
}
