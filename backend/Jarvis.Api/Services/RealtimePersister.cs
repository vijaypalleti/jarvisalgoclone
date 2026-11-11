using Jarvis.Api.Data;
using StackExchange.Redis;

namespace Jarvis.Api.Services;

public record RtPersisterStatus(bool SqlReady, bool RedisConnected, DateTime? LastFlushUtc, long RowsWrittenThisRun,
    long Batches, DateTime? LastPurgeUtc, string? LastError);

/// <summary>
/// Copies every real-time topic from its Redis stream into dbo.RealtimeEvents.
/// Same guarantee as the Delta persister: SQL commit first, THEN XACK. Anything not acked (API stopped, SQL down)
/// is re-read on the next pass; duplicates are dropped by the unique (Topic, RedisId) index.
/// Also purges rows older than Realtime:RetentionDays once an hour.
/// </summary>
public class RealtimePersister(RealtimeBus bus, RealtimeDb db, IConfiguration config, ILogger<RealtimePersister> log) : BackgroundService
{
    private const int BatchSize = 2000;
    private readonly string _consumer = Environment.MachineName;
    private readonly int _retentionDays = config.GetValue("Realtime:RetentionDays", 7);

    private bool _sqlReady;
    private DateTime? _lastFlush, _lastPurge;
    private long _written, _batches;
    private string? _lastError;

    public RtPersisterStatus Status() => new(_sqlReady, bus.RedisConnected, _lastFlush, _written, _batches, _lastPurge, _lastError);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested && !_sqlReady)
        {
            try { await db.EnsureCreatedAsync(stop); _sqlReady = true; _lastError = null; }
            catch (Exception ex) when (!stop.IsCancellationRequested)
            {
                _lastError = $"SQL Server: {ex.Message}";
                log.LogWarning("Realtime SQL not ready: {Message}", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(10), stop);
            }
        }

        var groupsReady = false;
        var drainPending = true;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (!bus.RedisConnected)
                {
                    groupsReady = false;
                    _lastError = "Redis is not connected — live updates continue, saving to SQL paused";
                    await Task.Delay(TimeSpan.FromSeconds(3), stop);
                    continue;
                }
                if (!groupsReady) { await bus.EnsureGroupsAsync(); groupsReady = true; drainPending = true; }

                var fullBatch = false;
                var anyRead = false;
                foreach (var topic in RealtimeBus.Topics.Select(t => t.Name))
                {
                    var entries = await bus.ReadBatchAsync(topic, _consumer, BatchSize, drainPending);
                    if (entries.Length == 0) continue;
                    anyRead = true;
                    fullBatch |= entries.Length == BatchSize;

                    var rows = entries.Select(e => new RtRow(topic, e["k"].ToString(),
                        DateTimeOffset.FromUnixTimeMilliseconds((long)e["t"]).UtcDateTime, e["d"].ToString(), e.Id.ToString())).ToList();
                    await db.SaveAsync(rows, stop);                                        // commit first…
                    await bus.AckAsync(topic, entries.Select(e => e.Id).ToArray());         // …then ack
                    _written += rows.Count; _batches++; _lastFlush = DateTime.UtcNow;
                }
                drainPending = drainPending && anyRead;   // keep draining un-acked entries until none are left
                _lastError = null;

                if (_lastPurge is null || DateTime.UtcNow - _lastPurge > TimeSpan.FromHours(1))
                {
                    var n = await db.PurgeAsync(_retentionDays, stop);
                    if (n > 0) log.LogInformation("Realtime purge removed {Rows} rows older than {Days} days", n, _retentionDays);
                    _lastPurge = DateTime.UtcNow;
                }

                if (!fullBatch && !drainPending) await Task.Delay(TimeSpan.FromSeconds(2), stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                log.LogWarning("Realtime persister error (will retry; un-acked entries stay in Redis): {Message}", ex.Message);
                drainPending = true;
                if (ex is RedisException) groupsReady = false;
                await Task.Delay(TimeSpan.FromSeconds(5), stop);
            }
        }
    }
}
