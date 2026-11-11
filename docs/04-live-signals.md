# 4. Live Signals & Lifecycle

Code: `frontend/jarvis-ui/src/app/console/signals/` (list and detail modal); backend in `ConsoleControllers.cs` (`/api/console/signals`) and `MockDataStore.cs`.

## The problem this section solves

In the real system, a signal could say **"exit completed"** while one client account still held quantity at the broker. The last exit chunk had been marked COMPLETED with 0 filled and no broker order id. You only found out by opening the signal and scrolling through the table.

**The rule now:** a signal's status follows its *worst* account. If any quantity is still open, it is never shown as completed.

## Signal health (computed on the backend)

| Health | Meaning | Badge |
|---|---|---|
| `residual` | Exit was sent, but some account still holds quantity | ⚠ amber "Exit partial · N residual" |
| `error` | Unresolved rejected, failed or partial accounts, or contradictory data | ✕ red "… · N issues" |
| `open` | Entry done, exit not sent yet | ● blue "Open" |
| `ok` | Fully exited | ✓ green "Exited" |

- The status the OMS originally recorded is kept as `recordedExitStatus`. The modal shows it crossed out: "OMS recorded: ~~exit completed~~".
- `unresolvedIssues` counts only problems that still need action. Hedge retries that recovered are **not** counted.

## The signal list

- **Needs attention** is the first quick filter. It shows only `residual` and `error` signals.
- **Segmented progress bar:** green = exited, blue = filled but still open, red = never filled, as a share of the requested quantity. Hover for exact numbers.
- **One badge** per signal, instead of the four separate tags it used to show.
- **Lifecycle steps:** `✓ Entry → ✓ Exit` (or ⚠ partial, or … pending). Click a correlation ID to copy it.
- **Date headings** (Today, Yesterday, then dates) when sorted by time.
- **Cards / Compact** toggle. Compact shows one line per signal; your choice is remembered in the browser.
- Filters (date, symbol, correlation, client, basket, strategy) can be saved and loaded as a preset. Strategy is a multi-select (search, codes, Clear / Apply), and the list shows signals from any selected strategy.
- Auto-refresh is on, every 5 s.

## The detail modal (click a signal)

1. **Warning box** at the top: "⚠ N accounts need attention". Each row lists the problem and has an **Exit remaining** button.
2. **Status chips with counts** (e.g. Rejected 4 · Partial 1 · Completed 19) replace the old dropdown.
3. **Problems first:** rows are sorted by severity, and the worst one opens automatically.
4. **Easier to read:**
   - zeros are faded;
   - columns that are empty on the current page are hidden ("Show all columns" brings them back);
   - the header row and client column stay in place while you scroll.
5. **Chunk table** (in the expanded row) shows, for each exit chunk:
   - requested and filled quantity, with a small fill bar;
   - broker status vs OMS task status;
   - **why it didn't fill**;
   - a **Retry chunk** button.

   A ⚠ marks contradictions, such as "COMPLETED but 0 filled" or "EXECUTED but no broker order".
6. **Copy buttons** on order IDs and account numbers.
7. **Actions:**
   - **Force exit** (whole signal);
   - **Exit remaining** (one account);
   - **Retry chunk** (one chunk).

   Each asks for confirmation and then reloads.

## Endpoints

| Method | Route |
|---|---|
| GET | `/api/console/signals?filter=attention…`: list (`attentionCount` included) |
| GET | `/api/console/signals/{id}?status=&page=`: detail, with `attention`, `statusCounts` and severity-sorted accounts |
| POST | `/api/console/signals/{id}/force-exit` |
| POST | `/api/console/signals/{id}/accounts/{clientId}/exit-remaining` |
| POST | `/api/console/signals/{id}/accounts/{clientId}/chunks/{n}/retry` |

## Try it

With the [demo simulator](08-demo-simulator.md) running, about 1 in 8 new exits leaves a stuck chunk. Filter **Needs attention**, open the signal, and press **Retry chunk** or **Exit remaining**. The badge turns green.
