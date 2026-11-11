# 11. Realtime pipeline: every source → Redis → SQL Server

All live data goes through one shared pipeline, so a new feature doesn't need its own storage code.

```
 Source                     RealtimeBus.Publish(topic, key, data)
 ───────────                 │
 Demo simulator  ─┐          ├─► Redis  rt:{topic}:latest   HASH   key → latest JSON   ("what is it now?")
 Fyers poller    ─┼────────► ├─► Redis  rt:{topic}:stream   STREAM every change (capped ~50k)
 (your feature)  ─┘          └─► SignalR /hubs/market, group "rt:{topic}", event "rt"   (live to browsers)
                                            │
                                            ▼  RealtimePersister (every ~2 s, consumer group "sql-persister")
                             SQL Server  JarvisMarket.dbo.RealtimeEvents  (+ typed views vSimPnl, vFyersOrders…)
```

The Delta BTC feed predates this and keeps its own typed pipeline ([07](07-storage-redis-sql.md)). Everything else uses this one.

## Topics

| Topic | Source | Key | What's in it | Published |
|---|---|---|---|---|
| `sim.spot` | Simulator | `NIFTY` | spot, changePct, scenario | every tick (2 s) |
| `sim.pnl` | Simulator | client id | name, broker, realized, unrealized, total, openPositions | every 10 s per account |
| `sim.event` | Simulator | client id or `system` | kind, text, clientId, symbol, amount | every event |
| `fyers.pnl` | Fyers | Fyers id | realized, unrealized, total, open/total positions | on change |
| `fyers.funds` | Fyers | fund line title | equity, commodity | on change |
| `fyers.positions` | Fyers | position id | symbol, qty, averages, ltp, realized, unrealized, pl | on change |
| `fyers.orders` | Fyers | order id | symbol, side, qty, filledQty, **status**, price, message | on change, so every status transition is kept |
| `fyers.trades` | Fyers | trade number | orderId, symbol, side, qty, price, value | on change (new fills) |
| `fyers.holdings` | Fyers | symbol | qty, costPrice, ltp, marketValue, pl | on change |
| `fyers.quotes` | Fyers | symbol | ltp, change, changePct, OHLC, volume | on change |

**"On change"** means the Fyers poller fetches every 10 s but publishes a record only when its content differs from last time. History therefore stays small and every row is a real change.

## Where to look

| I want… | Use |
|---|---|
| to see it working | the **Realtime Data** page (sidebar → `/admin/realtime`). For each topic it shows Published → Redis → Pending → SQL rows; click a topic for its Latest / Live / History values and a ready-to-copy SQL query |
| the current value | `GET /api/realtime/{topic}/latest` |
| the last N changes | `GET /api/realtime/{topic}/recent?count=50` (Redis stream) |
| older data | `GET /api/realtime/{topic}/history?key=&from=&to=&take=200` (SQL; times in UTC) |
| pipeline health | `GET /api/realtime` |
| SQL | `SELECT TOP 100 * FROM dbo.vSimPnl ORDER BY EventTime DESC;` (views listed below) |
| Redis | `wsl -d Ubuntu -- redis-cli hgetall rt:sim.pnl:latest`, `… xrevrange rt:sim.event:stream + - COUNT 5` |

### SQL views (database `JarvisMarket`, times in UTC)

`vSimSpot`, `vSimPnl`, `vSimEvents`, `vFyersPnl`, `vFyersFunds`, `vFyersPositions`, `vFyersOrders`, `vFyersTrades`, `vFyersHoldings`, `vFyersQuotes`.

Each one has real columns, extracted from the JSON payload with `JSON_VALUE`. Example queries:

```sql
-- P&L curve of one account today (IST)
SELECT DATEADD(minute, 330, EventTime) AS TimeIst, Total, Realized, Unrealized
FROM dbo.vSimPnl WHERE ClientId = '1' AND EventTime >= CAST(SYSUTCDATETIME() AS date) ORDER BY EventTime;

-- worst drawdown per account
SELECT ClientId, MIN(Total) AS WorstPnl, MAX(Total) AS BestPnl FROM dbo.vSimPnl GROUP BY ClientId ORDER BY WorstPnl;

-- life of every Fyers order (each status change is a row)
SELECT OrderId, EventTime, Status, FilledQty, Message FROM dbo.vFyersOrders ORDER BY OrderId, EventTime;
```

## Building a new feature on it

**Backend: publish your data** (any service or controller):

```csharp
public class MyFeature(RealtimeBus bus)
{
    void OnSomething(Order o) =>
        bus.Publish("myfeature.orders", o.Id, new { o.Symbol, o.Qty, o.Status }, onlyIfChanged: true);
}
```

Then:
1. Add the topic to `RealtimeBus.Topics` (name, source, description, key meaning). Publishing works without this, but the persister, endpoints and page only handle registered topics.
2. Optionally add a typed `CREATE OR ALTER VIEW` to `RealtimeDb.Views` so you can query real columns.

There's no new table, Redis code or persister code to write. Payloads should be **flat objects** with camelCase fields; the page and views assume this.

**Frontend: read it** with `core/realtime.service.ts`:

```ts
private rt = inject(RealtimeService);

this.rt.latest('sim.pnl').subscribe(rows => ...);                        // now
this.rt.history('sim.pnl', { key: '1', take: 500 }).subscribe(...);      // past (SQL)
this.rt.live<{ total: number }>('sim.pnl')                               // push (SignalR)
  .pipe(takeUntilDestroyed(this.destroyRef))
  .subscribe(m => console.log(m.key, m.data.total));
```

`live()` shares one SignalR connection, unsubscribes from the topic when the last subscriber leaves, and re-subscribes after a reconnect.

## Reliability

| Situation | What happens |
|---|---|
| Redis down | Publishing skips Redis, `latest` answers from an in-memory copy, and live SignalR still works. **History for that period is not recorded** |
| SQL down or API stopped | Entries wait in the Redis stream and are saved later. SQL commits before Redis is acknowledged |
| Same entry delivered twice | Dropped by the unique `(Topic, RedisId)` index |
| Disk growth | Rows older than `Realtime:RetentionDays` (7) are purged hourly. Redis streams are capped at `Realtime:StreamMaxLength` (50,000) per topic |

## Settings (`appsettings.json`)

```json
"Realtime": { "StreamMaxLength": 50000, "RetentionDays": 7, "SimPnlSeconds": 10 },
"Fyers": { "Poll": { "Enabled": true, "Seconds": 10, "QuoteSymbols": "NSE:NIFTY50-INDEX,NSE:NIFTYBANK-INDEX,BSE:SENSEX-INDEX" } }
```

Fyers credentials stay in user-secrets ([05](05-broker-live-fyers.md)). If the token is missing or expired, the poller shows *token invalid* on the Realtime Data page and retries every minute, and nothing else is affected.

**Volume guide:** `sim.pnl` is 41 accounts every 10 s, which is about 350k rows per full day of running. Raise `SimPnlSeconds` if that's too much.

## Files

| File | Role |
|---|---|
| `backend/.../Services/RealtimeBus.cs` | `Publish`, topic registry, Redis and SignalR fan-out, latest/recent reads |
| `backend/.../Services/RealtimePersister.cs` | Redis streams → SQL (commit, then ack), hourly purge |
| `backend/.../Data/RealtimeDb.cs` | `RealtimeEvents` table, typed views, history and stats queries |
| `backend/.../Services/FyersPoller.cs` | Background read-only Fyers poll → `fyers.*` topics |
| `backend/.../Services/MarketSimulator.cs` | Publishes `sim.*` after each tick |
| `backend/.../Controllers/RealtimeController.cs` | `/api/realtime/*` |
| `backend/.../Services/DeltaMarketFeed.cs` | `MarketHub.Subscribe/Unsubscribe(topic)` |
| `frontend/.../core/realtime.service.ts` | `latest / recent / history / live` |
| `frontend/.../pages/realtime/` | Realtime Data page |
