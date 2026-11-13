using Jarvis.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jarvis.Api.Controllers;

/// <summary>
/// Market Charts + stock quotes. Any NSE/BSE stock or index: candles from Fyers' read-only historical-data API and quotes
/// from data/quotes, using the logged-in Fyers session. Names are resolved with the public symbol master (FyersSymbols).
/// BTCUSD uses /api/delta/candles instead.
/// </summary>
[ApiController]
[Route("api/market")]
public class MarketChartsController(FyersClient fyers, FyersSymbols symbols, ILogger<MarketChartsController> log) : ControllerBase
{
    public record ChartSymbol(string Key, string Name, string FyersSymbol);
    /// <summary>The fixed tabs on the page (short keys kept for old links: ?symbol=NIFTY50).</summary>
    public static readonly ChartSymbol[] Indices =
    [
        new("NIFTY50", "NIFTY 50", "NSE:NIFTY50-INDEX"),
        new("BANKNIFTY", "BANK NIFTY", "NSE:NIFTYBANK-INDEX"),
        new("SENSEX", "SENSEX", "BSE:SENSEX-INDEX"),
    ];
    /// <summary>chart resolution → Fyers resolution, seconds per candle, days of history to request</summary>
    private static readonly Dictionary<string, (string Fyers, int Secs, int Days)> Res = new()
    {
        ["1m"] = ("1", 60, 4), ["5m"] = ("5", 300, 12), ["15m"] = ("15", 900, 30), ["1h"] = ("60", 3600, 90), ["1d"] = ("D", 86400, 365),
    };

    public static ChartSymbol? Find(string? key) =>
        Indices.FirstOrDefault(i => i.Key.Equals((key ?? "").Replace(" ", "").Replace("-", ""), StringComparison.OrdinalIgnoreCase)
                                 || i.FyersSymbol.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>NIFTY50 / NSE:RELIANCE-EQ / "Reliance" → (ticker, display name)</summary>
    private async Task<(string Ticker, string Name)?> ResolveAsync(string? symbol)
    {
        if (Find(symbol) is { } idx) return (idx.FyersSymbol, idx.Name);
        var hit = await symbols.ResolveAsync(symbol ?? "");
        return hit is null ? null : (hit.Ticker, hit.Name);
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int limit = 8)
    {
        var hits = await symbols.SearchAsync(q, limit);
        return Ok(new { query = q, count = symbols.Count, results = hits.Select(h => new { ticker = h.Ticker, h.Name, symbol = h.Short, h.Exchange, h.Series }) });
    }

    [HttpGet("candles")]
    public async Task<IActionResult> Candles([FromQuery] string symbol = "NIFTY50", [FromQuery] string resolution = "5m", [FromQuery] int count = 300)
    {
        if (await ResolveAsync(symbol) is not { } sym) return NotFound(new { error = $"No NSE/BSE stock or index matches '{symbol}'." });
        if (!Res.TryGetValue(resolution, out var r)) return BadRequest(new { error = $"resolution must be one of {string.Join(", ", Res.Keys)}" });
        if (!fyers.Configured) return StatusCode(503, new { error = "Fyers isn't connected. Log in on Broker Live (Fyers) to load NSE/BSE charts.", needsLogin = true });
        var to = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var from = to - r.Days * 86400L;
        try
        {
            var h = await fyers.History(sym.Ticker, r.Fyers, from, to);
            // Fyers can return candles out of order (and occasionally the same bucket twice): sort + de-duplicate,
            // the chart library requires strictly ascending times
            var candles = h.Candles.Where(c => c.Count >= 5).GroupBy(c => (long)c[0]).Select(g => g.Last()).OrderBy(c => c[0])
                .TakeLast(Math.Clamp(count, 10, 2000))
                .Select(c => new { time = (long)c[0], open = c[1], high = c[2], low = c[3], close = c[4], volume = c.Count > 5 ? c[5] : 0 }).ToList();
            return Ok(new { symbol = sym.Ticker, name = sym.Name, fyersSymbol = sym.Ticker, isIndex = sym.Ticker.EndsWith("-INDEX"),
                resolution, intervalSeconds = r.Secs, source = "fyers", candles });
        }
        catch (FyersException ex)
        {
            log.LogInformation("Chart candles for {Symbol} failed: {Message}", sym.Ticker, ex.Message);
            return StatusCode(ex.Status is 401 or 503 ? 503 : 502, new
            {
                error = ex.Status is 401 or 503 ? "Your Fyers session has expired. Renew it on Broker Live (Fyers) to load NSE/BSE charts." : $"Fyers: {ex.Message}",
                needsLogin = ex.Status is 401 or 503,
            });
        }
    }

    [HttpGet("quote")]
    public async Task<IActionResult> Quote([FromQuery] string symbol = "NIFTY50")
    {
        if (await ResolveAsync(symbol) is not { } sym) return NotFound(new { error = $"No NSE/BSE stock or index matches '{symbol}'." });
        var all = await QuotesFor([sym.Ticker]);
        if (all.Error is { } e) return StatusCode(e.Status, new { error = e.Message, needsLogin = e.Status == 503 });
        var q = all.Quotes.FirstOrDefault();
        return q is null ? StatusCode(502, new { error = "No quote returned." }) : Ok(q with { Name = sym.Name });
    }

    /// <summary>Several quotes in one Fyers call: ?symbols=RELIANCE,TCS,NSE:INFY-EQ (max 50).</summary>
    [HttpGet("quotes")]
    public async Task<IActionResult> Quotes([FromQuery] string symbols)
    {
        var tickers = new List<string>();
        foreach (var s in (symbols ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(50))
            if (await ResolveAsync(s) is { } r) tickers.Add(r.Ticker);
        if (tickers.Count == 0) return NotFound(new { error = "No symbols matched." });
        var all = await QuotesFor(tickers);
        return all.Error is { } e ? StatusCode(e.Status, new { error = e.Message, needsLogin = e.Status == 503 }) : Ok(all.Quotes);
    }

    public record StockQuote(string Symbol, string? Name, decimal Last, decimal Change, decimal ChangePct, decimal Open, decimal High, decimal Low,
        decimal PrevClose, long Volume, long Time);

    private async Task<(List<StockQuote> Quotes, (int Status, string Message)? Error)> QuotesFor(List<string> tickers)
    {
        if (!fyers.Configured) return ([], (503, "Fyers isn't connected."));
        try
        {
            var res = await fyers.Quotes(string.Join(',', tickers.Distinct()));
            var list = res.D.Where(d => d.V is not null).Select(d => new StockQuote(d.N ?? "", d.V!.ShortName, d.V.Lp, d.V.Ch, d.V.Chp, d.V.Open, d.V.High, d.V.Low,
                d.V.PrevClose, d.V.Volume, DateTimeOffset.UtcNow.ToUnixTimeSeconds())).ToList();
            return (list, null);
        }
        catch (FyersException ex) { return ([], (ex.Status is 401 or 503 ? 503 : 502, ex.Message)); }
    }
}
