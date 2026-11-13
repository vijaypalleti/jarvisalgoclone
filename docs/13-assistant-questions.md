# 13. Assistant question catalogue

Everything you can ask **Jarvis Assistant** (the ✦ chat button at the bottom right). Ask in plain English; the exact wording doesn't matter. Each section lists example questions and the tool that answers them.

- How it works and how to add tools: [12-ai-assistant.md](12-ai-assistant.md)
- ✅ = tested with real answers · 🔒 = needs a live Fyers session (Broker Live → Login with Fyers / Renew with PIN)
- The assistant is **read-only**: it explains and reports, but never places, modifies or exits orders.

**Which accounts are which**
- **"Vijay"**, "my account", "me" or "Fyers" = your **real Fyers account**.
- **Client ids** (1023, 8616, 1360…) = the demo OMS book. **User ids** from User Management ("User #8606") work too: "pnl of 8606" → Lakshmi Nair = OMS client 1360. Account lookups also accept "client 1023", "broker account #1023", the broker account number (e.g. 52062393), a person's name ("Kavya Nair") or a broker ("ANT").

---

## 1. Market prices

| Ask | Tool | Notes |
|---|---|---|
| ✅ What is the current BTC price? | `get_btc_price` | Live Delta feed. USD: last, mark, 24h change/high/low, funding, OI |
| What's the BTC funding rate / open interest / best bid and ask? | `get_btc_price` | |
| ✅ How much has BTC moved in the last 4 hours? | `get_btc_change` | From stored 1-min candles (SQL); 0.25–168 h |
| What were BTC's high and low today? | `get_btc_change` | e.g. "in the last 24 hours" |
| ✅ Where are NIFTY, BANK NIFTY and SENSEX right now? | `get_market_indices` | 🔒 real Fyers quotes; otherwise only the **simulated** NIFTY (it says so) |
| Is NIFTY up or down today? | `get_market_indices` | |
| ✅ What is the current price of Reliance? | `get_stock_quote` | 🔒 Any NSE/BSE stock: last price, change, change %, open, high, low, previous close, volume. Names, nicknames ("SBI", "L&T", "Zomato") or symbols. Outside market hours: the last session's prices |
| ✅ Price of TCS, Infosys and HDFC Bank | `get_stock_quote` | Several stocks in one question (one Fyers call) |

## 2. Vijay's real Fyers account 🔒

| Ask | Tool | Notes |
|---|---|---|
| ✅ What is Vijay's current P&L? | `get_fyers_account` | Realized, unrealized, total |
| What are my available funds / margin? | `get_fyers_account` | Fund lines with non-zero values |
| What positions am I holding? | `get_fyers_account` | Up to 20 open positions with P&L |
| What's the P&L on my holdings? | `get_fyers_account` | Holdings count, P&L, market value |
| How many orders did I place today, by status? | `get_fyers_account` | Counts per status |
| ✅ Show my rejected Fyers orders today | `get_fyers_orders_today` | With the broker's rejection message |
| Do I have any pending orders? | `get_fyers_orders_today` | `pending` covers PENDING and TRANSIT |
| Show my completed / cancelled orders | `get_fyers_orders_today` | |
| How did my P&L move since this morning? | `get_pnl_history` | `fyers.pnl` history; recorded on change while the API runs |

If the session has expired, the answer says so and tells you how to log in again.

## 3. Broker accounts (demo OMS book)

| Ask | Tool | Notes |
|---|---|---|
| ✅ What is the status of broker account 1023? | `find_broker_account` | Active, session/token, broker, basket, positions, P&L, container, issues |
| ✅ What is the P&L of client 8616? | `find_broker_account` | Realized, unrealized, total |
| Is the session valid for 1360? Is its token stale? | `find_broker_account` | |
| Which basket is 1023 in? How many strategies does it run? | `find_broker_account` | |
| Show accounts of Kavya / all ANT accounts | `find_broker_account` | Name or broker search (max 5 matches) |
| ✅ How did 1023's P&L move in the last hour? | `get_pnl_history` | Start → now, change, high/low with times (IST); max 24 h |
| How has the whole book's P&L moved today? | `get_pnl_history` | Omit the account → sum of all accounts |

## 4. Positions and exposure

| Ask | Tool | Notes |
|---|---|---|
| ✅ How many positions are open right now? | `get_positions` | Open, closed and at-risk counts |
| ✅ How many positions are open in 1023? | `get_positions` | |
| Show the closed positions of 1023 | `get_positions` | `status=closed` |
| Which positions are at risk (down more than 20%)? | `get_positions` | `status=risk`, worst first |
| Show open positions of strategy S1EN | `get_positions` | Filter by strategy |
| ✅ What is our net exposure to NIFTY 27350 PE? | `get_symbol_exposure` | Net/gross qty, short or long, accounts, avg, LTP, unrealized, per expiry |
| How much 25300 CE are we short across all accounts? | `get_symbol_exposure` | "25300 CE", "27350 PE" or full symbols |
| What are our biggest exposures? | `get_symbol_exposure` | No symbol → largest by gross qty |
| ✅ Which short positions are not hedged? | `get_unhedged_positions` | NAKED (no legs) and PARTIAL (one leg missing) with the missing qty |
| Is 1 fully hedged? | `get_unhedged_positions` | For one account |

## 5. Signals and execution

| Ask | Tool | Notes |
|---|---|---|
| What is the status of signal 45402? | `get_signal` | Status, exit status, fills, residual qty, issues |
| What happened to the latest NIFTY17NOV26C25300 signal? | `get_signal` | Latest for a symbol |
| ✅ Which accounts didn't get signal 506, and why? | `get_signal_accounts` | Rejected and partial accounts with reasons, plus qty still open after exit |
| Why did 1360 miss signal 506? | `get_signal_accounts` | |
| Show all accounts for signal 506 | `get_signal_accounts` | "all", not just problems |
| ✅ Show the latest 3 signals that need attention | `get_recent_signals` | Filters: attention / open / exited / all, plus symbol or strategy |
| What are the most recent S1EN signals? | `get_recent_signals` | |
| Which signals are exit-partial and how much qty is stuck? | `get_recent_signals` / `get_open_issues` | |
| ✅ How many signals came in today, and what's the fill rate? | `get_today_activity` | Signals per hour, orders, filled, rejected, working |
| What was the busiest hour today? | `get_today_activity` | |

## 6. Issues, risk and operations

| Ask | Tool | Notes |
|---|---|---|
| ✅ How many issues need solving right now? | `get_open_issues` | Total, broken down: signals needing attention, broker rejections, cleanup, in-flight exits, containers |
| What are the worst problems right now? | `get_open_issues` | Includes the 3 worst signals |
| ✅ Why are orders getting rejected on ANT? | `get_broker_rejections` | By reason (margin shortfall, RMS, price band…) and by broker |
| Which accounts have the most open rejections? | `get_broker_rejections` | |
| ✅ Any stuck exits or cleanup needed for client 1360? | `get_exits_and_cleanup` | In-flight exits by stage (oldest first) and cleanup items |
| What needs cleanup across the book? | `get_exits_and_cleanup` | Qty mismatch, orphan, stale, hedge leg left open |
| Which accounts have invalid sessions? | `get_open_issues` | `sessionsInvalid` count |

## 7. Performance and rankings

| Ask | Tool | Notes |
|---|---|---|
| ✅ Give me a portfolio summary | `get_portfolio_summary` | Accounts, positions, total P&L, top 3 gainers and losers, simulated NIFTY |
| ✅ Top 5 losing accounts | `get_rankings` | `pnl_bottom` |
| Top 5 winning accounts | `get_rankings` | `pnl_top` |
| Who has the most open positions / most at-risk positions? | `get_rankings` | `open_positions` / `at_risk` |
| ✅ What is the total strategy P&L? | `get_strategy_pnl` | Grand total of **all** strategies and accounts (= "all N" on the Performance card, not "This page") |
| ✅ Which strategy is making the most money, and which is worst? | `get_strategy_pnl` | All strategies ranked |
| How is S1EN doing? | `get_strategy_pnl` | One strategy: P&L, positions, best and worst accounts, basket, code |
| ✅ How is BASKET 06 doing? | `get_basket_summary` | Accounts, open positions, P&L, strategies in the basket |
| P&L by basket | `get_basket_summary` | All baskets ranked |

## 8. Platform health

| Ask | Tool | Notes |
|---|---|---|
| ✅ Is any container down? | `get_infrastructure` | Running/stopped, unhealthy, no heartbeat, average CPU and memory |
| ✅ Is the data pipeline healthy? | `get_infrastructure` | Redis, SQL persisters (last flush, errors) |
| Is the BTC feed connected? | `get_infrastructure` | Delta feed status, last message, reconnects |
| Is the Fyers poller working? | `get_infrastructure` | Poller state and last error |
| Whose subscriptions expire this week? | `get_expiring_subscriptions` | Any number of days (default 7) |

## 9. Help and how-to

| Ask | Tool | Notes |
|---|---|---|
| ✅ What does exit partial mean? | `get_help` | Built-in glossary |
| ✅ What does DLQ mean? | `get_help` | Also: residual, needs attention, chunk, dead letter, in-flight exit, cleanup, hedge, risk, relay, scenario |
| ✅ How do I renew my Fyers session? | `get_help` | Click-by-click UI steps |
| How do I log in to Fyers? | `get_help` | |
| How do I exit the remaining qty of a signal? / How do I force exit? | `get_help` | |
| How do I use the filters / presets? | `get_help` | |
| How do I change the simulator scenario? | `get_help` | |
| How does the Redis → SQL pipeline work? | `get_help` | Answers from `docs/*.md` (best-matching sections) |

## 10. Show me on screen (navigate + highlight)

The assistant opens the right console tab, clears filters that would hide the item, scrolls to it and **pulses it with a red ring and a "Jarvis: …" label** for about 12 s (click it to dismiss). **Show on page** under the answer repeats it.

| Ask | Tool | What happens |
|---|---|---|
| ✅ Point the container which is not working | `show_on_screen` (`containers_down`) | Infrastructure tab; every stopped / unhealthy / no-heartbeat container highlighted, first one scrolled into view. If none are down, it highlights degraded ones; if all are healthy, it just says so |
| Show me container oms-client-8631 / the container of 1023 | `show_on_screen` (`container`) | That container card |
| ✅ Show me signal 506 | `show_on_screen` (`signal`) | Live Signals filtered to that signal (correlation id + its day), row highlighted |
| ✅ Take me to the positions of client 1023 | `show_on_screen` (`account`) | Positions & Exits filtered to 1023, account selected and highlighted |
| ✅ Navigate to Mohan Saxena's subscription status | `show_on_screen` (`subscription`) | **Basket Subscriptions** filtered to that subscriber (name, user ID or broker account), their rows highlighted with basket · status · days left; the answer states the status |
| ✅ Open NIFTY50 chart / open BTC chart / open SENSEX live chart / ✅ open Reliance chart | `show_on_screen` (`chart`) | **Market Charts** on that symbol (NIFTY 50, BANK NIFTY, SENSEX, BTCUSD or **any NSE/BSE stock**), chart highlighted. Add a timeframe: "open SENSEX 15m chart" (1m / 5m / 15m / 1h / 1d) |
| ✅ Navigate to the subscriptions expiring within a week | `show_on_screen` (`subscriptions_expiring`) | **Basket Subscriptions** on *Active* with **Expiring: within 7 days** set in the filter panel, every matching row highlighted. "in 15 days" etc. also work (1–90) |

It only *shows*: it never restarts, stops or exits anything. Typos are fine ("naviagate", "subcriptions"). When you say *navigate / show me / take me to / open / point / highlight*, the assistant is told it must open the page, not just answer in text. When several table rows are highlighted, one label sits on the top row; hover any row for its own details. By voice: *"Jarvis, show me the container that's down"*; the screen un-dims so you can see the highlight.

## 11. Follow-ups and combinations

The last 12 messages are sent with each question, so you can ask follow-ups:

- ✅ "What is the P&L trend of 1023 in the last hour?" → "and how many open positions does it have?"
- "Top 5 losing accounts" → "why is 419 losing?" → "show its open positions"
- "Which accounts didn't get signal 506?" → "is 1360's session valid?"

One question can use several tools: *"Give me BTC price and the portfolio P&L"*, *"How many issues are there, and is any container down?"*

## What it will decline or can't answer

| Ask | Reply |
|---|---|
| ✅ "Exit all positions of 1023", "place a buy order" | Refused: read-only. It points you to the console page |
| Data that isn't in the app (news, other brokers' live data, forecasts) | It says it doesn't have that data and won't invent numbers |
| Fyers questions while the session has expired 🔒 | It says the session expired and how to log in again |

## Asking by voice

Every question here can also be **spoken**: say *"Jarvis, …"* (wake word on) or press 🎙. Keep it short and specific, e.g. *"Jarvis, top five losing accounts"*. The answer is shown and read aloud; long tables are read as their first rows, with the full table on screen.

## Tips

- **Be specific:** "open positions in 1023" works better than "positions?".
- **Give ids:** signals (506, 45402) and clients (1023) are found instantly.
- **Free-tier speed:** on Groq's free tier, asking more than ~3 questions a minute adds a few seconds of waiting; nothing fails.
- **Check numbers:** the tool chips under each answer show where the numbers came from (hover to see the arguments). Money amounts are checked against the tool data automatically.

---

*Keep this file in sync with the tools: when you add a tool in `AssistantTools.cs`, add its questions here (see [12-ai-assistant.md → Adding a tool](12-ai-assistant.md#adding-a-tool)).*
