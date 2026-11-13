namespace Jarvis.Api.Services;

/// <summary>One tradable instrument from Fyers' public symbol master.</summary>
/// <param name="Ticker">Fyers symbol, e.g. NSE:RELIANCE-EQ (what data/quotes and data/history take)</param>
/// <param name="Short">exchange symbol without series, e.g. RELIANCE</param>
public record FyersInstrument(string Ticker, string Name, string Short, string Exchange, string Series, string? Isin);

/// <summary>
/// Name → Fyers ticker lookup ("Reliance" → NSE:RELIANCE-EQ) from Fyers' PUBLIC symbol master
/// (https://public.fyers.in/sym_details/NSE_CM.csv and BSE_CM.csv — no login, no account data).
/// Downloaded on first use and then once a day; a copy is kept in %LOCALAPPDATA%\JarvisDemo\symbols so a restart
/// (or no internet) still works. Only cash-market equities (EQ / BE / BSE A,B,T,X…) and indices are kept.
/// </summary>
public class FyersSymbols(IHttpClientFactory httpFactory, ILogger<FyersSymbols> log)
{
    private static readonly string[] Files = ["NSE_CM", "BSE_CM"];
    private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JarvisDemo", "symbols");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<FyersInstrument> _all = [];
    private DateTime _loadedUtc = DateTime.MinValue;

    /// <summary>common names people type for indices</summary>
    private static readonly Dictionary<string, string> IndexAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NIFTY"] = "NSE:NIFTY50-INDEX", ["NIFTY50"] = "NSE:NIFTY50-INDEX", ["NIFTY 50"] = "NSE:NIFTY50-INDEX",
        ["BANKNIFTY"] = "NSE:NIFTYBANK-INDEX", ["BANK NIFTY"] = "NSE:NIFTYBANK-INDEX", ["NIFTYBANK"] = "NSE:NIFTYBANK-INDEX",
        ["FINNIFTY"] = "NSE:FINNIFTY-INDEX", ["SENSEX"] = "BSE:SENSEX-INDEX", ["INDIA VIX"] = "NSE:INDIAVIX-INDEX", ["VIX"] = "NSE:INDIAVIX-INDEX",
        // everyday names that don't match the listed name / symbol
        ["SBI"] = "NSE:SBIN-EQ", ["HUL"] = "NSE:HINDUNILVR-EQ", ["AIRTEL"] = "NSE:BHARTIARTL-EQ", ["BHARTI AIRTEL"] = "NSE:BHARTIARTL-EQ",
        ["KOTAK"] = "NSE:KOTAKBANK-EQ", ["AXIS"] = "NSE:AXISBANK-EQ", ["HCL"] = "NSE:HCLTECH-EQ", ["ZOMATO"] = "NSE:ETERNAL-EQ",
        ["PAYTM"] = "NSE:PAYTM-EQ", ["L&T"] = "NSE:LT-EQ", ["LNT"] = "NSE:LT-EQ", ["MARUTI"] = "NSE:MARUTI-EQ", ["BAJAJ FINSERV"] = "NSE:BAJAJFINSV-EQ",
        ["HDFC LIFE"] = "NSE:HDFCLIFE-EQ", ["SBI LIFE"] = "NSE:SBILIFE-EQ", ["DMART"] = "NSE:DMART-EQ", ["NYKAA"] = "NSE:NYKAA-EQ",
    };

    public int Count => _all.Count;
    public DateTime LoadedUtc => _loadedUtc;

    public async Task<IReadOnlyList<FyersInstrument>> AllAsync(CancellationToken ct = default)
    {
        if (_all.Count > 0 && DateTime.UtcNow - _loadedUtc < TimeSpan.FromHours(24)) return _all;
        await _gate.WaitAsync(ct);
        try
        {
            if (_all.Count > 0 && DateTime.UtcNow - _loadedUtc < TimeSpan.FromHours(24)) return _all;
            var list = new List<FyersInstrument>();
            foreach (var f in Files) list.AddRange(Parse(await ReadAsync(f, ct)));
            if (list.Count > 0) { _all = list; _loadedUtc = DateTime.UtcNow; log.LogInformation("Fyers symbol master loaded: {Count} instruments", list.Count); }
            return _all;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Download (or fall back to the saved copy).</summary>
    private async Task<string> ReadAsync(string name, CancellationToken ct)
    {
        var file = Path.Combine(Dir, name + ".csv");
        try
        {
            if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromHours(20)) return await File.ReadAllTextAsync(file, ct);
            var http = httpFactory.CreateClient("fyers-public");
            var csv = await http.GetStringAsync($"sym_details/{name}.csv", ct);
            Directory.CreateDirectory(Dir);
            await File.WriteAllTextAsync(file, csv, ct);
            return csv;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            log.LogWarning("Fyers symbol master {Name} download failed ({Message}); using the saved copy if any", name, ex.Message);
            return File.Exists(file) ? await File.ReadAllTextAsync(file, ct) : "";
        }
    }

    private static IEnumerable<FyersInstrument> Parse(string csv)
    {
        // columns: 0 fytoken, 1 name, 2 instrument type (0 equity, 10 index), …, 5 ISIN, …, 9 ticker (NSE:RELIANCE-EQ), …, 13 short symbol
        foreach (var line in csv.Split('\n'))
        {
            var c = line.TrimEnd('\r').Split(',');
            if (c.Length < 14 || !c[9].Contains(':')) continue;
            var ticker = c[9];
            var exch = ticker[..ticker.IndexOf(':')];
            var dash = ticker.LastIndexOf('-');
            var series = dash > 0 ? ticker[(dash + 1)..] : "";
            var isIndex = series == "INDEX";
            var equity = exch == "NSE" ? series is "EQ" or "BE" : series.Length <= 2 && c[2] == "0";   // BSE: A, B, T, X, XT, M…
            if (!isIndex && !equity) continue;
            yield return new FyersInstrument(ticker, c[1].Trim(), isIndex ? ticker[(ticker.IndexOf(':') + 1)..dash] : c[13].Trim(), exch, series,
                string.IsNullOrWhiteSpace(c[5]) ? null : c[5]);
        }
    }

    /// <summary>Best matches for a name / symbol / ticker, best first (NSE equity preferred over BSE).</summary>
    public async Task<List<FyersInstrument>> SearchAsync(string query, int limit = 8, CancellationToken ct = default)
    {
        var all = await AllAsync(ct);
        var q = (query ?? "").Trim();
        if (q.Length == 0 || all.Count == 0) return [];
        if (IndexAliases.TryGetValue(q, out var alias)) return all.Where(i => i.Ticker == alias).Take(1).ToList();
        if (q.Contains(':')) { var hit = all.FirstOrDefault(i => i.Ticker.Equals(q, StringComparison.OrdinalIgnoreCase)); if (hit is not null) return [hit]; }

        var Q = q.ToUpperInvariant();
        var qNoSpace = Q.Replace(" ", "").Replace(".", "").Replace("&", "");
        var words = Q.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w is not ("LTD" or "LIMITED" or "THE" or "OF" or "SHARE" or "SHARES" or "STOCK" or "PRICE")).ToArray();
        int Score(FyersInstrument i)
        {
            var name = i.Name.ToUpperInvariant();
            int s;
            if (i.Short == Q || i.Short == qNoSpace) s = 0;                                  // RELIANCE, HDFCBANK
            else if (name.StartsWith(Q + " ") || name == Q) s = 1;                            // "TATA MOTORS" → TATA MOTORS LIMITED
            else if (words.Length > 0 && words.All(w => name.Contains(w))) s = name.StartsWith(words[0]) ? 2 : 3;
            else if (i.Short.StartsWith(qNoSpace)) s = 4;
            else return int.MaxValue;
            // prefer NSE, then the main series, then shorter names (the "plain" company over subsidiaries)
            return s * 1000 + (i.Exchange == "NSE" ? 0 : 300) + (i.Series is "EQ" or "INDEX" or "A" ? 0 : 100) + Math.Min(name.Length, 99);
        }
        return all.Select(i => (i, s: Score(i))).Where(x => x.s != int.MaxValue).OrderBy(x => x.s).Select(x => x.i).Take(Math.Clamp(limit, 1, 25)).ToList();
    }

    public async Task<FyersInstrument?> ResolveAsync(string query, CancellationToken ct = default) => (await SearchAsync(query, 1, ct)).FirstOrDefault();
}
