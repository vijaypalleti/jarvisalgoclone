using Jarvis.Api.Data;
using Jarvis.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jarvis.Api.Controllers;

/// <summary>
/// One read API for every real-time topic (simulator, Fyers, future sources):
///   latest  → Redis hash (what is it now)       recent → Redis stream (last N changes)
///   history → SQL Server (anything older, filterable by key and time)
/// Live push: SignalR /hubs/market → invoke Subscribe(topic) → event "rt".
/// </summary>
[ApiController]
[Route("api/realtime")]
public class RealtimeController(RealtimeBus bus, RealtimeDb db, RealtimePersister persister, FyersPoller fyers) : ControllerBase
{
    /// <summary>Pipeline health + per-topic counts in memory, Redis and SQL.</summary>
    [HttpGet]
    public async Task<object> Status(CancellationToken ct)
    {
        List<RtSqlStats> sql = [];
        string? sqlError = null;
        try { sql = await db.StatsAsync(ct); } catch (Exception ex) { sqlError = ex.Message; }

        var topics = new List<object>();
        foreach (var t in bus.Stats())
        {
            var (len, pending) = await bus.StreamInfoAsync(t.Topic);
            var s = sql.FirstOrDefault(x => x.Topic == t.Topic);
            topics.Add(new
            {
                t.Topic, t.Source, t.Description, t.KeyMeaning, t.PublishedThisRun, t.LastPublishUtc, t.LatestKeys,
                redisStreamLength = len, pendingToSql = pending,
                sqlRows = s?.Rows ?? 0, sqlFirstUtc = s?.FirstUtc, sqlLastUtc = s?.LastUtc,
            });
        }
        return new { redisConnected = bus.RedisConnected, persister = persister.Status(), sqlError, fyersPoller = fyers.Status(), topics };
    }

    [HttpGet("{topic}/latest")]
    public async Task<ActionResult<List<RtMessage>>> Latest(string topic) =>
        RealtimeBus.IsKnown(topic) ? await bus.LatestAsync(topic) : UnknownTopic(topic);

    [HttpGet("{topic}/recent")]
    public async Task<ActionResult<List<RtMessage>>> Recent(string topic, [FromQuery] int count = 50) =>
        RealtimeBus.IsKnown(topic) ? await bus.RecentAsync(topic, Math.Clamp(count, 1, 1000)) : UnknownTopic(topic);

    /// <param name="from">UTC (ISO 8601)</param><param name="to">UTC (ISO 8601)</param>
    [HttpGet("{topic}/history")]
    public async Task<ActionResult<List<RtMessage>>> History(string topic, [FromQuery] string? key, [FromQuery] DateTime? from,
        [FromQuery] DateTime? to, [FromQuery] int take = 200, CancellationToken ct = default) =>
        RealtimeBus.IsKnown(topic) ? await db.HistoryAsync(topic, key, from, to, take, ct) : UnknownTopic(topic);

    private NotFoundObjectResult UnknownTopic(string topic) =>
        NotFound(new { error = $"Unknown topic '{topic}'", topics = RealtimeBus.Topics.Select(t => t.Name) });
}
