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
- **The page loads every account in scope** (all 41 by default), so the Strategy P&L, Active accounts and Open positions cards are totals for **all** of them (tag "All 41"). The assistant's "total strategy P&L" is the same number, so you can check one against the other. Filters (basket, strategy, name, broker) narrow both the list and the cards.
- **Strategy filter** is a searchable multi-select (Clear / Apply). With strategies selected, *every* number covers only those strategies: KPIs, each user's R/U/total P&L, open/closed counts, the positions table and the trades. Accounts with none of them drop out of scope. Empty selection means all strategies.
- User list with total P&L split into realized (**R**) and unrealized (**U**).
- The selected user's detail: P&L card, positions table, trade history (expand a row for its correlation trace).
- P&L and LTP values flash green or red as they change.
- **⋮ on a user row → Manage User** opens that broker account's owner in User Management.

## Performance → User Performance: the selected user scopes every sub-tab

Pick a user once (in any sub-tab, or via **Trading → ⋮ → Manage User**) and they **stay selected** across Overview, Trading, User Management, Basket & Strategy and Subscriptions & Billing, as on the production console.
- **Tab row:** the user chip (**● Name #id ✕**) and the **ACCOUNTS** filter (*All N* or one broker account) show on every sub-tab. Each sub-tab shows a one-line description under the row.
- **✕ on the chip** clears the user: Trading shows everyone again, and the other sub-tabs show the user picker.
- **One shared state:** the user, filter, actions, dialog and toast live in `console/performance/user-workspace.service.ts`, so a change on one sub-tab (e.g. pausing a subscription) shows on all of them.

| Sub-tab | With a user selected |
|---|---|
| **Overview** (`user-overview.component.ts`) | Client header (status, #id, email, mobile, static IP) with quick links to Trading / User / Baskets / Billing.<br>Tiles: **Broker accounts** (trade-ready today = active + logged in + algo on), **Baskets held**, **Active subscriptions** (paused count), **Lifetime value** (sum of paid payments).<br>**Needs attention** across every section: inactive user, no static IP, account logged out / deactivated / algo off, subscription expiring within 15 days, expired or paused, payment pending or failed. Each item links to its tab; **Recheck** reloads.<br>Broker-account and subscription cards. |
| **Trading** | The user list and the cards show **only this user's OMS broker accounts** (or the one picked under ACCOUNTS). The scope text names the user. Uses `clients=` on `/api/console/performance/summary`. |
| **User Management** | As described below. |
| **Basket & Strategy** (`user-baskets.component.ts`) | "Client-scoped" banner (*Global Basket Management* is not in the demo).<br>**Baskets × broker accounts** grid: each cell is that account's subscription (status, multiplier ✎, ⏸ / ▶, ✕) or *Not assigned* with **＋ Assign**. **＋ Assign basket** at the top.<br>Expand a basket row to see its strategies and the dispatched CE/PE qty **per account**. |
| **Subscriptions & Billing** (`user-billing.component.ts`) | No production screenshot, so it's built in the same style.<br>Tiles: lifetime value, outstanding (pending or failed), active plans (monthly equivalent), next renewal.<br>Subscriptions with price (demo: ₹2,499 per month; 10 % off for 6 months, 20 % off for 12), **Renew +1 month**, **Pause / Activate**.<br>Payments (invoice, method, status) with **Mark paid**. A new subscription creates a pending payment. |

## Performance → User Management (`console/performance/user-management.component.*`)

Everything a system user **owns**. All of it is demo data; every change edits only the in-memory store and is lost when the API restarts.

- **Picker:**
  - Search by name, email, mobile or `#id`; about 155 user cards, shown 60 at a time with *Show more*.
  - Each card shows **Active / Inactive** and **No static IP** when the user has no public IP.
  - **Create user** adds a user.
  - You can also arrive here from **Trading → ⋮ → Manage User**, which finds the user who owns that broker account.
- **Tab row:** while a user is open, a chip (**● Name #id ✕**) and an **ACCOUNTS** filter (*All N* or one broker account) appear. The filter narrows broker accounts, subscriptions and baskets to that account.
- **Header:** avatar, name, status, email, mobile, user id, and **Switch user**. **Jump to** chips go to the sections, and **Collapse / Expand all** folds them. Every section can be folded on its own.
- **User Details:**
  - Buttons: **Edit user** (name, email, mobile), **Deactivate / Activate user**, and **IP configuration** (not available in the demo).
  - Fields: full name, email, mobile, role, status, user id, public static IP, private IP, username, referral code, created, last modified.
  - The static and private IPs are the same as the user's OMS container on Infrastructure.
- **Broker Accounts:**
  - Columns: broker, client id, username, session (*Logged in* follows the OMS session), lot, algo on/off, account active.
  - Actions: **Lot** (0–100; 0 = use the basket setting), **Algo on / off**, **Deactivate / Activate**.
  - **Login** and **Credentials** need a real broker connection, so they only show a message.
- **Basket Subscriptions:**
  - Columns: basket (with its strategy count), plan and length, broker account, multiplier, period, days left, and status (Active / Paused / Inactive / Cancelled / Expired).
  - Actions: **Edit** (multiplier), **Extend** (+1 month), **Pause / Activate**, **Cancel** (asks first), and **Add subscription** (basket, broker account, 1–12 months, multiplier).
- **Baskets:**
  - One card for each subscribed basket, with this user's multiplier and **Change**.
  - Strategy table: lot size, strategy multiplier, CE and PE leg values, product, and **Dispatched qty**.
  - **Dispatched qty** = `user multiplier × lot size × strategy multiplier × leg value`, truncated (2.9 → 2). A stored 0 means "unset" and falls back to 1. A leg value of 0 means that leg isn't traded (shown as —).
  - Lot size and strategy multipliers are basket-wide, so they're read-only here.
- **API:** `/api/console/users`. See [10-api-reference.md](10-api-reference.md). Data and rules are in `backend/Jarvis.Api/Data/MockDataStore.Users.cs`. Users are seeded last with their own random generator, so the existing demo data doesn't change.

## Basket Management → Basket Subscriptions (`pages/basket-subscriptions/`, route `/admin/basket-subscriptions`)

Every subscriber's basket subscriptions in one list, to monitor them and to **extend** them. Same demo data as User Management, so a change on one page shows on the other.

- **Sidebar:** **Basket Management** is now a real sub-menu: Basket Subscriptions, plus Basket Configuration, Basket Console (New) and Split / Freeze Limits (those three are placeholders). The group opens by itself when one of its pages is showing.
- **Header:** **Export CSV** (every row matching the current filters and status tab) and **Create Subscription** (search a subscriber who has a broker account → broker account, basket, plan length, multiplier; it creates a pending payment).
- **Filters** (collapsible, with Reset / Apply): subscriber, user ID, broker account or plan; basket; broker; expiring within 7 / 15 / 30 days.
- **Status tabs with counts:** All, Active, Inactive, **Expired** (an active plan whose end date has passed), Paused, Cancelled.
- **Table columns:** subscriber and user ID, basket (*Internal basket*), plan (length and price), multiplier, start / end, status with days left (or "ended N day(s) ago"), broker and account.
- **Actions on each row:**
  - **Pause / Activate** (activating an ended plan restarts it from today for one plan length);
  - **Edit** the multiplier;
  - **Extend** by 1 / 3 / 6 / 12 months, with a preview of the new end date. An already-ended plan is extended **from today**;
  - **Remind** (demo: shows a message, nothing is sent);
  - **Delete** (asks first).
- **Paging:** First / Previous / 5 page numbers / Next / Last, Total, and 10 / 20 / 50 / 100 items per page. Newest start date first.
- **Demo data:** about 120 subscriptions, including expired and cancelled ones. The Overview's *Subscriptions ending in 7 days*, *Subscription Watch*, the Subscriptions Track tile and the assistant all read these same subscriptions, so they always agree with this page. Prices are demo prices (₹2,499 per month; 10 % off for 6 months, 20 % off for 12), not the production plan prices.

## Infrastructure (`console/infrastructure/`)

Container health dashboard: running and stopped counts, health, heartbeat, CPU and memory, Redis and Mongo latency, stream messages, restarts, IP and NIC routing checks. Each container has Restart and Stop actions, plus Provision all, Stop all and Reload contract master. Auto-refresh every 3 s.

## Baskets and strategies

The names come from production and live in `backend/Jarvis.Api/Data/Catalog.cs`:
- 19 baskets, some with descriptions;
- 75 strategies, each with its code (e.g. `STRAT1510A589`), a basket and an underlying (NIFTY / BANKNIFTY / SENSEX / MCX / CRYPTO, or none).

Fake signals and positions only use the index strategies, and always one matching the symbol's underlying. The strategy → basket links are guessed from the names; edit `Catalog.cs` if the real mapping differs. To add a basket or strategy, add a line there; the filters, signals and accounts pick it up on the next API restart.

## Not cloned yet

Basket Configuration, Basket Console (New), Split / Freeze Limits, Logs & Audit, Alerts, Trading Calendar, Strategy Cache, Queue Monitor, Rejected Signals, Send Signal, the Strategy / User Performance toggle above the Performance sub-tabs, and the other sidebar pages. These show a placeholder.
