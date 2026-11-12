# 7. Storage: Redis (real-time) → SQL Server (history)

> This page covers the **Delta BTC** pipeline. Simulator and Fyers data use the shared [realtime pipeline](11-realtime-pipeline.md) (`rt:*` keys in Redis, `dbo.RealtimeEvents` and `v*` views in SQL).

**The idea:** Redis holds the newest market data and buffers incoming trades. SQL Server keeps the permanent history. Data goes into Redis first and is copied to SQL in small batches.

```
DeltaMarketFeed ──► Redis  delta:BTCUSD:ticker   latest ticker (overwritten)
                           delta:BTCUSD:book     latest order book (overwritten)
                           delta:BTCUSD:trades   Stream of every trade (~100k max)
                                  │  consumer group "sql-persister"
                                  ▼
                   MarketPersister  (every ~2 s)
                     1. read new trades from the stream
                     2. bulk insert into SQL (SqlBulkCopy)
                     3. rebuild the affected 1-minute candles (MERGE)
                     4. XACK in Redis, only after SQL committed
                                  ▼
                   SQL Server  localhost\SQLEXPRESS  →  database JarvisMarket
                     dbo.DeltaTrades           every trade (unique RedisId, so no duplicates)
                     dbo.DeltaCandles1m        built from trades; 5m/1h/1d are aggregated from these
                     dbo.DeltaTickerSnapshots  mark / funding / OI once a minute
```

## Why it is safe

- **SQL down or API stopped:** un-acknowledged trades stay in the Redis stream and are written when things come back.
- **Redis down:** live prices keep streaming to the browser and only saving pauses. The app shows *"Redis is not connected — live feed continues, persistence paused"*.
- **Duplicates:** `RedisId` has a unique index with `IGNORE_DUP_KEY`, so a batch written twice doesn't create duplicate rows.

## Setup

### SQL Server
Nothing to do. On first run the API creates the `JarvisMarket` database and its tables. The connection string is `ConnectionStrings:MarketDb` in `appsettings.json` and uses Windows authentication.

### Redis (inside WSL Ubuntu)
It is installed once with:
```bash
sudo apt update && sudo apt install -y redis-server
sudo systemctl enable --now redis-server
```
Connection string: `ConnectionStrings:Redis` = `localhost:6379,abortConnect=false,connectTimeout=3000`.

> **WSL stops when idle, and Redis stops with it.** The API starts it automatically when it boots (`Services/WslRedisStarter.cs`). By hand: `wsl -d Ubuntu -e true`. For a permanent fix, see [Troubleshooting](09-troubleshooting.md#redis-is-not-connected--keeps-disconnecting).

## Checking that it works

```powershell
# whole pipeline: Redis connected? SQL ready? last flush? rows saved?
Invoke-RestMethod http://localhost:5186/api/market/status

# latest trades saved in SQL
Invoke-RestMethod "http://localhost:5186/api/market/trades?take=10"
```

### Look at Redis from the terminal
```powershell
wsl -d Ubuntu -- redis-cli ping                                   # PONG
wsl -d Ubuntu -- redis-cli get delta:BTCUSD:ticker                # latest ticker JSON
wsl -d Ubuntu -- redis-cli xlen delta:BTCUSD:trades               # trades in the stream
wsl -d Ubuntu -- redis-cli xrevrange delta:BTCUSD:trades + - COUNT 5
wsl -d Ubuntu -- redis-cli xinfo groups delta:BTCUSD:trades       # "pending" should be ~0
```

### Look at Redis in VS Code (Redis extension)
Add a connection with:
- **Host:** `localhost` (or `127.0.0.1`)
- **Port:** `6379`
- **No username or password**

Don't use the WSL IP (`172.x.x.x` from `wsl hostname -I`). Redis listens only on localhost inside WSL, and Windows reaches it through WSL's localhost forwarding.

Turn on the **JSON** view to read the ticker and book values.

### Look at SQL Server
In SSMS or the VS Code SQL extension, connect to `localhost\SQLEXPRESS` with Windows authentication. Then:
```sql
USE JarvisMarket;
SELECT TOP 20 * FROM dbo.DeltaTrades          ORDER BY TradeTime DESC;     -- times are UTC
SELECT TOP 20 * FROM dbo.DeltaCandles1m       ORDER BY OpenTime DESC;
SELECT TOP 20 * FROM dbo.DeltaTickerSnapshots ORDER BY SnapshotTime DESC;
```
The full schema is in `Data/MarketDb.cs`. For BTCUSD, `Size` is in contracts, and 1 contract = 0.001 BTC.

## Files

`Services/RedisMarketStore.cs`, `Services/MarketPersister.cs`, `Data/MarketDb.cs`, and `Controllers/DeltaController.cs` (`MarketStorageController`).

## Looking at the data yourself

Open **SSMS** and connect to `localhost\SQLEXPRESS` with Windows Authentication (in *Options → Connection Properties* tick **Trust server certificate**). Then open [sql/useful-queries.sql](sql/useful-queries.sql). It has queries for BTC candles/trades/ticker, the Fyers views, simulator P&L/events/spot and the AI chat log.

- Times are stored in **UTC**; add 5:30 for IST (`DATEADD(MINUTE, 330, EventTime)`).
- For `DeltaTrades` sort by `Id` (indexed), not `TradeTime` (200k+ rows scanned, ~30 s).
- `v*` views parse JSON, so filter by time (`EventTime >= ...`) or use `TOP` on large topics like `sim.pnl`.

