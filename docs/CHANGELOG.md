# Changelog

Newest first. Add a line here whenever something changes (see [README](README.md#keeping-these-docs-up-to-date)).

## 2026-11-11

- **Fyers login from the app** (replaces the Python `generate_authcode` script).
  - **Login with Fyers** on Broker Live opens the Fyers login in a popup. With the redirect URL pointed at `/api/fyers/auth/callback`, the API swaps the auth code (`appIdHash` = sha256 of `appId:secret`) for an access token and the page goes LIVE. Otherwise you paste the redirected URL into the page.
  - **Renew with PIN** uses the saved refresh token (about 15 days) to renew the daily session without a browser login.
  - The token is saved encrypted in `%LOCALAPPDATA%\JarvisDemo\fyers-token.dat` (Data Protection/DPAPI) and resumed on restart. The secret key stays in user-secrets (`Fyers:SecretKey`).
  - Verified against Fyers: the login URL loads (HTTP 200), a fake auth code is rejected with "invalid auth code" (so the request format is accepted), forged callbacks are refused, and the error messages are clear. A full login with a real account still has to be done by you.
- **Redis now starts with the API:** new `WslRedisStarter`. If `localhost:6379` is closed at startup, the API starts WSL Ubuntu (Redis boots with it as a systemd service, with `service redis-server start` as a fallback) and keeps a hidden `wsl.exe … sleep infinity` running until the API stops. Turn it off with `Redis:AutoStartWsl=false`. Verified: Ubuntu stopped → API started → Redis reachable about 3 s later and still up 30 s later.
- **Strategy filter is a real multi-select and actually filters:** the dropdown matches production: search, checkboxes with the STRAT code on the right, "N strategies selected", Clear / Apply (`shared/multi-select.component.ts`).
  - **Performance:** selected strategies drive every number, from the KPIs and each user's P&L (realized and unrealized) to positions and trades. Accounts with none of those strategies drop out of scope. Before this change the dropdown only changed the "Scope" text.
  - **Live Signals:** shows signals from any of the selected strategies.
  - API: `strategies=a,b` on `/performance/summary` and `/performance/users/{id}`; `strategy=a,b` on `/signals`.
  - Verified: S1EN only gives −₹21,644 across 23 accounts (all strategies: +₹79,186 across 41), and the detail shows only S1EN positions and trades.
- **Real baskets and strategies:** the made-up baskets (SWL_SENSEX, MS_NIFTY…) and strategies (S2HD, QIP…) were replaced with the production lists: 19 baskets and 75 strategies with their STRAT codes, in the new `backend/Jarvis.Api/Data/Catalog.cs`.
  - Each strategy is linked to a basket and, for index strategies, an underlying. These links are guessed from the names, e.g. `B6_BK_STBT` → BASKET 06 / BANKNIFTY.
  - Signals pick a strategy that matches their symbol (SENSEX → `SX_STBT_WT`…), and positions use NIFTY strategies.
  - The filter dropdowns now list the full catalog with codes (`B6_BK · STRAT2635K589`).
  - The seeded fake data therefore differs from before (new random sequence).
- **Hub rails no longer detach when zooming:** after a browser zoom the rails could stay drawn for the previous layout, shifted sideways from the tiles. Redraws are now triggered by window resize, device-pixel-ratio change, visualViewport resize and every overview refresh, as well as ResizeObserver. They are measured from the SVG's own origin and applied inside Angular's zone so the view always repaints. Verified with a scripted live zoom (1600 → 1067 → 2400 px): every rail endpoint lands exactly on its tile, even with ResizeObserver disabled.
- **Overview hub tiles now resize with the screen:** the tile columns changed from a fixed 272 px to fluid (`minmax(240px, 1fr)`), with gutters that grow a little. Zooming out or using a wider screen widens the tiles instead of leaving empty margins, as on the real page.

## 2026-11-10

- **Realtime pipeline for every source** ([11](11-realtime-pipeline.md)).
  - Simulator and Fyers data now go through Redis into SQL Server, like the Delta data. A new feature only has to call `RealtimeBus.Publish(topic, key, data)`.
  - Redis keeps the latest value plus a stream; `RealtimePersister` saves to `dbo.RealtimeEvents` (SQL commit before Redis acknowledgement); data is pushed live over SignalR.
  - 10 topics (`sim.spot/pnl/event`, `fyers.pnl/funds/positions/orders/trades/holdings/quotes`) and typed SQL views (`vSimPnl`, `vFyersOrders`…).
  - New `FyersPoller` fetches Fyers every 10 s in the background (read-only) and publishes only changes.
  - New `/api/realtime` endpoints (status, latest, recent, history), the frontend `RealtimeService` (`latest/recent/history/live`), and a **Realtime Data** page in the sidebar.
  - Rows older than 7 days are purged.
  - Tested with simulator data end to end: Redis → SQL → views → API → SignalR. The Fyers path could not be tested live because the token had expired; the poller showed "token invalid" and kept retrying.
- **Demo simulator added** ([08](08-demo-simulator.md)).
  - The fake data now moves every 2 s: NIFTY price, option prices, per-user P&L, RISK flags, auto-closes, new entries and exits, in-flight exit stages, new signals, broker rejections, container load, and the overview's relay and success-rate numbers.
  - Five scenarios: Calm, Normal, Volatile, Rally, Crash.
  - A "Demo live" pill in the console header has a scenario picker, pause/resume and an event feed.
  - Positions, Performance and Infrastructure auto-refresh every 3 s, and Live Signals every 5 s.
  - P&L and LTP values flash green or red when they change.
  - Force-exiting a position now also records the exit order and closes the matching trade.
- **Redis disconnects explained:** WSL stops when idle, and Redis stops with it. The fixes are in [Troubleshooting](09-troubleshooting.md#redis-is-not-connected--keeps-disconnecting).
- **Documentation folder created:** this `docs/` folder.

## Earlier work (in order)

1. **Console clone:** Strategy Performance Console v2 in Angular 19 with a .NET 10 API and seeded fake data. Includes the Overview (hub, Recent Alerts, Subscription Watch, Broker Session Health) and Live Signals with the detail modal.
2. **Run instructions:** fixed a port conflict, and added a project `.npmrc` (`os=win32`) so esbuild installs correctly.
3. **Broker Live (Fyers):** a read-only Fyers v3 integration as its own sidebar page. Credentials are in user-secrets. Fyers rejections are merged into Recent Alerts ([05](05-broker-live-fyers.md)).
4. **Crypto Live (Delta):** live BTCUSD over the Delta WebSocket with no login, pushed to the browser via SignalR, with a candle chart. Added the `/hubs` proxy entry and a User-Agent header for Delta REST ([06](06-crypto-live-delta.md)).
5. **Storage:** Redis (in WSL) for real-time data and a trade stream, then MarketPersister, then SQL Server `JarvisMarket` (trades, 1m candles, ticker snapshots). Covers how to view Redis from the CLI and the VS Code extension ([07](07-storage-redis-sql.md)).
6. **More console tabs:** Overview v2 additions (Today's activity, Needs attention), Positions & Exits, Performance → Trading, Infrastructure ([03](03-console-pages.md)).
7. **Git author fixed:** commits now show Vijay Palleti.
8. **Overview hub polish:**
   - glowing and pulsing live tiles, with moving beads along the rails into each tile;
   - three-layer rails and a core with aura, dial and shimmer;
   - value-change flash and a click "launch" animation;
   - support for reduced motion;
   - sizes matched to the reference screenshot (tiles about 272 × 82 px, core 154 px).
9. **Live Signals improvements** ([04](04-live-signals.md)):
   - a status never says "completed" while quantity is still open;
   - a Needs attention filter, segmented progress bar, a single badge, the Entry → Exit stepper with copy, date groups, and a Compact view;
   - the modal gained a warning box with fix buttons, problems-first sorting, status chips, faded zeros and hidden empty columns, a chunk table with "why unfilled" and Retry chunk, and Exit remaining;
   - the relay pill was merged and the banner made dismissible;
   - noise fix: hedge retries that recovered no longer count as issues.
