using System.Text.Json;
using Jarvis.Api.Data;
using Jarvis.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jarvis.Api.Controllers;

/// <summary>Public Delta Exchange market data (no login). Live updates come over SignalR at /hubs/market.</summary>
[ApiController]
[Route("api/delta")]
public class DeltaController(DeltaMarketFeed feed, MarketDb db, IHttpClientFactory httpFactory, ILogger<DeltaController> log) : ControllerBase
{
    private static readonly Dictionary<string, int> ResolutionSeconds = new()
    {
        ["1m"] = 60, ["5m"] = 300, ["15m"] = 900, ["1h"] = 3600, ["4h"] = 14400, ["1d"] = 86400,
    };

    [HttpGet("snapshot")]
    public DeltaSnapshot Snapshot() => feed.Snapshot();

    /// <summary>
    /// Chart candles. Buckets from the moment we started recording come from SQL Server (built from stored trades);
    /// anything older — or missing in SQL — is filled from Delta's public REST history.
    /// </summary>
    [HttpGet("candles")]
    public async Task<IActionResult> Candles([FromQuery] string resolution = "5m", [FromQuery] int count = 300, CancellationToken ct = default)
    {
        if (!ResolutionSeconds.TryGetValue(resolution, out var secs)) return BadRequest($"resolution must be one of {string.Join(", ", ResolutionSeconds.Keys)}");
        count = Math.Clamp(count, 10, 1500);
        var end = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var start = end - (long)secs * count;

        // 1) SQL Server (our stored data)
        var stored = new Dictionary<long, CandleRow>();
        string? sqlError = null;
        long firstFullBucket = long.MaxValue;
        try
        {
            var first = await db.FirstTradeUtcAsync(feed.Symbol, ct);
            if (first is { } f)
            {
                var firstSec = new DateTimeOffset(f).ToUnixTimeSeconds();
                // the bucket we started recording in is partial → let Delta REST provide it
                firstFullBucket = (firstSec + secs - 1) / secs * secs;
                foreach (var c in await db.GetCandlesAsync(feed.Symbol, secs, DateTimeOffset.FromUnixTimeSeconds(start).UtcDateTime, ct))
                    if (c.Time >= firstFullBucket) stored[c.Time] = c;
            }
        }
        catch (Exception ex) { sqlError = ex.Message; log.LogDebug("SQL candles unavailable: {Message}", ex.Message); }

        // 2) Delta REST history to fill everything else
        var merged = new SortedDictionary<long, object>();
        string? restError = null;
        try
        {
            var http = httpFactory.CreateClient("delta");
            using var res = await http.GetAsync($"v2/history/candles?resolution={resolution}&symbol={feed.Symbol}&start={start}&end={end}", ct);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStreamAsync(ct));
            if (res.IsSuccessStatusCode && doc.RootElement.TryGetProperty("result", out var arr))
                foreach (var c in arr.EnumerateArray())
                {
                    var t = c.GetProperty("time").GetInt64();
                    if (stored.ContainsKey(t)) continue;
                    merged[t] = new
                    {
                        time = t, open = c.GetProperty("open").GetDecimal(), high = c.GetProperty("high").GetDecimal(),
                        low = c.GetProperty("low").GetDecimal(), close = c.GetProperty("close").GetDecimal(),
                        volume = c.TryGetProperty("volume", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0,
                        source = "delta",
                    };
                }
            else restError = $"Delta returned {(int)res.StatusCode}";
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException) { restError = ex.Message; }

        foreach (var c in stored.Values)
            merged[c.Time] = new { time = c.Time, open = c.Open, high = c.High, low = c.Low, close = c.Close, volume = c.Volume, source = "sql" };

        if (merged.Count == 0) return StatusCode(502, new { error = restError ?? sqlError ?? "no candle data" });

        return Ok(new
        {
            symbol = feed.Symbol, resolution, intervalSeconds = secs,
            fromSql = stored.Count, fromDelta = merged.Count - stored.Count,
            recordingSinceUtc = firstFullBucket == long.MaxValue ? (DateTime?)null : DateTimeOffset.FromUnixTimeSeconds(firstFullBucket).UtcDateTime,
            candles = merged.Values,
        });
    }
}

/// <summary>Storage pipeline health: Redis (real-time) → SQL Server (history).</summary>
[ApiController]
[Route("api/market")]
public class MarketStorageController(MarketPersister persister, RedisMarketStore redis, MarketDb db, DeltaMarketFeed feed) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var p = persister.Status();
        var (streamLength, pending) = await redis.StreamInfoAsync(feed.Symbol);
        MarketDbStats? stats = null;
        string? sqlError = null;
        if (p.SqlReady)
        {
            try { stats = await db.StatsAsync(feed.Symbol, ct); }
            catch (Exception ex) { sqlError = ex.Message; }
        }
        return Ok(new
        {
            symbol = feed.Symbol,
            redis = new { connected = p.RedisConnected, endpoint = p.RedisEndpoint, streamKey = RedisMarketStore.TradesKey(feed.Symbol), streamLength, pendingToSql = pending },
            sql = new { ready = p.SqlReady, server = p.SqlServer, database = p.Database, stats, error = sqlError },
            persister = new { p.LastFlushUtc, p.TradesWrittenThisRun, p.Batches, p.LastSnapshotUtc, p.LastError },
        });
    }

    /// <summary>Most recent trades as stored in SQL Server (proves the Redis → SQL path).</summary>
    [HttpGet("trades")]
    public async Task<IActionResult> Trades([FromQuery] int take = 50, CancellationToken ct = default) =>
        Ok(await db.RecentTradesAsync(feed.Symbol, take, ct));
}
