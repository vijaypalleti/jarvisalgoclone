# 3. Console pages

The **Strategy Performance Console v2** was cloned from screenshots of jarvisalgo.in. All data on these tabs is fake and comes from `MockDataStore`. With the [demo simulator](08-demo-simulator.md) running, it changes over time.

Frontend code lives in `frontend/jarvis-ui/src/app/console/`. To add a tab, create its component and add a `@case` for it in `console.component.html`.

## Header (all tabs)

- **Toolbar:** Compare Accounts, **Refresh** (reloads every tab), Export (downloads the overview as JSON).
- **Relay pill:** one green pill with *Relay Live · Exec % · DLQ %*. Hover for dispatch % and lag. These used to be a separate strip.
- **Demo pill:** simulator status, live NIFTY price, scenario picker and pause button ([08](08-demo-simulator.md)).
- **Red banner:** dead letters and failed executions. You can close it with ✕ for the rest of the browser session; a small "⚠ N" chip brings it back.

## Overview (`console/overview/`)

- **Hub:** 5 tiles on each side wired to a central core (154 px) by animated rails. The tile columns are **fluid**: they share the card's width (at least 240 px each), so on a wider screen or when you zoom out the tiles stretch, like on the real site, and the rails redraw to fit. If the card is too narrow, it scrolls sideways instead of squashing the tiles. The core has an aura, a dial and a shimmer. Each rail has three layers (base path, moving beads, a spark) and glowing end dots.
  - Rails are redrawn whenever the layout can change: element resize, window resize, browser zoom (device-pixel-ratio change) and every data refresh. Logic is in `drawConnectors()` in `overview-tab.component.ts`.
  - Tiles flash briefly when their value changes, and play a "launch" animation when clicked before navigating to their tab.
  - Animations respect the OS *reduce motion* setting.
  - SVG `url(#id)` references use the absolute page URL because of `<base href="/">`; otherwise gradients disappear in some browsers.
- **Today's activity:** signals per hour and the order funnel (signals → orders → filled / rejected / working).
- **Needs attention** panel.
- **Recent Alerts:** broker cards with rejection lists and acknowledge buttons. Real Fyers rejections are merged in as `FYERS <id> · LIVE`.
- **Subscription Watch** and **Broker Session Health**.

## Live Signals & Lifecycle (`console/signals/`)

Its own guide: [04-live-signals.md](04-live-signals.md).

## Positions & Exits (`console/positions/`)

| Sub-tab | Shows |
|---|---|
| Position Tracking | Broker accounts on the left. The right side shows the selected account's positions (side, quantities, averages, realized/unrealized P&L, flags, signal trace) and order history. Each position can be force-exited |
| In-Flight Exits | Exits on their way to the broker, by stage: QUEUED → SENT → PARTIAL / RETRYING |
| Needs Cleanup | Mismatches: OMS vs broker quantity, orphan positions, stale LTP, hedge left open |
| Hedges | Short positions and their protective CE/PE legs: PROTECTED / PARTIAL / NAKED |
| Auto-Close Log | Automatic exits (MIS square-off, max loss, target, expiry) |

Auto-refresh is on by default (every 3 s).

## Performance → Trading (`console/performance/`)

- Filters (basket, strategy, date range) and summary cards: Strategy P&L, Active accounts, Open positions.
- **Strategy filter** is a searchable multi-select (Clear / Apply). With strategies selected, *every* number covers only those strategies: KPIs, each user's R/U/total P&L, open/closed counts, the positions table and the trades. Accounts with none of them drop out of scope. Empty selection means all strategies.
- User list with total P&L split into realized (**R**) and unrealized (**U**).
- The selected user's detail: P&L card, positions table, trade history (expand a row for its correlation trace).
- P&L and LTP values flash green or red as they change.

## Infrastructure (`console/infrastructure/`)

Container health dashboard: running and stopped counts, health, heartbeat, CPU and memory, Redis and Mongo latency, stream messages, restarts, IP and NIC routing checks. Each container has Restart and Stop actions, plus Provision all, Stop all and Reload contract master. Auto-refresh every 3 s.

## Baskets and strategies

The names come from production and live in `backend/Jarvis.Api/Data/Catalog.cs`:
- 19 baskets, some with descriptions;
- 75 strategies, each with its code (e.g. `STRAT1510A589`), a basket and an underlying (NIFTY / BANKNIFTY / SENSEX / MCX / CRYPTO, or none).

Fake signals and positions only use the index strategies, and always one matching the symbol's underlying. The strategy → basket links are guessed from the names; edit `Catalog.cs` if the real mapping differs. To add a basket or strategy, add a line there; the filters, signals and accounts pick it up on the next API restart.

## Not cloned yet

Logs & Audit, Alerts, Trading Calendar, Strategy Cache, Queue Monitor, Rejected Signals, Send Signal, the Performance sub-tabs other than Trading, and the other sidebar pages. These show a placeholder.
