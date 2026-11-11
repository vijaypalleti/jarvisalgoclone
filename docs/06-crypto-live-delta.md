# 6. Crypto Live (Delta Exchange)

A sidebar page (`/admin/crypto-live`) showing the **live BTCUSD perpetual** from Delta Exchange India. It uses public market data, so **no login or API key** is needed.

It shows the last price, mark and index prices, 24h stats, funding, open interest, the top 15 order book levels, a running trade feed, and a candle chart (1m, 5m, 15m, 1h, 4h, 1d) whose last candle updates on every trade.

## How the data flows

```
Delta WebSocket  wss://socket.india.delta.exchange
   │  channels: v2/ticker · all_trades · l2_orderbook
   ▼
DeltaMarketFeed   (BackgroundService in the API: one connection, auto-reconnect,
   │               book trimmed to 15 levels, pushed at most 3×/s)
   ├──► SignalR hub /hubs/market   events: ticker · trade · orderbook · status
   │        ▼
   │     Angular DeltaMarketService → CryptoLiveComponent (lightweight-charts)
   │
   └──► Redis → SQL Server   (history, see 07-storage-redis-sql.md)
```

- The browser **never waits on storage**. Live updates go straight out over SignalR.
- When the API restarts, it warm-starts from the last ticker and book saved in Redis, so the page isn't empty.
- **Chart history:** candle buckets since recording started come from SQL; older or missing buckets come from Delta's REST API (`/v2/history/candles`).

## Settings (`appsettings.json` → `Delta`)

| Key | Default | Meaning |
|---|---|---|
| `Symbol` | `BTCUSD` | Contract to stream |
| `RedisTradeStreamMaxLength` | `100000` | Maximum length of the Redis trade stream |
| `TickerSnapshotSeconds` | `60` | How often a ticker snapshot is saved to SQL |

## Files

- Backend: `Services/DeltaMarketFeed.cs` (also contains `MarketHub`), `Controllers/DeltaController.cs`
- Frontend: `core/delta-market.service.ts`, `pages/crypto-live/`

## Endpoints

`GET /api/delta/snapshot`, `GET /api/delta/candles?resolution=5m&count=300`, SignalR `/hubs/market`.

## Gotchas

- Delta's REST API returns an HTML page instead of JSON when a request has no `User-Agent`, so the `delta` HttpClient sets one.
- If the page says **DISCONNECTED**, the usual cause is an `ng serve` started before the `/hubs` proxy entry existed. Restart `ng serve`.
