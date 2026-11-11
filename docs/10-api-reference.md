# 10. API reference

The base URL is `http://localhost:5186`. The UI reaches it through the `/api` proxy.

## Console: overview, alerts, sessions (`ConsoleControllers.cs`)

| Method | Route | Notes |
|---|---|---|
| GET | `/api/console/overview` | Tile counts, relay stats, banner numbers |
| GET | `/api/console/alerts?search=` | Alert accounts with their rejections (live Fyers rejections merged in) |
| POST | `/api/console/alerts/{accountId}/ack` | Acknowledge one account's alerts |
| POST | `/api/console/alerts/rejections/{rejectionId}/ack` | Acknowledge one rejection |
| POST | `/api/console/alerts/ack-all` | Acknowledge everything |
| GET | `/api/console/subscriptions/expiring?page=&pageSize=` | Subscriptions ending soon |
| GET | `/api/console/broker-sessions` | Broker session health |
| GET | `/api/console/activity/today` | Signals per hour and the order funnel |

## Signals

| Method | Route | Notes |
|---|---|---|
| GET | `/api/console/signals?from=&to=&symbol=&correlationId=&client=&basket=&strategy=&filter=&sort=&take=` | `filter`: `attention`, `all`, `entry`, `exit`, `exit-pending`, `exit-completed`, `partial-exit`, `chunked` |
| GET | `/api/console/signals/filters` | Basket and strategy options |
| GET | `/api/console/signals/{id}?client=&account=&broker=&status=&page=&pageSize=` | Detail, with attention list, status counts and severity-sorted accounts |
| POST | `/api/console/signals/{id}/force-exit` | Square off every open account |
| POST | `/api/console/signals/{id}/accounts/{clientId}/exit-remaining` | Exit one account's residual |
| POST | `/api/console/signals/{id}/accounts/{clientId}/chunks/{index}/retry` | Re-place one exit chunk |

## Positions & Exits (`OpsControllers.cs`)

| Method | Route |
|---|---|
| GET | `/api/console/positions/summary` |
| GET | `/api/console/positions/accounts?search=&broker=&clientId=&partnerId=&openOnly=&sort=` |
| GET | `/api/console/positions/accounts/{clientId}?status=&symbol=&strategyId=&correlation=&product=` |
| GET | `/api/console/positions/inflight` · `/cleanup` · `/hedges` · `/auto-close` |
| POST | `/api/console/positions/accounts/{clientId}/{positionId}/exit` (force-exit one position at LTP) |

## Performance

| Method | Route |
|---|---|
| GET | `/api/console/performance/summary?basket=&partnerId=&name=&broker=&sort=&pageSize=&strategies=a,b` (P&L/positions from those strategies only) |
| GET | `/api/console/performance/users/{clientId}?trades=45&strategies=a,b` |

## Infrastructure

| Method | Route |
|---|---|
| GET | `/api/console/infrastructure/containers` |
| POST | `/api/console/infrastructure/containers/{name}/restart` · `/stop` |
| POST | `/api/console/infrastructure/containers/provision-all` · `/stop-all` |
| POST | `/api/console/infrastructure/contract-master/reload` |

## Demo simulator (`SimController.cs`)

| Method | Route |
|---|---|
| GET | `/api/sim` (status: running, scenario, NIFTY spot, total P&L, last 40 events) |
| POST | `/api/sim/pause` · `/api/sim/resume` |
| POST | `/api/sim/scenario/{calm\|normal\|volatile\|rally\|crash}` |

## Realtime pipeline (`RealtimeController.cs`)

| Method | Route |
|---|---|
| GET | `/api/realtime` (Redis/SQL/persister/Fyers-poller health and per-topic counts) |
| GET | `/api/realtime/{topic}/latest` (current value per key; Redis, or memory if Redis is down) |
| GET | `/api/realtime/{topic}/recent?count=50` (last changes from the Redis stream) |
| GET | `/api/realtime/{topic}/history?key=&from=&to=&take=200` (SQL; `from`/`to` in UTC) |
| WS | `/hubs/market` → invoke `Subscribe(topic)` / `Unsubscribe(topic)` → event `rt` `{ topic, key, timeUtc, data }` |

Topics: `sim.spot`, `sim.pnl`, `sim.event`, `fyers.pnl`, `fyers.funds`, `fyers.positions`, `fyers.orders`, `fyers.trades`, `fyers.holdings`, `fyers.quotes`.

## Fyers login (`FyersAuthController.cs`)

| Method | Route |
|---|---|
| GET | `/api/fyers/auth/config` (app id, whether the secret is set, redirect URI, one-click or not, saved-session info; no secrets) |
| GET | `/api/fyers/auth/start` (`{ url }` Fyers login URL with a random state) |
| GET | `/api/fyers/auth/callback?auth_code=&state=` (Fyers redirects here; exchanges the code and returns a small page that closes itself) |
| POST | `/api/fyers/auth/exchange` `{ input }` (redirected URL or raw auth_code) |
| POST | `/api/fyers/auth/renew` `{ pin }` (refresh token + PIN, no browser login) |
| DELETE | `/api/fyers/auth/saved` (forget the saved session) |

## Fyers (real, read-only)

| Method | Route |
|---|---|
| GET | `/api/fyers/status` · `/funds` · `/positions` · `/orders` · `/trades` · `/holdings` |
| GET | `/api/fyers/quotes?symbols=NSE:NIFTY50-INDEX,...` |
| POST | `/api/fyers/token` `{ clientId, accessToken }` (in-memory only) |

## Delta Exchange and storage

| Method | Route |
|---|---|
| GET | `/api/delta/snapshot` |
| GET | `/api/delta/candles?resolution=1m\|5m\|15m\|1h\|4h\|1d&count=300` |
| GET | `/api/market/status` (Redis, SQL and persister health) |
| GET | `/api/market/trades?take=50` (latest trades in SQL) |
| WS | `/hubs/market` (SignalR events `ticker`, `trade`, `orderbook`, `status`) |
| GET | `/api/health` |
