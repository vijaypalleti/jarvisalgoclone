# 1. Getting started

## What you need

| Tool | Version used | Notes |
|---|---|---|
| .NET SDK | 10 | Runs the API |
| Node.js | 22.11 | That's why the UI uses **Angular 19**: Angular 20 needs Node ≥ 22.12 |
| SQL Server Express | 2025, instance `localhost\SQLEXPRESS` | Stores market history. Optional: the console runs without it |
| Redis | 7.x inside **WSL Ubuntu** | Real-time market cache. Optional: live prices still work without it |
| Chrome / Edge | any recent | |

Optional pieces are needed only for the Crypto Live storage pipeline ([07](07-storage-redis-sql.md)). The console, its fake data and the demo simulator need only .NET and Node.

## Run it (two terminals)

**Terminal 1: API** → http://localhost:5186

```powershell
cd "C:\Users\pvija\OneDrive\Documents\CodeLineTech\Jarvis(demo)"
dotnet run --project backend/Jarvis.Api --launch-profile http
```

**Terminal 2: UI** → http://localhost:4200

```powershell
cd "C:\Users\pvija\OneDrive\Documents\CodeLineTech\Jarvis(demo)\frontend\jarvis-ui"
npm install        # first time only
npx ng serve
```

Open http://localhost:4200. You land on **Strategy Performance Console v2**.

The UI calls `/api/...` and `/hubs/...`, and `proxy.conf.json` forwards those to the API on port 5186. Angular reads the proxy file **only at startup**, so restart `ng serve` after changing it.

## Redis starts automatically

Redis lives inside WSL Ubuntu, and WSL shuts down when idle. **The API handles this for you:** if Redis isn't reachable when the API starts, it starts Ubuntu (and Redis with it) and keeps it running until the API stops. You'll see `Redis not reachable … starting WSL 'Ubuntu'` and then `Redis is up` in the API console. Settings: `Redis:AutoStartWsl` (default `true`) and `Redis:WslDistro` (default `Ubuntu`) in `appsettings.json`.

To start it by hand (e.g. to look at Redis while the API is off):

```powershell
wsl -d Ubuntu -e true                 # starts Ubuntu; Redis starts with it
wsl -d Ubuntu -- redis-cli ping       # should print PONG
```

To keep WSL running in the background, see [Troubleshooting → Redis keeps disconnecting](09-troubleshooting.md#redis-is-not-connected--keeps-disconnecting).

## Project layout

```
Jarvis(demo)/
├─ backend/Jarvis.Api/            .NET 10 Web API
│  ├─ Program.cs                  service registration (store, feeds, Redis, SQL, simulator, SignalR)
│  ├─ appsettings.json            connection strings, Delta + Simulation settings
│  ├─ Models/                     DTOs (Models.cs = console/signals, OpsModels.cs = positions/perf/infra)
│  ├─ Data/                       MockDataStore (fake data) + MarketDb (SQL)
│  ├─ Services/                   FyersClient, DeltaMarketFeed, RedisMarketStore, MarketPersister, MarketSimulator
│  └─ Controllers/                REST endpoints
├─ frontend/jarvis-ui/            Angular 19 app
│  ├─ proxy.conf.json             /api + /hubs → localhost:5186
│  ├─ .npmrc                      os=win32 (overrides a global os=linux that broke esbuild)
│  └─ src/app/
│     ├─ layout/                  sidebar + header shell
│     ├─ console/                 the Strategy Performance console and its tabs
│     ├─ pages/                   Broker Live (Fyers), Crypto Live (Delta)
│     ├─ core/                    API services + TypeScript models
│     └─ shared/                  icons, TickDirective (green/red value flash)
└─ docs/                          you are here
```

## Git setup on this machine

Commits were showing the wrong author, so the identity for this repo was set to:

```powershell
git config user.name  "Vijay Palleti"
git config user.email "pvijaypvijay3333@gmail.com"
```

That email must also be added to your GitHub account (Settings → Emails) for GitHub to link commits to your profile.

`bin/` and `obj/` folders are currently tracked by git. Consider adding a `.gitignore` for them so build outputs stop showing as changes.
