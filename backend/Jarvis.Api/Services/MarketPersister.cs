using Jarvis.Api.Data;
using StackExchange.Redis;

namespace Jarvis.Api.Services;

public record PersisterStatus(
    bool SqlReady, bool RedisConnected, string RedisEndpoint, string SqlServer, string Database,
    DateTime? LastFlushUtc, long TradesWrittenThisRun, long Batches, DateTime? LastSnapshotUtc, string? LastError);

/// <summary>
/// Moves real-time data from Redis into SQL Server:
///   • trades: reads the Redis stream with a consumer group in batches, bulk-inserts into dbo.DeltaTrades,
///     rebuilds dbo.DeltaCandles1m for the touched minutes, and only THEN acks — so nothing is lost if the API
///     or SQL Server goes down (un-acked entries are re-read on the next run; duplicates are ignored by SQL).
///   • ticker: snapshot from Redis into dbo.DeltaTickerSnapshots every N seconds.
/// </summary>
public class MarketPersister(
    RedisMarketStore redis, MarketDb db, DeltaMarketFeed feed, IConfiguration config, ILogger<MarketPersister> log) : BackgroundService
{
    private const int BatchSize = 2000;
    private readonly TimeSpan _snapshotEvery = TimeSpan.FromSeconds(config.GetValue("Delta:TickerSnapshotSeconds", 60));
    private readonly string _consumer = Environment.MachineName;

    private bool _sqlReady;
    private DateTime? _lastFlush, _lastSnapshot;
    private long _written, _batches;
    private string? _lastError;

    public PersisterStatus Status() => new(_sqlReady, redis.Connected, redis.Endpoint, db.Server, db.DatabaseName,
        _lastFlush, _written, _batches, _lastSnapshot, _lastError);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        // 1) make sure the database + tables exist (retry until SQL Server is reachable)
        while (!stop.IsCancellationRequested && !_sqlReady)
        {
            try { await db.EnsureCreatedAsync(stop); _sqlReady = true; _lastError = null; }
            catch (Exception ex) when (!stop.IsCancellationRequested)
            {
                _lastError = $"SQL Server: {ex.Message}";
                log.LogWarning("SQL Server not ready: {Message}", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(10), stop);
            }
        }

        var symbol = feed.Symbol;
        var groupReady = false;
        var drainPending = true; // after a restart, first finish anything delivered-but-unacked

        // 2) pump Redis → SQL
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (!redis.Connected)
                {
                    groupReady = false;
                    _lastError = "Redis is not connected — live feed continues, persistence paused";
                    await Task.Delay(TimeSpan.FromSeconds(3), stop);
                    continue;
                }
                if (!groupReady) { await redis.EnsureGroupAsync(symbol); groupReady = true; drainPending = true; }

                var entries = await redis.ReadBatchAsync(symbol, _consumer, BatchSize, drainPending);
                if (drainPending && entries.Length == 0) { drainPending = false; continue; }

                if (entries.Length > 0)
                {
                    var rows = entries.Select(e =>
                    {
                        var (t, us) = RedisMarketStore.ParseTrade(e);
                        return new StoredTrade(e.Id.ToString(), symbol, us, t.Price, t.Size, t.Side);
                    }).ToList();

                    await db.SaveTradesAsync(rows, stop);                               // SQL commit first…
                    await redis.AckAsync(symbol, entries.Select(e => e.Id).ToArray());   // …then ack in Redis

                    _written += rows.Count; _batches++; _lastFlush = DateTime.UtcNow; _lastError = null;
                }

                await SnapshotTickerIfDue(symbol, stop);

                // full batch → keep draining immediately; otherwise wait for more trades
                if (entries.Length < BatchSize) await Task.Delay(TimeSpan.FromSeconds(2), stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                log.LogWarning("Persister error (will retry; un-acked trades stay in Redis): {Message}", ex.Message);
                drainPending = true;
                if (ex is RedisException) groupReady = false;
                await Task.Delay(TimeSpan.FromSeconds(5), stop);
            }
        }
    }

    private async Task SnapshotTickerIfDue(string symbol, CancellationToken stop)
    {
        if (_lastSnapshot is { } last && DateTime.UtcNow - last < _snapshotEvery) return;
        var ticker = await redis.GetTickerAsync(symbol);
        if (ticker is null) return;
        await db.SaveTickerSnapshotAsync(ticker, stop);
        _lastSnapshot = DateTime.UtcNow;
    }
}
