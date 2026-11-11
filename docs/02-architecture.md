# 2. Architecture

## The big picture

```
                          ┌────────────────────────── .NET API (localhost:5186) ───────────────────────────┐
 Browser (Angular 19)     │                                                                                │
 localhost:4200           │  MockDataStore (singleton, in memory)  ◄── MarketSimulator (tick every 2 s)    │
   │  /api/*  ──proxy──►  │     fake signals, accounts, positions, orders, containers, alerts              │
   │                      │                                                                                │
   │                      │  FyersClient ──HTTPS──► Fyers API v3            (real, read-only)              │
   │                      │                                                                                │
   │                      │  Simulator + FyersPoller ──► RealtimeBus ──► Redis rt:* ──► RealtimePersister  │
   │                      │                                    └──► SignalR "rt"            ──► SQL RealtimeEvents │
   │                      │                                                                                │
   │  /hubs/market (WS)   │  DeltaMarketFeed ◄──WSS── Delta Exchange India  (real, public, no login)       │
   │  ◄──SignalR────────  │        │                                                                       │
   │                      │        ├──► Redis (in WSL)  latest ticker/book + trade stream                  │
   │                      │        │        │                                                              │
   │                      │        │   MarketPersister ──► SQL Server  JarvisMarket (trades, 1m candles)   │
                          └────────────────────────────────────────────────────────────────────────────────┘
```

There are three kinds of data:

| Kind | Source | Changes? |
|---|---|---|
| **Fake OMS data** (signals, accounts, P&L, containers…) | `MockDataStore`, seeded with a fixed random seed | Yes. The [demo simulator](08-demo-simulator.md) moves it every 2 s. It resets when the API restarts |
| **Real broker data** | Fyers API v3 | Live, and only when a valid token is configured |
| **Real market data** | Delta Exchange BTCUSD | Live via WebSocket, cached in Redis, saved in SQL |

## Backend (`backend/Jarvis.Api`)

| File | Role |
|---|---|
| `Program.cs` | Registers everything: MockDataStore, FyersClient, `delta` HttpClient (needs a User-Agent), Redis multiplexer, MarketDb, the hosted services (DeltaMarketFeed, MarketPersister, MarketSimulator), SignalR, CORS |
| `Data/MockDataStore.cs` | Seeds signals, alerts, subscriptions and sessions. It builds the overview summary and handles signal actions (exit remaining, retry chunk) |
| `Data/MockDataStore.Ops.cs` | Seeds accounts, positions, orders, trades, in-flight exits, cleanup, hedges, auto-close and containers |
| `Data/MockDataStore.Sim.cs` | Demo simulator logic: price model, P&L marking, lifecycle events, scenarios |
| `Data/MarketDb.cs` | Creates the `JarvisMarket` database and tables, bulk-inserts trades, rebuilds 1-minute candles |
| `Services/FyersClient.cs` | Read-only Fyers v3 client |
| `Services/DeltaMarketFeed.cs` | One WebSocket to Delta; fans out over SignalR (`MarketHub`) and writes to Redis |
| `Services/RedisMarketStore.cs` | Redis keys and stream helpers |
| `Services/MarketPersister.cs` | Reads the Redis stream (consumer group), writes to SQL, then acknowledges |
| `Services/MarketSimulator.cs` | Background timer that calls `SimTick()` and publishes `sim.*` topics |
| `Services/RealtimeBus.cs`, `RealtimePersister.cs`, `FyersPoller.cs`, `Data/RealtimeDb.cs` | Shared realtime pipeline: Redis → SQL, plus SignalR ([11](11-realtime-pipeline.md)) |
| `Controllers/*.cs` | REST endpoints; see [10-api-reference.md](10-api-reference.md) |

**Thread safety:** every read and write of MockDataStore happens under `store.SyncRoot`, a single lock shared by the controllers and the simulator.

## Frontend (`frontend/jarvis-ui/src/app`)

- **Standalone components** with Angular **signals** (`signal`, `computed`, `effect`) and the new control flow (`@if`, `@for`, `@let`).
- `console/console-state.service.ts` holds the overview summary and simulator status. It polls them every 3 s, and its `refreshTick` tells every tab to reload when you press **Refresh**.
- `core/*.service.ts` are thin typed HTTP clients: `api` for console and signals, `ops` for positions, performance and infra, plus `fyers`, `delta-market` and `sim`.
- `shared/tick.directive.ts`: `[appTick]="value"` flashes green when the value goes up and red when it goes down.
- Charts use `lightweight-charts@4.2.3`, and live market data arrives via `@microsoft/signalr@8.0.7`.

## Routes

| URL | Page |
|---|---|
| `/admin/strategy-performance-console-v2` | The console (tabs via `?tab=signals`, `positions`, `performance`, `infrastructure`) |
| `/admin/broker-live` | Broker Live (Fyers) |
| `/admin/crypto-live` | Crypto Live (Delta) |
| `/admin/realtime` | Realtime Data (pipeline explorer) |
| everything else in the sidebar | placeholder page |
