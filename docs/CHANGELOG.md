# Changelog

Newest first. Add a line here whenever something changes (see [README](README.md#keeping-these-docs-up-to-date)).

## 2026-11-12

- **Assistant can open and point at subscriptions:**
  - "Navigate to Mohan Saxena's subscription status" → Basket Subscriptions filtered to him, his row highlighted ("MCX Full Access Basket — Active · 3 days left"), and the status in the answer.
  - "Navigate to subscriptions expiring within a week" → Basket Subscriptions on Active + *Expiring within 7 days*, all 4 rows highlighted.
  - **How:** new `show_on_screen` targets `subscription` / `subscriptions_expiring`; Basket Subscriptions reads its filters from the URL; rows are tagged `sub:<id>`.
  - **Navigation is forced:** navigation wording (typos included) now makes the assistant open the page. In testing it had once only answered in text.
  - **Highlighting rows:** several highlighted table rows get one summary label instead of overlapping labels, and the page scrolls to the topmost one.
  - **Verified** with your exact wording (typos included) through the real chat, three times in a row.
- **Fix: expiring subscriptions now agree everywhere.**
  - **The problem:** the Overview's "Subscriptions ending in 7 days", the Subscriptions Track tile, Subscription Watch and the assistant's `get_expiring_subscriptions` read an older list of 10 random subscriptions (random names, e.g. a "Kavya Menon" with 1 day left), separate from the users' real subscriptions. So the Basket Subscriptions page didn't show them.
  - **The fix:** that list is now derived from the users' active subscriptions (`MockDataStore.ExpiringSubscriptions(days)`).
  - The old random calls are kept so every other piece of demo data (accounts, positions, P&L) stays identical.
  - **Verified:** the Overview list and Basket Subscriptions (Active, expiring ≤ 7 days) return the same 4 rows; account data is identical to the running API.
- **Basket Management → Basket Subscriptions** (`/admin/basket-subscriptions`, cloned from the production screen):
  - Every subscriber's subscriptions in one list: status tabs with counts (All / Active / Inactive / Expired / Paused / Cancelled), collapsible filters (search, basket, broker, expiring within N days), paging and items per page.
  - **Actions:** pause / activate, edit multiplier, **extend by 1–12 months** (with a preview of the new end date; ended plans restart from today), remind (demo), delete.
  - **Header buttons:** Export CSV and Create Subscription.
  - **Sidebar:** Basket Management is now a real sub-menu.
  - **Backend:** `GET /api/console/basket-subscriptions`, extend `?months=`, delete subscription. The seed data now includes expired and cancelled subscriptions.
  - **Verified in headless Chrome:**
    - 119 subscriptions with correct tab counts; paging works;
    - extended an expired plan by 3 months → it became active (the Expired count went from 13 to 12);
    - pause → shown under Paused; edit multiplier to 3×;
    - the "expiring within 15 days" filter showed 17;
    - created and deleted a subscription; remind and Export CSV;
    - no browser errors.
- **The selected user now scopes every Performance sub-tab**, as on the production console.
  - The user chip and the ACCOUNTS filter stay in the tab row across Overview, Trading, User Management, Basket & Strategy and Subscriptions & Billing.
  - **Trading** shows only that user's broker accounts (new `clients=` filter on the performance summary).
  - **New sub-tabs:**
    - **Overview:** client header with quick links, 4 tiles and **Needs attention** across every section.
    - **Basket & Strategy:** the client-scoped banner and a baskets × broker accounts grid with per-cell multiplier, pause / activate / cancel and assign, plus expandable per-account dispatch.
    - **Subscriptions & Billing:** plans with prices, renewals, payments, Mark paid, lifetime value.
  - **Backend:** payments (seeded; new subscriptions create a pending payment), plan prices, and an `attention` list in the user workspace.
  - **Code:** the state moved into `UserWorkspaceService`, shared by all sub-tabs.
  - **Verified end to end in headless Chrome:**
    - picked a user on Overview → Trading showed only their 2 accounts → ACCOUNTS filter narrowed it to 1;
    - assigned a basket from the grid → the pending payment showed under Billing → Mark paid;
    - User Management showed the new subscription;
    - ✕ restored all 41 accounts on Trading. No browser errors.
- **Performance → User Management** (cloned from the production screens, with fake data):
  - A user picker with search and Create user.
  - A user workspace with collapsible **User Details**, **Broker Accounts**, **Basket Subscriptions** and **Baskets** sections (including dispatched qty), Jump-to chips, a user chip and an ACCOUNTS filter in the tab row.
  - **Trading → ⋮ → Manage User** opens the owner of a broker account.
  - Actions (edit and activate users, lot, algo on/off, pause / activate / extend / cancel subscriptions, multipliers, add a subscription) change only the in-memory demo data. Broker login, credentials and IP configuration show "not available in the demo".
  - New `Data/MockDataStore.Users.cs` (about 155 users; every OMS account has an owner; `example.com` emails) and `Controllers/UsersController.cs`.
  - **Verified:**
    - Every endpoint and validation works (an invalid lot returns 400).
    - The UI was driven end to end in headless Chrome: picker, search, workspace, account filter, pause, change multiplier, demo-only message, and ⋮ → Manage User. No browser errors, in light and dark mode.
  - **Fixed while testing:** the route parameter was named `{action}`, which ASP.NET routing reserves, so the route never matched; it's now `{op}`.
- **Performance page now shows all accounts (41, not 25):** `/api/console/performance/summary` defaults to every account in scope. The Strategy P&L, Active accounts and Open positions cards therefore cover the same accounts as the assistant, so the two numbers can be checked against each other. The "This page" tags became "All N". Verified: card P&L ₹7,728.50 = the assistant's total on the same data.
- **Fix: strategy P&L differed between the assistant and the Performance page.**
  - **Page:** the big number is only the **25 accounts on the page** (41 are in scope). The card now also shows **"all 41: ₹…"**, via a new `scopePnl` field in `/api/console/performance/summary`.
  - **Assistant:** `get_strategy_pnl` returned only the top 8 strategies, and the model added those 8 rows up itself, giving a wrong "grand total". The tool now returns `grandTotal` / `grandRealized` / `grandUnrealized` for **all** strategies and accounts. The system prompt also forbids adding rows to make totals.
  - **Verified:** on a test instance, the assistant's total (₹7,728.50) matched the page's all-accounts total exactly.
- **Chat panel redesign:** the assistant panel now matches the console's look.
  - **Header:** dark (same as the app header) with an online status dot and the model name. The emoji buttons are replaced by proper icons: wake word, new conversation, expand / compact, close.
  - **Welcome screen:** greeting plus suggestions grouped as Markets, Accounts, Operations and Platform.
  - **Messages:** the assistant's answers have an avatar and cleaner tables. A **Sources** row shows friendly tool names (e.g. "Broker account"; hover for the raw tool and arguments). Each message shows the time and duration, and gets **Copy** and **Show on page** buttons.
  - **Expand:** widens the panel to 760 px and full height.
  - **Composer:** grows as you type, with mic and send icons and a read-only note.
  - **Fix:** answer formatting (paragraph spacing, table lines, code) was never applied, because the answer HTML is inserted with `innerHTML` and Angular's style scoping didn't reach it. It now uses `::ng-deep`.
  - Checked in light, dark and expanded views.

## 2026-11-11

- **`docs/sql/useful-queries.sql`:** ready-made queries for every table and view (BTC, Fyers, simulator, AI log), most run in under 0.5 s. Grouping queries over the JSON views can take about a minute while SQL Express is short of memory (see troubleshooting). `07-storage-redis-sql.md` now explains how to open the data in SSMS.
- **Assistant can point at things on screen:** new tool `show_on_screen` + `UiPointerService`. "Point the container which is not working" opens Console → Infrastructure, clears filters, scrolls to the stopped / unhealthy / no-heartbeat container(s) and pulses them with a "Jarvis: oms-client-8631 — Stopped · no heartbeat" label. Also works for signals ("show me signal 506") and accounts ("take me to 1023's positions"). Answers get a 📍 show again button; in voice mode the screen un-dims to show the highlight.
  - Verified on a test instance with one container stopped: all three questions navigated to the right tab and highlighted the right item, fully in view.
- **Voice assistant ("Jarvis …"):** an opt-in wake word (◎/◉ in the chat header) and a 🎙 push-to-talk button.
  - Saying "Jarvis" moves the orb smoothly to the bottom-centre, dims and blurs the screen, and raises a Siri-style animated glow from the bottom that follows the mic level.
  - Shows your words live, then shows the answer in a glass card and speaks it (browser text-to-speech, with rupees, dollars and percent spoken naturally).
  - Esc, a click or the orb closes it, and it returns to listening for "Jarvis".
  - Chrome and Edge only for recognition; audio goes to the browser's cloud speech service while listening (hence opt-in).
  - Tested with a scripted recognizer, a fake mic and the real API: wake → "Listening…" with live transcript → real answer shown and spoken → back to wake mode.
- **Animated orb on the assistant button:** the floating button and chat header use the supplied orb animation. It was converted from a 5.6 MB GIF to a 128 px animated WebP (615 KB), with a GIF fallback and a still frame when the OS asks for reduced motion. Files: `frontend/jarvis-ui/public/assistant/`.
- **Groq key pool + model fallback for the assistant:** set up to 4 or more keys (`Groq:ApiKeys:0..N`). They're tried round-robin; a rate-limited key is parked until Groq's retry time, an invalid key for 1 h. When the primary model is exhausted, the same keys are tried on fallback models (`gpt-oss-120b`, `llama-3.3-70b-versatile`), each with its own allowance. The chat shows "via …" when a fallback answered, and `/api/assistant/status` shows per-key health.
  - Found while testing: the real limit hit was Groq's **daily** 200,000 tokens/day for gpt-oss-20b, which is **per organization**, so keys from one account share it.
  - Verified: invalid key → parked; 20B over its daily limit → answers came from gpt-oss-120b in about 1.2 s with correct data.
- **Question catalogue for the assistant:** [13-assistant-questions.md](13-assistant-questions.md) lists every kind of question, grouped into 10 areas, with the tool that answers it and notes. Tested questions are marked ✅.
- **5 more assistant tools** (24 in total): `get_signal_accounts` (who didn't get a signal and why), `get_symbol_exposure` (net exposure per strike, e.g. "27350 PE"), `get_unhedged_positions` (naked or partial hedges), `get_fyers_orders_today` (real Fyers orders and rejection messages), `get_help` (glossary, UI how-tos and the docs folder).
  - Tested with Groq: signal 506 → 6 margin-shortfall rejections listed; 27350 PE → net short 390 across 5 accounts; 14 of 24 shorts not fully hedged; "renew Fyers" → click-by-click steps; Vijay's real Fyers account answered live.
- **12 more assistant tools** ([12](12-ai-assistant.md)), making 19 in total: P&L history (SQL), rankings, strategy P&L, basket summary, infrastructure and pipeline health, broker rejections, in-flight exits and cleanup, recent signals, market indices, BTC change (candles), today's activity, expiring subscriptions.
  - **Keyword routing** sends each question only the relevant tool schemas, which saves tokens on Groq's free tier.
  - Account lookup now understands "client 1023" or "broker account #1023".
  - The chat shows more suggested questions.
  - Tested with real Groq calls: every new tool returned correct answers (e.g. P&L history −₹6,101.55 → −₹22,928.75 = −₹16,827.20, fill rate 2,699/2,832 = 95.3%), and a follow-up ("and how many open positions does it have?") kept the account context.
- **AI assistant (floating chat)** ([12](12-ai-assistant.md)): a ✦ button on every page opens Jarvis Assistant. Plain-English questions are answered by GPT-OSS 20B on Groq using 7 read-only tools: BTC price, Vijay's real Fyers account, broker account status, positions, open issues, portfolio summary, signal lookup.
  - The key is in user-secrets (`Groq:ApiKey`). It handles Groq's free-tier rate limit by waiting and retrying.
  - Money is pre-formatted, and every ₹/$ amount in an answer is checked against the tool data (one automatic correction).
  - Questions are logged to the `ai.chat` topic.
  - Tested with real Groq calls: BTC price, open positions (all / client 1023), status of 1023, open issues with breakdown, portfolio summary and client P&L all gave correct numbers (arithmetic verified). "Vijay's P&L" correctly said the Fyers session had expired. "Exit all positions" was refused (read-only).
- **Signal rows show the date:** each Live Signals row now shows the date (`11 Nov 2026`) above the time (`15:29:21.000`). Before, only the time was shown, which was ambiguous when scrolling back through earlier days. Hover for the full timestamp.
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
