# 8. Demo simulator (moving fake data)

Without the simulator, the console's fake data is fixed: it is seeded once when the API starts. The simulator makes it behave like a live trading system for demos. It **only touches fake data**; Fyers and Delta are never affected.

## What it does every tick (2 s)

1. **Moves NIFTY:** a random walk around 24,850 with noise and drift that depend on the scenario. In neutral scenarios it drifts back toward the open, and it is capped at ±5%.
2. **Re-prices every option:**
   - calls move with NIFTY and puts move against it (more for in-the-money strikes);
   - plus a little noise and slow time decay.

   One price per symbol is shared by all accounts.
3. **Marks every open position:**
   - unrealized P&L and P&L % are recalculated;
   - a position losing more than 20% turns **RISK**;
   - one losing more than 60% is **auto-closed** and logged in the Auto-Close Log.
4. **Generates events** (more often in Volatile and Crash):
   - new entries, with about 6% rejected for margin;
   - exits (which add an exit order and close the matching trade);
   - in-flight exits moving through QUEUED → SENT → PARTIAL/RETRYING → done, which closes the position;
   - new signals, about once a minute in Normal;
   - older signals exiting, with about 1 in 8 leaving a stuck chunk to demo [Exit partial](04-live-signals.md);
   - new broker rejections in Alerts;
   - container CPU, memory and latency changes, with an occasional "Degraded" container that recovers.
5. **Overview numbers drift:** Exec %, DLQ % and success rate.

## Scenarios

| Scenario | Market | Event rate | Good for showing |
|---|---|---|---|
| Calm | barely moves | 0.4× | a quiet, steady day |
| **Normal** (default) | small swings | 1× | general demo |
| Volatile | big swings | 1.8× | P&L flashing, many exits |
| Rally | steady rise | 1.2× | long calls and short puts winning |
| Crash | steady fall | 1.6× | losses, RISK flags, auto-closes, alerts |

## How to control it

**In the UI:** the **Demo live** pill at the top right of the console.
- It shows the live NIFTY price and its % change.
- The **dropdown** switches the scenario.
- **❚❚ / ▶** pauses and resumes. Pause when you need numbers to hold still while explaining.
- **Click the pill** to see the last 40 simulated events and the total P&L.

**From the terminal:**
```powershell
Invoke-RestMethod http://localhost:5186/api/sim                                   # status + recent events
Invoke-RestMethod -Method Post http://localhost:5186/api/sim/scenario/crash       # calm|normal|volatile|rally|crash
Invoke-RestMethod -Method Post http://localhost:5186/api/sim/pause
Invoke-RestMethod -Method Post http://localhost:5186/api/sim/resume
```

**At startup** (`backend/Jarvis.Api/appsettings.json`):
```json
"Simulation": { "Enabled": true, "TickMs": 2000, "Scenario": "Normal" }
```
`Enabled: false` turns it off completely, so the data stays as seeded.

## How the pages pick it up

| Page | Refresh |
|---|---|
| Console header (overview numbers, demo pill) | every 3 s |
| Positions & Exits, Performance, Infrastructure | every 3 s (Auto-refresh checkbox, on by default) |
| Live Signals | every 5 s |

P&L and LTP values flash **green** when they go up and **red** when they go down (`shared/tick.directive.ts`, used as `[appTick]="value"`).

## Things to know

- The simulator uses its own unseeded random generator. The *starting* data is always the same, but every run evolves differently.
- Everything resets when the API restarts.
- To tune the behaviour, edit `Params` (volatility, drift and event rate per scenario) and the `Chance(...)` probabilities in `Data/MockDataStore.Sim.cs`.

## Files

| File | Role |
|---|---|
| `backend/.../Data/MockDataStore.Sim.cs` | All simulation logic |
| `backend/.../Services/MarketSimulator.cs` | Background timer |
| `backend/.../Controllers/SimController.cs` | `/api/sim` endpoints |
| `frontend/.../core/sim.service.ts` | API client |
| `frontend/.../console/console.component.*` | Demo pill, scenario picker, event feed |
| `frontend/.../shared/tick.directive.ts` | Green/red value flash |
