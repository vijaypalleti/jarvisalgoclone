using System.Text.Json;
using System.Text.Json.Nodes;
using Jarvis.Api.Data;
using Jarvis.Api.Models;

namespace Jarvis.Api.Services.Assistant;

/// <summary>
/// The tools the chat model may call. Every tool is READ-ONLY and only reads data the app already has
/// (Delta feed in memory, OMS demo store, the real Fyers account). Add a tool = add a definition + a case in RunAsync.
/// </summary>
public class AssistantTools(MockDataStore store, DeltaMarketFeed delta, FyersClient fyers, RealtimeDb rtDb, MarketDb marketDb,
    RealtimeBus bus, RealtimePersister rtPersister, MarketPersister marketPersister, FyersPoller fyersPoller, IWebHostEnvironment env,
    FyersSymbols symbols)
{
    // ------------------------------------------------------------------ definitions (OpenAI/Groq "tools" format)

    public static JsonArray AllDefinitions() =>
    [
        Tool("get_btc_price", "Live BTCUSD perpetual from Delta Exchange: last price, mark, 24h change/high/low, funding, open interest. Prices in USD.", new { }),
        Tool("get_fyers_account",
            "The REAL Fyers broker account of Vijay (the console owner). Use for 'Vijay', 'my account', 'Fyers', 'my P&L/funds/positions/holdings/orders'. Returns profile, funds, P&L, open positions, holdings, today's orders by status.",
            new { }),
        Tool("find_broker_account",
            "Look up demo OMS broker accounts by OMS client id (e.g. '1023'), SYSTEM USER id from User Management (e.g. '8606' / '#8606' / 'user 8606'), broker account number, person name or broker. Returns status (Active, session valid / token stale), broker, basket, open & closed positions, realized/unrealized/total P&L (INR), container health and open issues for that account.",
            new { query = Str("client id, broker account number, name or broker, e.g. '1023'") }, "query"),
        Tool("get_positions",
            "Positions in the demo OMS, optionally for one account and/or strategy. Returns counts (open / closed / at-risk) and a list with symbol, side, qty, avg, LTP, P&L.",
            new
            {
                account = Str("client id / account / name to limit to one account (omit for all accounts)"),
                status = new { type = "string", @enum = new[] { "open", "closed", "risk", "all" }, description = "default open" },
                strategy = Str("strategy name, e.g. 'S1EN' (optional)"),
                limit = new { type = "integer", description = "max rows to list, default 10" },
            }),
        Tool("get_open_issues",
            "Everything that currently needs action: signals needing attention (exit partial / rejected / contradictions), open broker rejections, positions needing cleanup, in-flight exits, unhealthy or stopped containers, dead letters. Answer with the total AND the breakdown from 'summary'.",
            new { }),
        Tool("get_portfolio_summary",
            "Whole demo OMS at a glance: accounts, open positions, total realized/unrealized P&L (INR), top gainers and losers, simulated NIFTY spot and market scenario.",
            new { }),
        Tool("get_signal",
            "Look up a trading signal by its id (e.g. 45402) or the latest one for a symbol (e.g. 'NIFTY17NOV26C25300'). Returns status, exit status, fills, residual qty, issues, strategy, time.",
            new { id = new { type = "integer", description = "signal id" }, symbol = Str("symbol, partial ok") }),
        Tool("get_pnl_history",
            "How P&L moved over the last N hours (from SQL history): start, now, change, high, low and a few sample points. account = client id/name for one demo account, 'vijay' for the real Fyers account, or omit for the whole demo book.",
            new { account = Str("client id / name, 'vijay', or omit for all"), hours = new { type = "number", description = "look-back hours, default 1, max 24" } }),
        Tool("get_rankings",
            "Rank demo accounts: best P&L, worst P&L, most open positions, or most at-risk positions.",
            new { metric = new { type = "string", @enum = new[] { "pnl_top", "pnl_bottom", "open_positions", "at_risk" } }, limit = new { type = "integer", description = "default 5" } },
            "metric"),
        Tool("get_strategy_pnl",
            "P&L by strategy across all demo accounts (realized, unrealized, open/closed positions, accounts). With a strategy name, details for that one incl. its best/worst accounts.",
            new { strategy = Str("strategy name, e.g. 'S1EN' (optional)"), limit = new { type = "integer", description = "rows when listing, default 8" } }),
        Tool("get_basket_summary",
            "Baskets: accounts, open positions and P&L per basket, plus the strategies in a basket. Optional basket name.",
            new { basket = Str("basket name, e.g. 'BASKET 06' (optional)") }),
        Tool("get_infrastructure",
            "Health of the platform: OMS containers (running/stopped, unhealthy, CPU), data pipeline (Redis, SQL persisters), Delta BTC feed, Fyers poller.",
            new { }),
        Tool("get_broker_rejections",
            "Open broker order rejections (alerts) with reason (margin shortfall, RMS, price band…), optionally for one broker or account.",
            new { broker = Str("e.g. 'ANT', 'FYERS' (optional)"), account = Str("broker account number (optional)"), limit = new { type = "integer", description = "default 5" } }),
        Tool("get_exits_and_cleanup",
            "Exits currently in flight (queued/sent/partial/retrying) and positions that need cleanup (qty mismatch, orphan, stale), optionally for one account.",
            new { account = Str("client id / name (optional)") }),
        Tool("get_recent_signals",
            "Most recent trading signals, newest first; filter by attention (problems), open, exited or all, and by symbol or strategy.",
            new
            {
                filter = new { type = "string", @enum = new[] { "all", "attention", "open", "exited" }, description = "default all" },
                symbol = Str("symbol, partial ok (optional)"), strategy = Str("strategy name (optional)"),
                limit = new { type = "integer", description = "default 5" },
            }),
        Tool("get_stock_quote",
            "Live price of ANY NSE/BSE listed stock or index from Fyers (read-only market data): last price, change, change %, open/high/low, previous close, volume. Accepts company names, nicknames or symbols, several at once, e.g. 'Reliance, TCS, HDFC Bank' or 'SBI' or 'NSE:INFY-EQ'. Prices are INR.",
            new { stocks = Str("one or more company names / symbols, comma-separated") }, "stocks"),
        Tool("get_market_indices",
            "Current NIFTY 50, BANK NIFTY and SENSEX levels (real Fyers quotes when connected; otherwise the simulated NIFTY).",
            new { }),
        Tool("get_btc_change",
            "How BTCUSD moved over the last N hours from stored 1-minute candles: open, now, change %, high, low, volume.",
            new { hours = new { type = "number", description = "look-back hours, default 24, max 168" } }),
        Tool("get_today_activity",
            "Today's OMS activity: signals per hour and the order funnel (orders, filled, rejected, working, fill rate).",
            new { }),
        Tool("get_expiring_subscriptions",
            "Client subscriptions ending within N days (name, broker, plan, days left). Text answer only; to OPEN / SHOW / NAVIGATE to them on screen use show_on_screen with target subscriptions_expiring instead.",
            new { days = new { type = "integer", description = "default 7" } }),
        Tool("get_signal_accounts",
            "Per-account execution of ONE signal (by id, or latest for a symbol): which accounts filled, which were rejected or partial and WHY (reject reason / partial reason), qty still open. Use for 'which accounts didn't get signal X', 'why did 1023 miss it'.",
            new
            {
                id = new { type = "integer", description = "signal id" }, symbol = Str("symbol, partial ok (latest signal for it)"),
                only = new { type = "string", @enum = new[] { "problems", "all" }, description = "default problems (rejected/partial/open only)" },
            }),
        Tool("get_symbol_exposure",
            "Net exposure per instrument across all demo accounts: net qty, accounts, avg price, LTP, unrealized P&L. Filter by symbol or a description like 'NIFTY 25300 CE'. Use for 'what is my exposure to …', 'how much 25300 CE are we short'.",
            new { symbol = Str("symbol or strike/type, e.g. 'NIFTY17NOV26C25300' or '25300 CE' (optional; omit for the biggest exposures)"), limit = new { type = "integer", description = "default 8" } }),
        Tool("get_unhedged_positions",
            "Short option positions whose protective hedge legs are missing or incomplete (status NAKED or PARTIAL), optionally for one account.",
            new { account = Str("client id / name (optional)") }),
        Tool("get_fyers_orders_today",
            "Vijay's REAL Fyers orders today with status and the broker's message (e.g. why an order was rejected). Filter rejected / pending / completed / cancelled / all.",
            new { status = new { type = "string", @enum = new[] { "all", "rejected", "pending", "completed", "cancelled" }, description = "default all" }, limit = new { type = "integer", description = "default 10" } }),
        Tool("show_on_screen",
            "Open the console page and visually POINT AT (highlight) something for the user. Use when they say show / point / highlight / take me to / where is / open. targets: 'containers_down' = every stopped, unhealthy or no-heartbeat container (default for 'which container is not working'); 'container' = one container by name or client id; 'signal' = one signal by id; 'account' = one account's positions by client id/name; 'subscription' = a subscriber's basket subscriptions (id = person's name, user id or broker account) on the Basket Subscriptions page; 'subscriptions_expiring' = active subscriptions ending within N days (id = days, default 7; 'a week' = 7); 'chart' = open the live candlestick chart of NIFTY50, BANKNIFTY, SENSEX or BTCUSD (id = symbol, optionally followed by a timeframe 1m/5m/15m/1h/1d, e.g. 'SENSEX 15m'). Then answer briefly what is highlighted and why (for subscriptions include status and days left).",
            new
            {
                target = new { type = "string", @enum = new[] { "containers_down", "container", "signal", "account", "subscription", "subscriptions_expiring", "chart" } },
                id = Str("container name / client id, signal id, account, subscriber name, or number of days (not needed for containers_down)"),
            }, "target"),
        Tool("get_help",
            "Explain terms and how-tos of this console from its documentation: what a status/label means (exit partial, residual, DLQ, chunk, in-flight exit, hedge…), how to do something (log in to Fyers, renew session, use filters, run the simulator…).",
            new { topic = Str("the question or term, e.g. 'exit partial' or 'renew Fyers session'") }, "topic"),
    ];

    // ------------------------------------------------------------------ routing
    // Sending all ~19 tool schemas costs ~1.5k tokens per call; Groq's free tier allows 8k tokens/min. So each question
    // only gets the tools whose keywords match it (+ a small core set). If nothing specific matches, all tools are sent.

    private static readonly string[] Core = ["find_broker_account", "get_positions", "get_portfolio_summary"];

    private static readonly Dictionary<string, string[]> Keywords = new()
    {
        ["get_btc_price"] = ["btc", "bitcoin", "crypto", "delta"],
        ["get_btc_change"] = ["btc", "bitcoin", "crypto", "delta"],
        ["get_fyers_account"] = ["vijay", "my ", " me", "mine", "fyers", "fund", "balance", "margin", "holding", "real account"],
        ["get_open_issues"] = ["issue", "problem", "attention", "error", "solve", "fix", "wrong", "broken", "alert"],
        ["get_signal"] = ["signal"],
        ["get_recent_signals"] = ["signal", "latest", "recent", "partial", "residual"],
        ["get_pnl_history"] = ["history", "trend", "moved", "move", "change", "since", "last hour", "hours", "minutes", "earlier", "over time", "graph", "chart"],
        ["get_rankings"] = ["top", "best", "worst", "most", "least", "highest", "lowest", "rank", "gainer", "loser", "winning", "losing", "biggest"],
        ["get_strategy_pnl"] = ["strateg"],
        ["get_basket_summary"] = ["basket"],
        ["get_infrastructure"] = ["container", "infra", "server", "cpu", "memory", "redis", "sql", "pipeline", "health", "feed", "heartbeat", "poller", "down", "running"],
        ["get_broker_rejections"] = ["reject", "rejection", "alert", "margin", "rms", "why"],
        ["get_exits_and_cleanup"] = ["exit", "in-flight", "inflight", "in flight", "cleanup", "clean up", "mismatch", "orphan", "stuck", "stale"],
        ["get_market_indices"] = ["nifty", "banknifty", "bank nifty", "sensex", "index", "indices", "market"],
        ["get_stock_quote"] = ["stock", "share", "price", "quote", "trading at", "ltp", "rate of", "worth", "nse", "bse", "equity", "ltd", "limited", "bank", "motors", "industries"],
        ["get_today_activity"] = ["activity", "today", "funnel", "fill rate", "per hour", "how many signals", "orders"],
        ["get_expiring_subscriptions"] = ["subscription", "subscri", "subcri", "expir", "renew", "plan"],
        ["get_signal_accounts"] = ["signal", "didn't get", "did not get", "didnt get", "missed", "which accounts", "not filled", "why did"],
        ["get_symbol_exposure"] = ["exposure", "exposed", "net qty", "net quantity", "strike", " ce", " pe", "across all accounts", "how much are we", "short in", "long in"],
        ["get_unhedged_positions"] = ["hedge", "naked", "unhedged", "unprotected", "protected", "protection"],
        ["get_fyers_orders_today"] = ["order", "rejected", "my reject"],
        ["show_on_screen"] = ["show", "point", "highlight", "take me", "navi", "nagiv", "go to", "open ", "where is", "where's", "locate", "filter", "chart", "graph", "candle"],
        ["get_help"] = ["what does", "what is a", "what's a", "what is the meaning", "mean", "meaning", "explain", "how do i", "how to", "help", "where can i", "what are"],
    };

    /// <summary>Tool schemas relevant to the recent user text (see routing note above).</summary>
    public static JsonArray Definitions(string? recentUserText)
    {
        var all = AllDefinitions();
        var text = " " + (recentUserText ?? "").ToLowerInvariant() + " ";
        var names = new HashSet<string>(Core);
        foreach (var (tool, words) in Keywords) if (words.Any(text.Contains)) names.Add(tool);
        if (Catalog.Strategies.Any(st => text.Contains(st.Name.ToLowerInvariant()))) names.Add("get_strategy_pnl");
        if (Catalog.Baskets.Any(b => text.Contains(b.Name.ToLowerInvariant()))) names.Add("get_basket_summary");
        if (names.Count == Core.Length) return all;            // nothing specific → let the model see everything
        var picked = new JsonArray();
        foreach (var t in all.ToList())
            if (names.Contains(t!["function"]!["name"]!.GetValue<string>())) { all.Remove(t); picked.Add(t); }
        return picked;
    }

    public static int Count => AllDefinitions().Count;

    private static JsonObject Tool(string name, string description, object properties, params string[] required) => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = JsonSerializer.SerializeToNode(properties),
                ["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray()),
            },
        },
    };
    private static object Str(string d) => new { type = "string", description = d };

    private static readonly System.Globalization.CultureInfo IN = new("en-IN");
    /// <summary>Pre-formatted rupees (₹1,23,456.78) so the model copies them instead of re-grouping digits.</summary>
    public static string Inr(decimal v) => (v < 0 ? "-₹" : "₹") + Math.Abs(Math.Round(v, 2)).ToString("#,##,##0.00", IN);

    // ------------------------------------------------------------------ execution

    public async Task<object> RunAsync(string name, JsonObject args)
    {
        string? S(string k) => args[k]?.GetValueKind() == JsonValueKind.String ? args[k]!.GetValue<string>().Trim() : args[k]?.ToString();
        int? I(string k) => int.TryParse(S(k), out var v) ? v : double.TryParse(S(k), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? (int)d : null;
        double? D(string k) => double.TryParse(S(k), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
        return name switch
        {
            "get_btc_price" => BtcPrice(),
            "get_fyers_account" => await FyersAccountAsync(),
            "find_broker_account" => FindAccount(S("query") ?? ""),
            "get_positions" => Positions(S("account"), S("status"), S("strategy"), I("limit") ?? 10),
            "get_open_issues" => OpenIssues(),
            "get_portfolio_summary" => Portfolio(),
            "get_signal" => Signal(I("id"), S("symbol")),
            "get_pnl_history" => await PnlHistoryAsync(S("account"), D("hours") ?? 1),
            "get_rankings" => Rankings(S("metric") ?? "pnl_top", I("limit") ?? 5),
            "get_strategy_pnl" => StrategyPnl(S("strategy"), I("limit") ?? 8),
            "get_basket_summary" => BasketSummary(S("basket")),
            "get_infrastructure" => Infrastructure(),
            "get_broker_rejections" => Rejections(S("broker"), S("account"), I("limit") ?? 5),
            "get_exits_and_cleanup" => ExitsAndCleanup(S("account")),
            "get_recent_signals" => RecentSignals(S("filter") ?? "all", S("symbol"), S("strategy"), I("limit") ?? 5),
            "get_market_indices" => await IndicesAsync(),
            "get_stock_quote" => await StockQuoteAsync(S("stocks") ?? S("stock") ?? S("symbol") ?? ""),
            // charts can be any NSE/BSE stock → resolving needs the (async) symbol master, so it runs outside ShowOnScreen's lock
            "show_on_screen" when (S("target") ?? "") == "chart" => await ShowChartAsync(S("id")),
            "get_btc_change" => await BtcChangeAsync(D("hours") ?? 24),
            "get_today_activity" => TodayActivity(),
            "get_expiring_subscriptions" => Subscriptions(I("days") ?? 7),
            "get_signal_accounts" => SignalAccounts(I("id"), S("symbol"), S("only") ?? "problems"),
            "get_symbol_exposure" => SymbolExposure(S("symbol"), I("limit") ?? 8),
            "get_unhedged_positions" => Unhedged(S("account")),
            "get_fyers_orders_today" => await FyersOrdersAsync(S("status") ?? "all", I("limit") ?? 10),
            "get_help" => Help(S("topic") ?? ""),
            "show_on_screen" => ShowOnScreen(S("target") ?? "containers_down", S("id")),
            _ => new { error = $"Unknown tool '{name}'" },
        };
    }

    private object BtcPrice()
    {
        var s = delta.Snapshot();
        var t = s.Ticker;
        if (t is null) return new { error = "No BTC data yet (Delta feed not connected)", feedConnected = s.Status.Connected };
        string Usd(decimal v) => "$" + v.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
        return new
        {
            currency = "USD (US dollars, not rupees)",
            summary = $"BTCUSD last {Usd(t.Last)}, mark {Usd(t.Mark)}, 24h {t.ChangePct:+0.00;-0.00}% (high {Usd(t.High)}, low {Usd(t.Low)})",
            symbol = t.Symbol, lastUsd = t.Last, markUsd = t.Mark, indexUsd = t.Spot, change24hPct = t.ChangePct,
            high24h = t.High, low24h = t.Low, fundingRatePct = t.FundingRate, openInterestUsd = t.OiUsd,
            bestBid = t.BestBid, bestAsk = t.BestAsk, feedConnected = s.Status.Connected,
            asOfIst = DateTimeOffset.FromUnixTimeMilliseconds(t.Time > 100_000_000_000_000 ? t.Time / 1000 : t.Time).UtcDateTime.AddHours(5.5).ToString("HH:mm:ss"),
        };
    }

    private async Task<object> FyersAccountAsync()
    {
        if (!fyers.Configured) return new { connected = false, error = "Fyers is not configured. Log in on Broker Live → Login with Fyers." };
        try
        {
            var profile = await fyers.Profile();
            var funds = await fyers.Funds();
            var pos = await fyers.Positions();
            var hold = await fyers.Holdings();
            var orders = await fyers.Orders();
            var open = pos.NetPositions.Where(p => p.NetQty != 0).ToList();
            return new
            {
                connected = true, name = profile.Data?.Name, fyersId = profile.Data?.FyId,
                funds = funds.FundLimit.Where(f => f.EquityAmount != 0 || f.CommodityAmount != 0)
                                       .Select(f => new { f.Title, equity = Inr(f.EquityAmount), commodity = Inr(f.CommodityAmount) }),
                pnl = $"total {Inr(pos.Overall?.PlTotal ?? pos.NetPositions.Sum(p => p.Pl))} " +
                      $"(realized {Inr(pos.Overall?.PlRealized ?? pos.NetPositions.Sum(p => p.RealizedProfit))}, " +
                      $"unrealized {Inr(pos.Overall?.PlUnrealized ?? pos.NetPositions.Sum(p => p.UnrealizedProfit))})",
                openPositions = open.Count, totalPositionsToday = pos.NetPositions.Count,
                positions = open.Take(20).Select(p => new { p.Symbol, p.NetQty, avg = p.NetAvg, p.Ltp, pl = Inr(p.Pl), p.ProductType }),
                holdings = new { count = hold.Holdings.Count, pl = Inr(hold.Holdings.Sum(h => h.Pl)), marketValue = Inr(hold.Holdings.Sum(h => h.MarketVal)) },
                ordersToday = orders.OrderBook.GroupBy(o => Controllers.FyersController.OrderStatus(o.Status)).ToDictionary(g => g.Key, g => g.Count()),
            };
        }
        catch (FyersException ex)
        {
            return new { connected = false, error = ex.Status == 401 ? "Fyers session expired. Log in again on Broker Live (Login with Fyers / Renew with PIN)." : ex.Message };
        }
    }

    private List<BrokerAccount> MatchAccounts(string q)
    {
        // tolerate "client 1023", "broker account #1023", "account no. 52062393", "Kavya's"
        q = System.Text.RegularExpressions.Regex.Replace(q, @"\b(client|broker|account|acct|id|no|number|of|for|the)\b\.?|'s\b|#", " ",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        q = System.Text.RegularExpressions.Regex.Replace(q, @"\s+", " ").Trim();
        if (q.Length == 0) return [];
        var exact = store.Accounts.Where(a => a.ClientId == q || a.AccountId.Equals(q, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count > 0) return exact;
        // a system user id from User Management ("user 8606", "#8606") → the OMS accounts that user owns
        var owner = store.Users.FirstOrDefault(u => u.Id.ToString() == q.Replace("user", "", StringComparison.OrdinalIgnoreCase).Trim());
        if (owner is not null)
        {
            var mine = owner.Accounts.Select(l => l.ClientId).ToHashSet();
            var owned = store.Accounts.Where(a => mine.Contains(a.ClientId)).ToList();
            if (owned.Count > 0) return owned;
        }
        return store.Accounts.Where(a => a.AccountId.Contains(q, StringComparison.OrdinalIgnoreCase)
                                      || a.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                      || a.Broker.Equals(q, StringComparison.OrdinalIgnoreCase)).Take(5).ToList();
    }

    private object FindAccount(string query)
    {
        lock (store.SyncRoot)
        {
            var matches = MatchAccounts(query);
            if (matches.Count == 0)
                return new
                {
                    found = false, query,
                    hint = "No demo OMS account matches. If this is Vijay's own account, use get_fyers_account. OMS client ids look like 1023, 8616, 1360; system user ids (User Management, #8601-#8760) are also accepted.",
                };
            return new
            {
                found = true,
                accounts = matches.Select(a =>
                {
                    var c = store.Containers.FirstOrDefault(x => x.ClientId == a.ClientId);
                    var owner = store.Users.FirstOrDefault(u => u.Accounts.Any(l => l.ClientId == a.ClientId));
                    var alerts = store.AlertAccounts.Where(x => x.AccountId == a.AccountId).Sum(x => x.Open);
                    return new
                    {
                        clientId = a.ClientId, a.Name, a.Broker, brokerAccount = a.AccountId, a.Basket,
                        systemUser = owner is null ? null : $"#{owner.Id} {owner.Name} (User Management id; the OMS client id is {a.ClientId})",
                        status = a.Status, sessionValid = a.SessionValid, tokenStale = a.TokenStale,
                        strategies = a.Strategies, excludedStrategies = a.ExcludedStrategies,
                        openPositions = a.OpenPositions, closedToday = a.ClosedToday,
                        atRiskPositions = a.Positions.Count(p => p.Status == "RISK"),
                        pnl = $"total {Inr(a.StrategyPnl)} (realized {Inr(a.Realized)}, unrealized {Inr(a.Unrealized)})",
                        container = c is null ? null : new { c.Name, c.Status, c.Health, heartbeat = c.Heartbeat, cpuPct = c.CpuPct },
                        cleanupIssues = store.CleanupItems.Count(x => x.ClientId == a.ClientId),
                        inFlightExits = store.InFlightExits.Count(x => x.ClientId == a.ClientId),
                        openBrokerRejections = alerts,
                    };
                }).ToList(),
            };
        }
    }

    private object Positions(string? account, string? status, string? strategy, int limit)
    {
        lock (store.SyncRoot)
        {
            IEnumerable<(BrokerAccount a, PositionRecord p)> q = store.Accounts.SelectMany(a => a.Positions.Select(p => (a, p)));
            string? scope = null;
            if (!string.IsNullOrWhiteSpace(account))
            {
                var m = MatchAccounts(account);
                if (m.Count == 0) return new { found = false, account, hint = "No demo account matches; for Vijay's own account use get_fyers_account." };
                var ids = m.Select(x => x.ClientId).ToHashSet();
                q = q.Where(x => ids.Contains(x.a.ClientId));
                scope = string.Join(", ", m.Select(x => $"{x.ClientId} ({x.Name}, {x.Broker})"));
            }
            if (!string.IsNullOrWhiteSpace(strategy)) q = q.Where(x => x.p.Strategy.Equals(strategy, StringComparison.OrdinalIgnoreCase));
            var all = q.ToList();
            var filter = (status ?? "open").ToLowerInvariant();
            var rows = filter switch
            {
                "closed" => all.Where(x => x.p.NetQty == 0),
                "risk" => all.Where(x => x.p.Status == "RISK"),
                "all" => all,
                _ => all.Where(x => x.p.NetQty != 0),
            };
            return new
            {
                scope = scope ?? "all accounts", strategy,
                open = all.Count(x => x.p.NetQty != 0), closed = all.Count(x => x.p.NetQty == 0), atRisk = all.Count(x => x.p.Status == "RISK"),
                pnl = $"unrealized {Inr(all.Sum(x => x.p.Unrealized))}, realized {Inr(all.Sum(x => x.p.Realized))}",
                listed = filter,
                rows = rows.OrderBy(x => x.p.Unrealized).Take(Math.Clamp(limit, 1, 30)).Select(x => new
                {
                    clientId = x.a.ClientId, x.p.Symbol, side = x.p.EntrySide, netQty = x.p.NetQty, avg = x.p.NetQty > 0 ? x.p.AvgBuy : x.p.AvgSell,
                    ltp = x.p.Ltp, unrealized = Inr(x.p.Unrealized), realized = Inr(x.p.Realized), pnlPct = x.p.PnlPct, x.p.Strategy, x.p.Status,
                }),
            };
        }
    }

    private object OpenIssues()
    {
        lock (store.SyncRoot)
        {
            var today = MockDataStore.NowIst.Date;
            var attention = store.Signals.Where(s => s.NeedsAttention && s.ReachedApiAt.Date >= today.AddDays(-2)).ToList();
            var badContainers = store.Containers.Where(c => c.Status != "Running" || c.Health != "Healthy" || !c.Heartbeat).ToList();
            var openRejections = store.AlertAccounts.Sum(a => a.Open);
            var o = store.GetOverview();
            var total = attention.Count + openRejections + store.CleanupItems.Count + badContainers.Count;
            return new
            {
                summary = $"{total} actionable: {attention.Count} signals need attention ({attention.Count(s => s.Health == "residual")} exit partial, " +
                          $"{attention.Count(s => s.Health == "error")} with errors), {openRejections} open broker rejections, " +
                          $"{store.CleanupItems.Count} positions need cleanup, {badContainers.Count} containers with problems; " +
                          $"also {store.InFlightExits.Count} exits in flight.",
                totalActionable = total,
                signalsNeedingAttention = new
                {
                    count = attention.Count,
                    exitPartial = attention.Count(s => s.Health == "residual"),
                    withErrors = attention.Count(s => s.Health == "error"),
                    worst = attention.OrderByDescending(s => s.ResidualQty).ThenByDescending(s => s.UnresolvedIssues).Take(3)
                                     .Select(s => new { s.Id, s.Symbol, s.Health, residualQty = s.ResidualQty, issues = s.UnresolvedIssues }),
                },
                openBrokerRejections = openRejections,
                positionsNeedingCleanup = store.CleanupItems.Count,
                cleanupByType = store.CleanupItems.GroupBy(c => c.Issue).ToDictionary(g => g.Key, g => g.Count()),
                inFlightExits = new { total = store.InFlightExits.Count, retrying = store.InFlightExits.Count(e => e.Stage == "RETRYING") },
                containersWithProblems = badContainers.Select(c => new { c.Name, c.ClientId, c.Status, c.Health }),
                deadLettersInRange = o.DeadLettersInRange, failedExecutionsInRange = o.FailedExecutionsInRange,
                sessionsInvalid = store.Accounts.Count(a => !a.SessionValid),
            };
        }
    }

    private object Portfolio()
    {
        var sim = store.GetSimStatus(0);
        lock (store.SyncRoot)
        {
            var rank = store.Accounts.OrderByDescending(a => a.StrategyPnl).ToList();
            object Row(BrokerAccount a) => new { clientId = a.ClientId, a.Name, a.Broker, pnl = Inr(a.StrategyPnl) };
            return new
            {
                accounts = store.Accounts.Count, activeSessions = store.Accounts.Count(a => a.SessionValid),
                openPositions = store.Accounts.Sum(a => a.OpenPositions), closedToday = store.Accounts.Sum(a => a.ClosedToday),
                pnl = $"total {Inr(store.Accounts.Sum(a => a.StrategyPnl))} (realized {Inr(store.Accounts.Sum(a => a.Realized))}, unrealized {Inr(store.Accounts.Sum(a => a.Unrealized))})",
                topGainers = rank.Take(3).Select(Row), topLosers = rank.AsEnumerable().Reverse().Take(3).Select(Row),
                niftySpotIndexPoints = $"{sim.Spot:N2} points (an index level, not rupees)", niftyChangePct = sim.SpotChangePct, scenario = sim.Scenario, simulatorRunning = sim.Running,
            };
        }
    }

    private object Signal(int? id, string? symbol)
    {
        lock (store.SyncRoot)
        {
            var s = id is { } i ? store.Signals.FirstOrDefault(x => x.Id == i)
                  : !string.IsNullOrWhiteSpace(symbol) ? store.Signals.Where(x => x.Symbol.Contains(symbol, StringComparison.OrdinalIgnoreCase)).MaxBy(x => x.ReachedApiAt)
                  : null;
            if (s is null) return new { found = false, id, symbol };
            return new
            {
                found = true, s.Id, s.Symbol, s.Exchange, strategy = s.StrategyName, s.Basket, s.Side, s.Type,
                status = s.EffectiveStatus, exitStatus = s.EffectiveExitStatus, recordedExitStatus = s.ExitStatus, health = s.Health,
                requested = s.Requested, filled = s.Filled, exited = s.Exited, residualQty = s.ResidualQty,
                accounts = s.TotalAccounts, openAccounts = s.OpenAccounts, unresolvedIssues = s.UnresolvedIssues,
                reachedApiIst = s.ReachedApiAt.ToString("dd MMM yyyy HH:mm:ss"),
            };
        }
    }

    // ==================================================================== on-screen pointing

    /// <summary>Set by show_on_screen; AssistantService returns it to the browser, which navigates and highlights.</summary>
    public UiAction? Action { get; private set; }

    private const string ConsoleRoute = "/admin/strategy-performance-console-v2";

    private static string Why(ContainerInfo c) => string.Join(" · ", new[]
    {
        c.Status != "Running" ? c.Status : null,
        c.Health != "Healthy" ? c.Health : null,
        !c.Heartbeat ? "no heartbeat" : null,
        c.CpuPct > 40 ? $"CPU {c.CpuPct:0.#}%" : null,
    }.Where(x => x is not null));

    private object ShowOnScreen(string target, string? id)
    {
        lock (store.SyncRoot)
        {
            switch (target)
            {
                case "containers_down":
                case "container":
                {
                    var all = store.GetContainers().Containers;
                    List<ContainerInfo> picked;
                    var onlyDegraded = false;
                    if (target == "container")
                    {
                        var q = (id ?? "").Trim();
                        picked = all.Where(c => c.Name.Equals(q, StringComparison.OrdinalIgnoreCase) || c.ClientId == q).ToList();
                        if (picked.Count == 0 && q.Length > 0) picked = all.Where(c => c.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || c.Accounts.Contains(q)).Take(3).ToList();
                        if (picked.Count == 0) return new { shown = false, error = $"No container matches '{id}'." };
                    }
                    else
                    {
                        picked = all.Where(c => c.Status != "Running" || c.Health == "Unhealthy" || !c.Heartbeat).ToList();
                        if (picked.Count == 0)
                        {
                            picked = all.Where(c => c.Health == "Degraded").ToList();
                            if (picked.Count == 0) return new { shown = false, allHealthy = true, total = all.Count, note = "Every container is running and healthy; nothing to point at." };
                            onlyDegraded = true;   // nothing down, but point at the degraded ones
                        }
                    }
                    Action = new UiAction(ConsoleRoute, new() { ["tab"] = "infrastructure" },
                        picked.Select(c => new UiTarget("container:" + c.Name, $"{c.Name} — {(Why(c) is { Length: > 0 } w ? w : "Healthy")}")).ToList(), null);
                    return new
                    {
                        shown = true, page = "Console → Infrastructure", onlyDegraded,
                        highlighted = picked.Select(c => new { c.Name, c.ClientId, c.Status, c.Health, heartbeat = c.Heartbeat, cpuPct = c.CpuPct, problem = Why(c) }).ToList(),
                    };
                }
                case "signal":
                {
                    var s = long.TryParse((id ?? "").Trim().TrimStart('#'), out var sid) ? store.Signals.FirstOrDefault(x => x.Id == sid) : null;
                    if (s is null) return new { shown = false, error = $"Signal '{id}' not found." };
                    var day = s.ReachedApiAt.ToString("yyyy-MM-dd");
                    Action = new UiAction(ConsoleRoute, new() { ["tab"] = "signals" },
                        [new UiTarget("signal:" + s.Id, $"Signal {s.Id} — {s.EffectiveStatus} · {s.EffectiveExitStatus}")],
                        new() { ["correlationId"] = s.CorrelationId, ["from"] = day, ["to"] = day });
                    return new { shown = true, page = "Console → Live Signals", s.Id, s.Symbol, status = s.EffectiveStatus, exitStatus = s.EffectiveExitStatus, s.Health };
                }
                case "account":
                {
                    var a = MatchAccounts(id ?? "").FirstOrDefault();
                    if (a is null) return new { shown = false, error = $"No account matches '{id}'." };
                    Action = new UiAction(ConsoleRoute, new() { ["tab"] = "positions" },
                        [new UiTarget("account:" + a.ClientId, $"Client {a.ClientId} — {a.Name} · {a.Broker}")],
                        new() { ["clientId"] = a.ClientId });
                    return new { shown = true, page = "Console → Positions & Exits", a.ClientId, a.Name, a.Broker };
                }
                case "subscription":
                {
                    var q = (id ?? "").Trim().TrimStart('#');
                    var users = store.Users.Where(u => q.Length > 0 && (u.Id.ToString() == q || u.Name.Equals(q, StringComparison.OrdinalIgnoreCase))).ToList();
                    if (users.Count == 0)
                        users = store.Users.Where(u => q.Length > 1 && (u.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                    || u.Accounts.Any(a => a.AccountId.Equals(q, StringComparison.OrdinalIgnoreCase) || a.ClientId == q))).Take(5).ToList();
                    if (users.Count == 0) return new { shown = false, error = $"No subscriber matches '{id}'." };
                    var ids = users.Select(u => u.Id).ToHashSet();
                    var search = users.Count == 1 ? users[0].Name : q;
                    var subs = store.ListSubscriptions(null, search, null, null, null, 1, 100).Items.Where(x => ids.Contains(x.UserId)).ToList();
                    if (subs.Count == 0) return new { shown = false, subscribers = users.Select(u => u.Name), error = "This subscriber has no basket subscriptions." };
                    Action = new UiAction("/admin/basket-subscriptions", new() { ["search"] = search, ["status"] = "all" },
                        subs.Take(20).Select(x => new UiTarget("sub:" + x.Id, $"{x.UserName} · {x.Basket} — {x.Status}{(x.Status is "Cancelled" ? "" : x.DaysLeft > 0 ? $" · {x.DaysLeft} days left" : " · ended")}")).ToList(), null);
                    return new
                    {
                        shown = true, page = "Basket Management → Basket Subscriptions",
                        subscriptions = subs.Select(x => new { x.UserName, x.UserId, x.Basket, x.Status, x.DaysLeft, end = x.End.ToString("dd MMM yyyy"), x.Broker, x.AccountId, x.Multiplier }),
                    };
                }
                case "subscriptions_expiring":
                {
                    var days = int.TryParse(System.Text.RegularExpressions.Regex.Match(id ?? "", @"\d+").Value, out var dd) ? Math.Clamp(dd, 1, 90) : 7;
                    var page = store.ListSubscriptions("active", null, null, null, days, 1, 100);
                    if (page.Total == 0) return new { shown = false, days, note = $"No active subscription ends within {days} day(s)." };
                    Action = new UiAction("/admin/basket-subscriptions", new() { ["status"] = "active", ["expiring"] = days.ToString() },
                        page.Items.OrderBy(x => x.DaysLeft).Take(20).Select(x => new UiTarget("sub:" + x.Id, $"{x.UserName} · {x.Basket} — {x.DaysLeft} day(s) left")).ToList(), null);
                    return new
                    {
                        shown = true, page = "Basket Management → Basket Subscriptions (Active, expiring filter)", days, count = page.Total,
                        subscriptions = page.Items.OrderBy(x => x.DaysLeft).Select(x => new { x.UserName, x.Basket, x.DaysLeft, end = x.End.ToString("dd MMM yyyy"), x.Broker, x.AccountId }),
                    };
                }
                default:
                    return new { shown = false, error = $"Unknown target '{target}'." };
            }
        }
    }

    // ==================================================================== charts + stock quotes (any NSE/BSE instrument)

    /// <summary>show_on_screen target 'chart': NIFTY 50 / BANK NIFTY / SENSEX / BTCUSD tabs, or ANY stock ("Reliance 15m").</summary>
    private async Task<object> ShowChartAsync(string? id)
    {
        var raw = (id ?? "NIFTY50").Trim();
        var tf = System.Text.RegularExpressions.Regex.Match(raw, @"\b(1m|5m|15m|1h|1d)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Value.ToLowerInvariant();
        var name = System.Text.RegularExpressions.Regex.Replace(raw, @"\b(1m|5m|15m|1h|1d|chart|charts|live|graph|candles?)\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        var key = name.ToUpperInvariant().Replace(" ", "").Replace("-", "") switch
        {
            "BTC" or "BTCUSD" or "BITCOIN" or "BTCUSDT" => "BTCUSD",
            "NIFTY" or "NIFTY50" or "NIFTYFIFTY" => "NIFTY50",
            "BANKNIFTY" or "NIFTYBANK" or "BNF" => "BANKNIFTY",
            "SENSEX" or "BSESENSEX" => "SENSEX",
            _ => null,
        };
        string label, source;
        if (key is not null)
        {
            label = key switch { "NIFTY50" => "NIFTY 50", "BANKNIFTY" => "BANK NIFTY", _ => key };
            source = key == "BTCUSD" ? "Delta Exchange (live 24x7)" : "Fyers historical data + live quotes (needs a valid Fyers session; market hours 09:15–15:30 IST)";
        }
        else
        {
            var hit = await symbols.ResolveAsync(name);
            if (hit is null) return new { shown = false, error = $"No NSE/BSE stock or index matches '{name}'. Try the company name or its NSE symbol." };
            key = hit.Ticker; label = $"{hit.Name} ({hit.Ticker})";
            source = "Fyers historical data + live quotes (needs a valid Fyers session; market hours 09:15–15:30 IST)";
        }
        var q = new Dictionary<string, string> { ["symbol"] = key };
        if (tf.Length > 0) q["tf"] = tf;
        Action = new UiAction("/admin/market-charts", q, [new UiTarget("chart:" + key, $"{label} live chart{(tf.Length > 0 ? " · " + tf : "")}")], null);
        return new { shown = true, page = "Market Charts", symbol = label, timeframe = tf.Length > 0 ? tf : "5m", source };
    }

    /// <summary>Live quotes for any NSE/BSE stocks / indices, e.g. "Reliance, TCS and HDFC Bank".</summary>
    private async Task<object> StockQuoteAsync(string stocks)
    {
        var names = System.Text.RegularExpressions.Regex.Split(stocks, @",|;|\band\b|&(?=\s)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        if (names.Count == 0) return new { error = "Name at least one stock, e.g. 'Reliance' or 'TCS, Infosys'." };
        var found = new List<(string Asked, FyersInstrument I)>();
        var unknown = new List<object>();
        foreach (var n in names)
        {
            var hits = await symbols.SearchAsync(n, 3);
            if (hits.Count > 0) found.Add((n, hits[0]));
            else unknown.Add(new { asked = n, note = "No NSE/BSE listing matches this name." });
        }
        if (found.Count == 0) return new { error = "None of these matched an NSE/BSE listing.", unknown };
        if (!fyers.Configured) return new { connected = false, error = "Fyers is not connected. Log in on Broker Live → Login with Fyers to get live stock prices.", matched = found.Select(f => f.I.Ticker) };
        try
        {
            var res = await fyers.Quotes(string.Join(',', found.Select(f => f.I.Ticker).Distinct()));
            var byTicker = res.D.Where(d => d.V is not null).ToDictionary(d => d.N ?? "", d => d.V!);
            var istNow = MockDataStore.NowIst;
            var open = istNow.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && istNow.TimeOfDay >= new TimeSpan(9, 15, 0) && istNow.TimeOfDay <= new TimeSpan(15, 30, 0);
            return new
            {
                currency = "INR", marketOpen = open,
                note = open ? "Live prices (NSE/BSE are open)." : "NSE/BSE are closed now: these are the last traded prices of the latest session.",
                quotes = found.Select(f =>
                {
                    byTicker.TryGetValue(f.I.Ticker, out var v);
                    return v is null
                        ? (object)new { asked = f.Asked, f.I.Name, symbol = f.I.Ticker, error = "No quote returned" }
                        : new
                        {
                            asked = f.Asked, f.I.Name, symbol = f.I.Ticker,
                            summary = $"{f.I.Name} ({f.I.Ticker}): {Inr(v.Lp)} {(v.Ch >= 0 ? "+" : "")}{v.Ch:0.00} ({(v.Chp >= 0 ? "+" : "")}{v.Chp:0.00}%)",
                            last = Inr(v.Lp), change = $"{(v.Ch >= 0 ? "+" : "")}{v.Ch:0.00}", changePct = $"{(v.Chp >= 0 ? "+" : "")}{v.Chp:0.00}%",
                            open = Inr(v.Open), high = Inr(v.High), low = Inr(v.Low), prevClose = Inr(v.PrevClose), volume = v.Volume,
                        };
                }),
                unknown,
            };
        }
        catch (FyersException ex) when (ex.Status == 401)
        {
            return new { connected = false, error = "The Fyers session has expired. Renew it on Broker Live (Fyers) → Renew with PIN, then ask again.", matched = found.Select(f => f.I.Ticker) };
        }
    }

    // ==================================================================== more tools

    private static bool MeansVijay(string? q) =>
        q is not null && (q.Contains("vijay", StringComparison.OrdinalIgnoreCase) || q.Contains("fyers", StringComparison.OrdinalIgnoreCase)
                          || q.Trim().Equals("me", StringComparison.OrdinalIgnoreCase) || q.Trim().Equals("my", StringComparison.OrdinalIgnoreCase));

    private async Task<object> PnlHistoryAsync(string? account, double hours)
    {
        hours = Math.Clamp(hours, 0.05, 24);
        var from = DateTime.UtcNow.AddHours(-hours);
        string topic, scope; string? key = null;
        if (MeansVijay(account)) { topic = "fyers.pnl"; scope = "Vijay's Fyers account"; }
        else if (!string.IsNullOrWhiteSpace(account))
        {
            BrokerAccount? a;
            lock (store.SyncRoot) a = MatchAccounts(account).FirstOrDefault();
            if (a is null) return new { found = false, account, hint = "No demo account matches; for Vijay use account='vijay'." };
            topic = "sim.pnl"; key = a.ClientId; scope = $"client {a.ClientId} ({a.Name})";
        }
        else { topic = "sim.pnl"; scope = "whole demo book (sum of accounts)"; }

        List<RtMessage> rows;
        try { rows = await rtDb.HistoryAsync(topic, key, from, null, 5000, CancellationToken.None); }
        catch (Exception ex) { return new { error = "History store (SQL) unavailable: " + ex.Message }; }

        // series of (time, total). For the whole book, sum all accounts per snapshot time (published together)
        var series = (key is null && topic == "sim.pnl"
                ? rows.GroupBy(r => new DateTime(r.TimeUtc.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond))
                      .Where(g => g.Count() >= 5)
                      .Select(g => (t: g.Key, v: g.Sum(r => r.Data.GetProperty("total").GetDecimal())))
                : rows.Select(r => (t: r.TimeUtc, v: r.Data.GetProperty("total").GetDecimal())))
            .OrderBy(x => x.t).ToList();

        if (series.Count == 0)
            return new { scope, hours, found = false, note = topic == "fyers.pnl" ? "No Fyers P&L history yet (needs a valid Fyers session; it is recorded on change)." : "No history in this window yet (recorded every 10 s while the API runs)." };
        var first = series[0]; var last = series[^1];
        var hi = series.MaxBy(x => x.v); var lo = series.MinBy(x => x.v);
        string Ist(DateTime u) => u.AddHours(5.5).ToString("HH:mm");
        var step = Math.Max(1, series.Count / 6);
        return new
        {
            scope, hours,
            summary = $"{scope}: {Inr(first.v)} at {Ist(first.t)} → {Inr(last.v)} at {Ist(last.t)} IST (change {Inr(last.v - first.v)}); " +
                      $"high {Inr(hi.v)} at {Ist(hi.t)}, low {Inr(lo.v)} at {Ist(lo.t)}.",
            points = series.Where((_, i) => i % step == 0 || i == series.Count - 1).Select(x => new { timeIst = Ist(x.t), pnl = Inr(x.v) }),
            samples = series.Count,
        };
    }

    private object Rankings(string metric, int limit)
    {
        limit = Math.Clamp(limit, 1, 15);
        lock (store.SyncRoot)
        {
            var rows = (metric switch
            {
                "pnl_bottom" => store.Accounts.OrderBy(a => a.StrategyPnl),
                "open_positions" => store.Accounts.OrderByDescending(a => a.OpenPositions),
                "at_risk" => store.Accounts.OrderByDescending(a => a.Positions.Count(p => p.Status == "RISK")),
                _ => store.Accounts.OrderByDescending(a => a.StrategyPnl),
            }).Take(limit).Select((a, i) => new
            {
                rank = i + 1, clientId = a.ClientId, a.Name, a.Broker, pnl = Inr(a.StrategyPnl),
                openPositions = a.OpenPositions, atRisk = a.Positions.Count(p => p.Status == "RISK"),
            }).ToList();
            return new { metric, rows };
        }
    }

    private object StrategyPnl(string? strategy, int limit)
    {
        lock (store.SyncRoot)
        {
            var pos = store.Accounts.SelectMany(a => a.Positions.Select(p => (a, p))).ToList();
            if (!string.IsNullOrWhiteSpace(strategy))
            {
                var info = Catalog.Strategies.FirstOrDefault(s => s.Name.Equals(strategy, StringComparison.OrdinalIgnoreCase))
                           ?? Catalog.Strategies.FirstOrDefault(s => s.Name.Contains(strategy, StringComparison.OrdinalIgnoreCase));
                if (info is null) return new { found = false, strategy, hint = "Unknown strategy name." };
                var mine = pos.Where(x => x.p.Strategy == info.Name).ToList();
                var byAcc = mine.GroupBy(x => x.a).Select(g => (a: g.Key, v: g.Sum(x => x.p.Realized + x.p.Unrealized))).OrderBy(x => x.v).ToList();
                decimal r = mine.Sum(x => x.p.Realized), u = mine.Sum(x => x.p.Unrealized);
                return new
                {
                    found = true, strategy = info.Name, code = info.Code, basket = info.Basket, underlying = info.Underlying ?? "n/a",
                    summary = mine.Count == 0
                        ? $"{info.Name} ({info.Code}, basket {info.Basket}) has no positions in the demo book."
                        : $"{info.Name}: total {Inr(r + u)} (realized {Inr(r)}, unrealized {Inr(u)}) across {byAcc.Count} accounts; " +
                          $"{mine.Count(x => x.p.NetQty != 0)} open, {mine.Count(x => x.p.NetQty == 0)} closed positions.",
                    best = byAcc.AsEnumerable().Reverse().Take(3).Select(x => new { clientId = x.a.ClientId, x.a.Name, pnl = Inr(x.v) }),
                    worst = byAcc.Take(3).Select(x => new { clientId = x.a.ClientId, x.a.Name, pnl = Inr(x.v) }),
                };
            }
            var list = pos.GroupBy(x => x.p.Strategy).Select(g => new
            {
                strategy = g.Key, total = g.Sum(x => x.p.Realized + x.p.Unrealized),
                realized = g.Sum(x => x.p.Realized), unrealized = g.Sum(x => x.p.Unrealized),
                open = g.Count(x => x.p.NetQty != 0), accounts = g.Select(x => x.a.ClientId).Distinct().Count(),
            }).OrderByDescending(x => x.total).ToList();
            // the model must never add up rows itself (it only sees `limit` of them): give it the real grand total
            decimal gr = list.Sum(x => x.realized), gu = list.Sum(x => x.unrealized);
            var shown = Math.Min(Math.Clamp(limit, 1, 30), list.Count);
            var accounts = store.Accounts.Count;
            return new
            {
                summary = $"Total strategy P&L across ALL {list.Count} strategies and {accounts} accounts: {Inr(gr + gu)} " +
                          $"(realized {Inr(gr)}, unrealized {Inr(gu)}). Rows below show only the top {shown} by P&L; do not add them up.",
                grandTotal = Inr(gr + gu), grandRealized = Inr(gr), grandUnrealized = Inr(gu), accounts,
                strategiesWithPositions = list.Count, rowsShown = shown,
                rows = list.Take(Math.Clamp(limit, 1, 30)).Select(x => new { x.strategy, total = Inr(x.total), realized = Inr(x.realized), unrealized = Inr(x.unrealized), x.open, x.accounts }),
                worst = list.LastOrDefault() is { } w ? new { w.strategy, total = Inr(w.total) } : null,
            };
        }
    }

    private object BasketSummary(string? basket)
    {
        lock (store.SyncRoot)
        {
            var groups = store.Accounts.GroupBy(a => a.Basket).ToDictionary(g => g.Key, g => g.ToList());
            object Row(string name)
            {
                var accts = groups.GetValueOrDefault(name) ?? [];
                return new
                {
                    basket = name, accounts = accts.Count, openPositions = accts.Sum(a => a.OpenPositions),
                    pnl = Inr(accts.Sum(a => a.StrategyPnl)),
                    strategies = Catalog.Strategies.Where(s => s.Basket == name).Select(s => s.Name),
                };
            }
            if (!string.IsNullOrWhiteSpace(basket))
            {
                var b = Catalog.Baskets.FirstOrDefault(x => x.Name.Equals(basket, StringComparison.OrdinalIgnoreCase))
                        ?? Catalog.Baskets.FirstOrDefault(x => x.Name.Contains(basket, StringComparison.OrdinalIgnoreCase));
                return b is null ? new { found = false, basket, baskets = Catalog.BasketNames } : Row(b.Name);
            }
            return new
            {
                baskets = Catalog.Baskets.Select(b => b.Name).Select(n => (n, a: groups.GetValueOrDefault(n) ?? []))
                    .OrderByDescending(x => x.a.Sum(a => a.StrategyPnl))
                    .Select(x => new { basket = x.n, accounts = x.a.Count, openPositions = x.a.Sum(a => a.OpenPositions), pnl = Inr(x.a.Sum(a => a.StrategyPnl)) }),
            };
        }
    }

    private object Infrastructure()
    {
        var c = store.GetContainers();
        var d = delta.Snapshot().Status;
        var rt = rtPersister.Status(); var mk = marketPersister.Status(); var fp = fyersPoller.Status();
        var problems = c.Containers.Where(x => x.Status != "Running" || x.Health != "Healthy" || !x.Heartbeat)
                                   .Select(x => new { x.Name, x.ClientId, x.Status, x.Health, heartbeat = x.Heartbeat, cpuPct = x.CpuPct });
        return new
        {
            containers = new { c.Total, c.Running, c.Stopped, c.Healthy, c.Unhealthy, noHeartbeat = c.NoHeartbeat, avgCpuPct = Math.Round(c.AvgCpu, 1), avgMemPct = Math.Round(c.AvgMemory, 1), problems },
            redis = new { connected = bus.RedisConnected },
            sqlPersisters = new
            {
                realtime = new { ready = rt.SqlReady, lastFlushUtc = rt.LastFlushUtc, rt.LastError },
                deltaMarket = new { ready = mk.SqlReady, lastFlushUtc = mk.LastFlushUtc, mk.LastError },
            },
            deltaFeed = new { d.Connected, d.Symbol, lastMessageUtc = d.LastMessageAt, d.Reconnects },
            fyersPoller = new { fp.State, fp.LastError },
        };
    }

    private object Rejections(string? broker, string? account, int limit)
    {
        lock (store.SyncRoot)
        {
            IEnumerable<AlertAccount> q = store.AlertAccounts.Where(a => a.Open > 0);
            if (!string.IsNullOrWhiteSpace(broker)) q = q.Where(a => a.Broker.Equals(broker, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(account)) q = q.Where(a => a.AccountId.Contains(account, StringComparison.OrdinalIgnoreCase));
            var list = q.OrderByDescending(a => a.Open).ToList();
            return new
            {
                openRejections = list.Sum(a => a.Open), accounts = list.Count,
                byReason = list.GroupBy(a => a.ReasonCode).ToDictionary(g => g.Key, g => g.Sum(a => a.Open)),
                byBroker = list.GroupBy(a => a.Broker).ToDictionary(g => g.Key, g => g.Sum(a => a.Open)),
                top = list.Take(Math.Clamp(limit, 1, 15)).Select(a => new
                {
                    a.Broker, a.AccountId, a.Symbol, a.ReasonCode, open = a.Open,
                    message = a.Message.Length > 140 ? a.Message[..140] + "…" : a.Message, lastIst = a.LastTime.ToString("dd MMM HH:mm"),
                }),
            };
        }
    }

    private object ExitsAndCleanup(string? account)
    {
        lock (store.SyncRoot)
        {
            HashSet<string>? ids = null;
            if (!string.IsNullOrWhiteSpace(account))
            {
                var m = MatchAccounts(account);
                if (m.Count == 0) return new { found = false, account };
                ids = m.Select(a => a.ClientId).ToHashSet();
            }
            var exits = store.InFlightExits.Where(e => ids is null || ids.Contains(e.ClientId)).OrderByDescending(e => e.Since).ToList();
            var clean = store.CleanupItems.Where(c => ids is null || ids.Contains(c.ClientId)).ToList();
            var now = MockDataStore.NowIst;
            return new
            {
                inFlight = new
                {
                    total = exits.Count, byStage = exits.GroupBy(e => e.Stage).ToDictionary(g => g.Key, g => g.Count()),
                    oldest = exits.OrderBy(e => e.Since).Take(5).Select(e => new { e.ClientId, e.Symbol, e.Qty, e.Stage, e.Attempts, minutesAgo = (int)(now - e.Since).TotalMinutes, e.Reason }),
                },
                cleanup = new
                {
                    total = clean.Count, byIssue = clean.GroupBy(c => c.Issue).ToDictionary(g => g.Key, g => g.Count()),
                    items = clean.Take(8).Select(c => new { c.ClientId, c.Broker, c.Symbol, c.Issue, omsQty = c.OmsQty, brokerQty = c.BrokerQty }),
                },
            };
        }
    }

    private object RecentSignals(string filter, string? symbol, string? strategy, int limit)
    {
        lock (store.SyncRoot)
        {
            IEnumerable<Signal> q = store.Signals;
            q = filter switch
            {
                "attention" => q.Where(s => s.NeedsAttention),
                "open" => q.Where(s => s.Health is "open" or "residual"),
                "exited" => q.Where(s => s.Health == "ok"),
                _ => q,
            };
            if (!string.IsNullOrWhiteSpace(symbol)) q = q.Where(s => s.Symbol.Contains(symbol, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(strategy)) q = q.Where(s => s.StrategyName.Equals(strategy, StringComparison.OrdinalIgnoreCase));
            var list = q.OrderByDescending(s => s.ReachedApiAt).ToList();
            return new
            {
                filter, matching = list.Count,
                signals = list.Take(Math.Clamp(limit, 1, 15)).Select(s => new
                {
                    s.Id, timeIst = s.ReachedApiAt.ToString("dd MMM HH:mm:ss"), s.Symbol, strategy = s.StrategyName, s.Type,
                    status = s.EffectiveStatus, exitStatus = s.EffectiveExitStatus, health = s.Health,
                    filled = $"{s.Filled}/{s.Requested}", exited = s.Exited, residualQty = s.ResidualQty, issues = s.UnresolvedIssues,
                }),
            };
        }
    }

    private async Task<object> IndicesAsync()
    {
        if (fyers.Configured)
        {
            try
            {
                var q = await fyers.Quotes("NSE:NIFTY50-INDEX,NSE:NIFTYBANK-INDEX,BSE:SENSEX-INDEX");
                return new
                {
                    source = "Fyers (real market data)",
                    indices = q.D.Where(x => x.V is not null).Select(x => new
                    {
                        symbol = x.N, level = x.V!.Lp.ToString("N2", IN), change = x.V.Ch, changePct = x.V.Chp,
                        high = x.V.High, low = x.V.Low, prevClose = x.V.PrevClose,
                    }),
                    note = "Index levels are points, not rupees.",
                };
            }
            catch (FyersException) { /* fall through to the simulated value */ }
        }
        var sim = store.GetSimStatus(0);
        return new
        {
            source = "SIMULATED (Fyers not connected): only a demo NIFTY level is available",
            indices = new[] { new { symbol = "NIFTY (simulated)", level = sim.Spot.ToString("N2", IN), changePct = sim.SpotChangePct } },
            note = "Log in to Fyers on Broker Live for real NIFTY / BANK NIFTY / SENSEX.",
        };
    }

    private async Task<object> BtcChangeAsync(double hours)
    {
        hours = Math.Clamp(hours, 0.25, 168);
        try
        {
            var candles = await marketDb.GetCandlesAsync(delta.Symbol, 60, DateTime.UtcNow.AddHours(-hours), CancellationToken.None);
            if (candles.Count == 0) return new { found = false, hours, note = "No stored BTC candles in that window yet." };
            var open = candles[0].Open; var last = candles[^1].Close;
            var hi = candles.Max(c => c.High); var lo = candles.Min(c => c.Low);
            string Usd(decimal v) => "$" + v.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
            var covered = TimeSpan.FromSeconds(Math.Max(60, candles[^1].Time - candles[0].Time + 60));
            return new
            {
                currency = "USD",
                summary = $"BTCUSD over the last {(covered.TotalHours < hours - 0.2 ? $"{covered.TotalHours:0.#}h (data available)" : $"{hours:0.#}h")}: " +
                          $"{Usd(open)} → {Usd(last)} ({(last - open) / open * 100:+0.00;-0.00}%), high {Usd(hi)}, low {Usd(lo)}.",
                candles = candles.Count, volumeContracts = candles.Sum(c => c.Volume),
            };
        }
        catch (Exception ex) { return new { error = "BTC history (SQL) unavailable: " + ex.Message }; }
    }

    private object TodayActivity()
    {
        var a = store.GetTodayActivity();
        var f = a.Funnel;
        return new
        {
            summary = $"Today: {f.Signals} signals, {f.Orders} account orders, {f.Filled} filled ({(f.Orders == 0 ? 0 : f.Filled * 100.0 / f.Orders):0.#}% fill rate), {f.Rejected} rejected, {f.Working} working.",
            signalsPerHourIst = a.Hours.Where(h => h.Signals > 0).Select(h => new { hour = $"{h.Hour:00}:00", h.Signals }),
            busiestHour = a.Hours.MaxBy(h => h.Signals) is { } b ? $"{b.Hour:00}:00 ({b.Signals} signals)" : null,
        };
    }

    private object Subscriptions(int days)
    {
        lock (store.SyncRoot)
        {
            var list = store.ExpiringSubscriptions(Math.Clamp(days, 1, 90));   // same data as Basket Subscriptions
            return new
            {
                days, count = list.Count,
                subscriptions = list.Take(15).Select(s => new { s.Name, s.Broker, s.AccountId, s.Plan, endsOn = s.EndsAt.ToString("dd MMM yyyy"), s.DaysLeft }),
            };
        }
    }

    // ==================================================================== top-5 tools (signal accounts, exposure, hedges, Fyers orders, help)

    private object SignalAccounts(int? id, string? symbol, string only)
    {
        lock (store.SyncRoot)
        {
            var s = id is { } i ? store.Signals.FirstOrDefault(x => x.Id == i)
                  : !string.IsNullOrWhiteSpace(symbol) ? store.Signals.Where(x => x.Symbol.Contains(symbol.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)).MaxBy(x => x.ReachedApiAt)
                  : null;
            if (s is null) return new { found = false, id, symbol, hint = "Give a signal id (e.g. 45402) or a symbol." };

            var rows = s.Accounts.Select(a => new
            {
                clientId = a.ClientId, a.Broker, brokerAccount = a.BrokerAccount, status = a.OrderStatus,
                requested = a.Req, filled = a.Fill, rejected = a.Rejected, exited = a.Exit, stillOpen = a.NetOpen,
                reason = a.RejectReason ?? a.PartialReason, hedge = a.Hedge?.Summary,
                problem = a.OrderStatus is "REJECTED" or "FAILED" or "PARTIAL" || (s.ExitSent && a.NetOpen > 0),
            }).ToList();
            var bad = rows.Where(x => x.problem).ToList();
            var reasons = bad.Where(x => x.reason is not null).GroupBy(x => x.reason!).Select(g => $"{g.Count()}× {g.Key}");
            return new
            {
                found = true, s.Id, s.Symbol, strategy = s.StrategyName, timeIst = s.ReachedApiAt.ToString("dd MMM HH:mm:ss"),
                summary = $"Signal #{s.Id} {s.Symbol}: {rows.Count} accounts — {rows.Count(x => x.status == "COMPLETED" && !x.problem)} completed, " +
                          $"{rows.Count(x => x.status == "REJECTED")} rejected, {rows.Count(x => x.status == "PARTIAL")} partial" +
                          (s.ExitSent ? $", {rows.Count(x => x.stillOpen > 0)} still holding qty after exit ({rows.Sum(x => x.stillOpen)} qty)" : "") +
                          (bad.Count > 0 ? $". Reasons: {string.Join("; ", reasons)}" : ". No problems."),
                accounts = (only == "all" ? rows : bad).Take(25),
            };
        }
    }

    /// <summary>"NIFTY 17 NOV 25300 CE", "25300 CE", "NIFTY17NOV26C25300" → predicate on our symbol format (…C25300 / …P25300).</summary>
    private static Func<string, bool> SymbolFilter(string q)
    {
        var compact = q.Replace(" ", "").ToUpperInvariant();
        if (compact.Length >= 10 && char.IsLetter(compact[0]) && char.IsDigit(compact[^1])) return sym => sym.Contains(compact, StringComparison.OrdinalIgnoreCase);
        var strike = System.Text.RegularExpressions.Regex.Match(q, @"\b(\d{4,6})\b").Groups[1].Value;
        var type = System.Text.RegularExpressions.Regex.IsMatch(q, @"\b(CE|CALL|C)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? 'C'
                 : System.Text.RegularExpressions.Regex.IsMatch(q, @"\b(PE|PUT|P)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? 'P' : '\0';
        var under = System.Text.RegularExpressions.Regex.Match(q, @"\b(BANKNIFTY|NIFTY|SENSEX)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Value.ToUpperInvariant();
        // two symbol formats: NIFTY17NOV26C25300 (type then strike) and SENSEX26N0578900PE / BANKNIFTY26NOV52000PE (strike then CE/PE)
        static (string strike, char type) Parse(string sym)
        {
            var m = System.Text.RegularExpressions.Regex.Match(sym, @"(\d{4,6})(CE|PE)$");
            if (m.Success) return (m.Groups[1].Value, m.Groups[2].Value[0]);
            m = System.Text.RegularExpressions.Regex.Match(sym, @"([CP])(\d{4,6})$");
            return m.Success ? (m.Groups[2].Value, m.Groups[1].Value[0]) : ("", '\0');
        }
        return sym =>
        {
            var (k, t) = Parse(sym);
            return (strike.Length == 0 || k.EndsWith(strike)) && (type == '\0' || t == type) &&
                   (under.Length == 0 || sym.StartsWith(under, StringComparison.OrdinalIgnoreCase) && !(under == "NIFTY" && sym.StartsWith("BANKNIFTY")));
        };
    }

    private object SymbolExposure(string? symbol, int limit)
    {
        lock (store.SyncRoot)
        {
            var open = store.Accounts.SelectMany(a => a.Positions.Where(p => p.NetQty != 0).Select(p => (a, p)));
            if (!string.IsNullOrWhiteSpace(symbol)) { var f = SymbolFilter(symbol); open = open.Where(x => f(x.p.Symbol)); }
            var groups = open.GroupBy(x => x.p.Symbol).Select(g =>
            {
                var net = g.Sum(x => x.p.NetQty);
                var gross = g.Sum(x => Math.Abs(x.p.NetQty));
                var avg = gross == 0 ? 0 : g.Sum(x => Math.Abs(x.p.NetQty) * (x.p.NetQty > 0 ? x.p.AvgBuy : x.p.AvgSell)) / gross;
                return new
                {
                    symbol = g.Key, display = g.First().p.DisplaySymbol, netQty = net, direction = net < 0 ? "net SHORT" : net > 0 ? "net LONG" : "flat",
                    grossQty = gross, accounts = g.Select(x => x.a.ClientId).Distinct().Count(),
                    avgPrice = Math.Round(avg, 2), ltp = g.First().p.Ltp, unrealized = g.Sum(x => x.p.Unrealized),
                    perAccount = g.OrderBy(x => x.p.Unrealized).Take(6).Select(x => new { clientId = x.a.ClientId, netQty = x.p.NetQty, unrealized = Inr(x.p.Unrealized) }),
                };
            }).OrderByDescending(x => x.grossQty).ToList();
            if (groups.Count == 0) return new { found = false, symbol, hint = "No open positions match. Try '25300 CE' or a full symbol like NIFTY17NOV26C25300." };
            return new
            {
                found = true, query = symbol ?? "(largest exposures)", instruments = groups.Count,
                totalUnrealized = Inr(groups.Sum(x => x.unrealized)),
                rows = groups.Take(Math.Clamp(limit, 1, 20)).Select(x => new
                {
                    x.symbol, x.display, x.netQty, x.direction, x.grossQty, x.accounts, x.avgPrice, x.ltp, unrealized = Inr(x.unrealized),
                    perAccount = groups.Count == 1 ? x.perAccount : null,
                }),
            };
        }
    }

    private object Unhedged(string? account)
    {
        lock (store.SyncRoot)
        {
            HashSet<string>? ids = null;
            if (!string.IsNullOrWhiteSpace(account))
            {
                var m = MatchAccounts(account);
                if (m.Count == 0) return new { found = false, account };
                ids = m.Select(a => a.ClientId).ToHashSet();
            }
            var all = store.Hedges.Where(h => ids is null || ids.Contains(h.ClientId)).ToList();
            var bad = all.Where(h => h.Status != "PROTECTED").OrderBy(h => h.Status == "PARTIAL").ToList();
            return new
            {
                summary = $"{bad.Count} of {all.Count} hedged short positions are not fully protected: " +
                          $"{bad.Count(h => h.Status == "NAKED")} NAKED (no hedge legs), {bad.Count(h => h.Status == "PARTIAL")} PARTIAL (one leg missing).",
                positions = bad.Take(15).Select(h => new
                {
                    h.ClientId, h.Broker, shortSymbol = h.MainSymbol, shortQty = h.MainQty, h.Status,
                    ceHedge = $"{h.CeLeg} × {h.CeQty}", peHedge = $"{h.PeLeg} × {h.PeQty}",
                    missingQty = Math.Max(0, h.MainQty - h.CeQty) + Math.Max(0, h.MainQty - h.PeQty),
                }),
            };
        }
    }

    private async Task<object> FyersOrdersAsync(string status, int limit)
    {
        if (!fyers.Configured) return new { connected = false, error = "Fyers is not configured. Log in on Broker Live → Login with Fyers." };
        try
        {
            var book = (await fyers.Orders()).OrderBook;
            var rows = book.Select(o => new
            {
                orderId = o.Id, o.Symbol, side = Controllers.FyersController.Side(o.Side), type = Controllers.FyersController.OrderType(o.Type),
                status = Controllers.FyersController.OrderStatus(o.Status), qty = o.Qty, filled = o.FilledQty,
                price = o.TradedPrice != 0 ? o.TradedPrice : o.LimitPrice, message = o.Message, time = o.OrderDateTime,
            }).ToList();
            var filtered = status switch
            {
                "rejected" => rows.Where(o => o.status == "REJECTED"),
                "pending" => rows.Where(o => o.status is "PENDING" or "TRANSIT"),
                "completed" => rows.Where(o => o.status == "COMPLETED"),
                "cancelled" => rows.Where(o => o.status == "CANCELLED"),
                _ => rows,
            };
            var counts = rows.GroupBy(o => o.status).ToDictionary(g => g.Key, g => g.Count());
            return new
            {
                connected = true,
                summary = rows.Count == 0 ? "No Fyers orders today."
                    : $"{rows.Count} Fyers orders today: " + string.Join(", ", counts.Select(kv => $"{kv.Value} {kv.Key.ToLowerInvariant()}")) + ".",
                rejectReasons = rows.Where(o => o.status == "REJECTED" && !string.IsNullOrWhiteSpace(o.message))
                                    .GroupBy(o => o.message!).Select(g => $"{g.Count()}× {g.Key}"),
                orders = filtered.Take(Math.Clamp(limit, 1, 30)),
            };
        }
        catch (FyersException ex)
        {
            return new { connected = false, error = ex.Status == 401 ? "Fyers session expired. Log in again on Broker Live (Login with Fyers / Renew with PIN)." : ex.Message };
        }
    }

    // ---- help: built-in glossary + best-matching sections from docs/*.md

    private static readonly Dictionary<string, string> Glossary = new(StringComparer.OrdinalIgnoreCase)
    {
        ["exit partial"] = "The exit was sent but at least one account still holds quantity at the broker (e.g. a chunk closed with 0 fill). The signal is never shown as completed while qty is open; fix it with 'Exit remaining' or 'Retry chunk' in the signal popup.",
        ["residual"] = "Quantity still open after an exit was sent (same as 'exit partial'). Shown as '⚠ Exit partial · N residual'.",
        ["needs attention"] = "Signals with residual qty, rejected/failed/partial accounts or contradictory data (e.g. COMPLETED with 0 fill). Recovered hedge retries are not counted.",
        ["chunk"] = "Large exit orders are split into chunks (e.g. 3 orders of 40). The chunk table shows each chunk's fill, broker vs OMS status and why it didn't fill.",
        ["dlq"] = "Dead-letter queue: messages the relay could not deliver after retries. 'DLQ %' in the Relay pill is the share of messages that ended there.",
        ["dead letter"] = "A message the relay gave up on after retries (see DLQ). The red banner shows dead letters and failed executions in the overview range.",
        ["in-flight exit"] = "An exit order on its way to the broker: QUEUED → SENT → PARTIAL / RETRYING → done.",
        ["cleanup"] = "Positions where OMS and broker disagree: qty mismatch, orphan position at broker, stale with no LTP, or a hedge leg left open after the main leg closed.",
        ["hedge"] = "Protective far-OTM CE/PE legs bought for a short option. PROTECTED = both legs, PARTIAL = one leg missing, NAKED = no legs.",
        ["risk"] = "A position losing more than 20% is flagged RISK; in the demo simulator one losing more than 60% is auto-closed (max-loss rule).",
        ["relay"] = "The service that dispatches signals to client containers. The pill shows Live/Down, execution % and DLQ %.",
        ["scenario"] = "Demo simulator market mode: Calm, Normal, Volatile, Rally, Crash. Change it in the 'Demo live' pill.",
        // how-tos (UI steps, not API calls)
        ["renew"] = "Open Broker Live (sidebar). If today's session expired but the saved refresh token is still valid (~15 days), a box 'Renew today's session with your PIN' appears: type your Fyers PIN and press Renew. If that box isn't there, click 'Login with Fyers', log in in the pop-up, and it closes by itself when connected.",
        ["login"] = "Fyers: Broker Live → 'Login with Fyers' → log in in the pop-up window → it closes and the page shows LIVE. If your Fyers app still redirects to Fyers' own page, paste that page's URL into the box and press 'Generate session'.",
        ["fyers session"] = "Fyers sessions expire daily. Broker Live → 'Renew with PIN' (if shown) or 'Login with Fyers'. The session is saved and reused after API restarts while valid.",
        ["exit remaining"] = "Live Signals → click the signal → in the yellow 'accounts need attention' box press 'Exit remaining' next to the account (or open the account row and use 'Retry chunk' for one chunk).",
        ["force exit"] = "Live Signals → open the signal → 'Force Exit' (top right) squares off all open accounts for that signal. Positions & Exits → account → a position's exit button closes one position.",
        ["filter"] = "Live Signals / Performance: set filters (date, basket, strategy multi-select with search, client) and press Apply; 'Needs attention' quick filter shows only problems. Save/Load preset stores your filters.",
        ["simulator"] = "The 'Demo live' pill at the top of the console: pick a scenario (Calm/Normal/Volatile/Rally/Crash) or pause/resume; click the pill for recent simulated events.",
        ["assistant"] = "This chat: ask in plain English about live data; it uses read-only tools and shows which ones under each answer. It cannot place or exit orders.",
    };

    private string? _docsDir;
    private string DocsDir => _docsDir ??= new[]
    {
        Path.Combine(env.ContentRootPath, "..", "..", "docs"), Path.Combine(env.ContentRootPath, "docs"),
    }.Select(Path.GetFullPath).FirstOrDefault(Directory.Exists) ?? "";

    private object Help(string topic)
    {
        var q = topic.ToLowerInvariant();
        var glossary = Glossary.Where(kv => q.Contains(kv.Key)).Select(kv => new { term = kv.Key, meaning = kv.Value }).ToList();

        var words = System.Text.RegularExpressions.Regex.Matches(q, @"[a-z0-9]{3,}").Select(m => m.Value)
            .Where(w => w is not ("what" or "does" or "how" or "the" or "and" or "mean" or "means" or "can" or "where" or "this" or "that" or "with" or "for" or "you")).Distinct().ToList();
        var sections = new List<(string file, string title, string body, int score)>();
        if (DocsDir.Length > 0 && words.Count > 0)
            foreach (var file in Directory.GetFiles(DocsDir, "*.md").Where(f => !f.EndsWith("CHANGELOG.md")
                         && (!f.EndsWith("10-api-reference.md") || q.Contains("api") || q.Contains("endpoint"))))
            {
                var text = File.ReadAllText(file);
                foreach (var part in System.Text.RegularExpressions.Regex.Split(text, @"(?m)^(?=#{2,3} )"))
                {
                    var title = part.Split('\n')[0].Trim('#', ' ', '\r');
                    var lower = part.ToLowerInvariant();
                    var score = words.Sum(w => (lower.Contains(w) ? 1 : 0) + (title.ToLowerInvariant().Contains(w) ? 2 : 0));
                    if (score > 0) sections.Add((Path.GetFileName(file), title, part.Trim(), score));
                }
            }
        var top = sections.OrderByDescending(x => x.score).Take(2)
            .Select(x => new { doc = $"docs/{x.file}", section = x.title, text = x.body.Length > 1400 ? x.body[..1400] + "…" : x.body });
        return new
        {
            topic, glossary, docs = top,
            instruction = "Answer with what the user should click/do in the console UI (pages, buttons). Mention API endpoints only if the user asked about the API.",
            note = glossary.Count == 0 && !top.Any() ? "Nothing in the docs matches; answer from general knowledge only if it is clearly about trading terms, and say so." : null,
        };
    }
}
