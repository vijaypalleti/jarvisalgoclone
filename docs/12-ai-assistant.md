# 12. AI assistant (floating chat)

The round **✦** button at the bottom right of every page opens **Jarvis Assistant**. You ask in plain English, and it answers from the app's live data using **tool calling** with **GPT-OSS 20B on Groq** (`openai/gpt-oss-20b`).

```
Chat panel (Angular) ──POST /api/assistant/chat──► AssistantService (.NET)
                                                       │  question + tool list
                                                       ▼
                                                    Groq  ── "call find_broker_account {query:'1023'}"
                                                       │
                     AssistantTools runs it on our data ◄┘   (OMS store, Delta feed, real Fyers account)
                                                       │  result
                                                       ▼
                                                    Groq  ── final answer ──► chat bubble + "🔧 tool" chips
```

## Chat panel

Click the orb at the bottom-right.
- **Header buttons:** wake word (sound-wave icon, green when on), new conversation, expand / compact (760 px wide, full height), close.
- **Empty chat:** suggestions grouped by Markets, Accounts, Operations and Platform.
- **Under each answer:**
  - a **Sources** row naming the tools used (hover a chip for the raw tool name, arguments and time);
  - **Copy**;
  - **Show on page**, when the answer pointed at something;
  - the time, how long the answer took, and "via …" when a fallback model answered.
- **Keys:** Enter sends, Shift+Enter adds a new line. The chat is kept for the browser tab (sessionStorage).

Styles are in `shared/assistant-chat.component.ts`. Answer HTML is inserted with `innerHTML`, so its rules use `::ng-deep`. Friendly tool names are in `TOOL_LABELS`; add one when you add a tool.

## Show on screen (navigate + highlight)

"Point the container which is not working", "show me signal 506", "take me to 1023's positions".

**How it works**
1. Tool `show_on_screen(target, id)` in `AssistantTools.cs` (targets `containers_down`, `container`, `signal`, `account`, `subscription`, `subscriptions_expiring`, `chart`) finds the item(s) and sets a `UiAction { route, query, targets[{key,label}], focus }`.
2. `AssistantService` returns it as `action` in the chat response (still read-only: it only describes what to show).
3. `shared/ui-pointer.service.ts` (`UiPointerService.run`) navigates to `route?query` (e.g. `?tab=infrastructure`), publishes `request` so the tab can reset filters / select the item (`focus`), waits up to 6 s for `[data-ai-target="<key>"]`, scrolls the first into view and adds the `.ai-point` pulse + a floating `.ai-point-tag` label (styles in `styles.scss`). It clears after 12 s or when you click the item.
4. In voice mode the dim layer is removed while something is highlighted.

**Targets on the page:**
- `container:<name>`: Infrastructure cards and table rows.
- `signal:<id>`: Live Signals rows.
- `account:<clientId>`: the Positions & Exits account list.
- `sub:<subscriptionId>`: Basket Subscriptions rows.
- `chart:<symbol>`: the Market Charts chart card.

**Filters by URL:** Basket Subscriptions reads its filters from the URL (`?search=&basket=&broker=&expiring=7&status=active`), so the assistant's link opens it already filtered. Those links can also be bookmarked or shared.

**Forcing navigation:** if the question asks to navigate / show / take me to / open / point / highlight (`AssistantService.NavigationIntent`, typo-tolerant), a system note for that turn tells the model it must call `show_on_screen`. Without it, the small model sometimes answered from `get_expiring_subscriptions` in text only.

**Add a new target:** put `[attr.data-ai-target]="'thing:' + id"` on the element, add a case in `ShowOnScreen` that returns the route/tab and the key, and (if filters could hide it) react to `UiPointerService.request` in that tab, as the three tabs above do.

**Ideas, not built yet:**
- **Positions & Exits sub-tabs:** stuck exits (In-Flight Exits rows), cleanup items, unhedged positions (Hedges NAKED/PARTIAL), and an account's at-risk position rows.
- **Live Signals:** signals needing attention, and the rejected accounts inside one signal's detail.
- **Overview and Performance:** an Overview hub card, and the worst strategy or top losing accounts on Performance.
- **Other pages:** the Fyers *Renew* / *Login* button on Broker Live, and the Redis / SQL persister status on the Realtime page.

## Voice ("Jarvis …")

Say **"Jarvis"** and then your question, e.g. *"Jarvis, how many positions are open in 1023?"*:

1. The orb **glides from the corner to the bottom-centre** and grows.
2. The screen **dims and blurs**, and a **Siri-style glow** rises from the bottom. Its height follows your mic level.
3. Your words appear live ("Listening…"). When you stop talking (about 1.6 s of silence), the question is sent ("Thinking…").
4. The answer is **shown** in a glass card **and spoken** aloud. The orb pulses while speaking.
5. Afterwards it goes back to listening for "Jarvis". **Esc**, clicking anywhere, or clicking the orb closes it and stops speech.

| Control | Where |
|---|---|
| **Wake word on/off** | ◎/◉ button in the chat header. A green breathing ring on the orb means it's listening for "Jarvis". Remembered in this browser |
| **Push-to-talk** | 🎙 button next to the text box: asks one question without the wake word |
| Voice questions in history | Shown with 🎙 in the chat |

**Requirements and privacy**
- Speech recognition works in **Chrome and Edge** only (Web Speech API); Firefox has no recognition. Spoken answers work in all modern browsers.
- The browser asks for **microphone permission** the first time.
- **Privacy:** Chrome and Edge send microphone audio to their cloud speech service while recognition is running. With the wake word **on**, that's all the time the page is open. That's why it's **off by default**; use push-to-talk if you prefer.
- Answers are made speakable before reading: markdown removed, `-₹1,200` → "minus 1,200 rupees", `$74,315` → "74,315 dollars", `%` → "percent", tables read as their first rows. Long answers are cut off with "More details are on screen."
- Voice: UK English male if available (e.g. "Google UK English Male"), else Indian English, else the default.

Files: `frontend/.../shared/voice.service.ts` (recognition, wake word, mic level, text-to-speech, `speakable()`), plus the overlay in `assistant-chat.component.ts`.

## Setup

```powershell
cd "C:\Users\pvija\OneDrive\Documents\CodeLineTech\Jarvis(demo)\backend\Jarvis.Api"
dotnet user-secrets set "Groq:ApiKey" "<your Groq key>"     # stays on this machine; never in code, docs or the browser
```

Restart the API. `appsettings.json → Groq` has `Model` (default `openai/gpt-oss-20b`) and optionally `ReasoningEffort` (`low` default, `medium`, `high`).

## What you can ask

> The full list of question types, with examples, is in **[13-assistant-questions.md](13-assistant-questions.md)**.

| Question | Tool used | Data |
|---|---|---|
| What is the current BTC price? | `get_btc_price` | Delta live feed (USD) |
| What is Vijay's current P&L? / my funds / my positions | `get_fyers_account` | **Real Fyers account**. "Vijay", "my account" and "Fyers" all mean this |
| What is the status of broker account 1023? | `find_broker_account` | Demo OMS account by client id, account number, name or broker |
| How many positions are open in 1023? / right now? | `get_positions` | Open / closed / at-risk counts and a list |
| How many issues need solving right now? | `get_open_issues` | Needs-attention signals, broker rejections, cleanup, in-flight exits, containers |
| Give me a portfolio summary | `get_portfolio_summary` | Totals, top gainers and losers, NIFTY (simulated) |
| What's the status of signal 45402? | `get_signal` | One signal by id or symbol |
| How did 1023's P&L move in the last hour? / Vijay's P&L trend | `get_pnl_history` | SQL history (`sim.pnl` / `fyers.pnl`): start, now, change, high, low |
| Top 5 losing accounts / most open / most at-risk | `get_rankings` | Demo accounts ranked |
| Which strategy makes the most? How is S1EN doing? | `get_strategy_pnl` | P&L by strategy, plus best and worst accounts for one strategy |
| How is BASKET 06 doing? | `get_basket_summary` | Accounts, open positions, P&L, strategies per basket |
| Is any container down? Is the pipeline healthy? | `get_infrastructure` | Containers, Redis, SQL persisters, Delta feed, Fyers poller |
| Why are orders rejected on ANT? | `get_broker_rejections` | Open rejections by reason and broker, with top accounts |
| Any stuck exits / cleanup for 1360? | `get_exits_and_cleanup` | In-flight exits by stage (oldest first), cleanup items |
| Latest 3 signals that need attention | `get_recent_signals` | Newest signals, filtered by attention / open / exited / symbol / strategy |
| Where are NIFTY, BANK NIFTY, SENSEX? | `get_market_indices` | Real Fyers quotes, or the simulated NIFTY when Fyers is offline |
| How much has BTC moved in 4 hours? | `get_btc_change` | Stored 1-minute candles (SQL) |
| Signals today and fill rate? | `get_today_activity` | Signals per hour and the order funnel |
| Whose subscriptions expire in 3 days? | `get_expiring_subscriptions` | Subscriptions ending soon |
| Which accounts didn't get signal 506, and why? | `get_signal_accounts` | Per-account fill, reject or partial reason, and qty still open, for one signal |
| What is our exposure to NIFTY 27350 PE? | `get_symbol_exposure` | Net and gross qty, accounts, average price, LTP, unrealized, per instrument. Understands "25300 CE" or full symbols, in both symbol formats |
| Which short positions are not hedged? | `get_unhedged_positions` | NAKED and PARTIAL hedges with the missing qty |
| Show my rejected Fyers orders today | `get_fyers_orders_today` | Your real Fyers order book with status and the broker's message |
| What does exit partial mean? How do I renew Fyers? | `get_help` | Built-in glossary and how-tos (UI steps) plus the best-matching sections of `docs/*.md` |

### Tool routing
There are 24 tools, and sending all of them costs about 1.5k tokens per call (Groq's free tier allows 8k tokens a minute). Each question therefore gets only the tools whose **keywords** match its last two user messages, plus three core tools (`find_broker_account`, `get_positions`, `get_portfolio_summary`). Strategy and basket names also count as keywords. If nothing specific matches, all tools are sent. When you add a tool, add its keywords to `AssistantTools.Keywords`.

Account lookups understand `1023`, `client 1023`, `broker account #1023`, broker account numbers and names.

Follow-up questions work: the last 12 messages are sent with each question. The page you're on is passed along too.

## Safety

- **Read-only:** no tool can place, modify or exit orders. "Exit all positions of 1023" gets a polite refusal.
- The Groq key and all broker tokens stay in the API.
- **Numbers are checked:** tools return amounts already formatted (`-₹15,948.40`, `$74,060.00`). After the model answers, every ₹/$ amount is checked against the tool results. If one doesn't match (small models sometimes mangle digits), the model is asked once to correct it; otherwise a note is added.
- Every question is logged (question, tools, time; **not** the answer) to the `ai.chat` realtime topic → SQL (`dbo.RealtimeEvents`).

## API keys and model fallback

**Set 1–4 (or more) keys**, in user-secrets only:

```powershell
cd "C:\Users\pvija\OneDrive\Documents\CodeLineTech\Jarvis(demo)\backend\Jarvis.Api"
dotnet user-secrets set "Groq:ApiKeys:0" "<key 1>"
dotnet user-secrets set "Groq:ApiKeys:1" "<key 2>"
dotnet user-secrets set "Groq:ApiKeys:2" "<key 3>"
dotnet user-secrets set "Groq:ApiKeys:3" "<key 4>"
```

The old single `Groq:ApiKey` still works and is added to the pool.

**How a question is routed** (`GroqKeyPool` + `AssistantService.CompleteAsync`):
1. Keys are tried **round-robin** for the primary model (`Groq:Model`, default `openai/gpt-oss-20b`).
2. **429 (rate limit):** that *key + model* is parked until Groq's "try again in …", and the next key is tried at once.
3. **401 (invalid or revoked key):** the key is parked for an hour, for all models.
4. When every key is parked for the primary model, the same keys are tried on the **fallback models** (`Groq:FallbackModels`, default `openai/gpt-oss-120b` → `llama-3.3-70b-versatile`). A model that no longer exists on Groq is skipped.
5. If everything is busy, it waits up to 20 s (twice). Otherwise it replies, e.g. "Groq limit reached (daily token limit on …). Try again in 72 min."

The chat shows **"via gpt-oss-120b"** under an answer when a fallback model answered. `GET /api/assistant/status` shows each key (only `…last4`) with its requests, rate-limit count and what it's cooling for.

> **Important: Groq limits are per organization *and* per model.** Keys from the **same** Groq account share one allowance (e.g. 200,000 tokens/day for gpt-oss-20b), so more keys from one account add **resilience** (a revoked or invalid key doesn't break the chat), not capacity. **Model fallback** is what adds capacity, because each model has its own allowance. For much more, use Groq's Dev Tier. Creating several free accounts just to get around the limits is likely against Groq's terms.

## Groq free tier

Limits seen on the free tier for `gpt-oss-20b`: about **8,000 tokens per minute** and **200,000 tokens per day** (per organization). The free tier allows about **8,000 tokens per minute**, and one question uses about 1,500–3,000 (tool list + data). If you ask several questions quickly, the API waits the few seconds Groq asks for and retries automatically, so the answer is a bit slower instead of failing. For heavier use, upgrade to Groq's Dev Tier.

## Adding a tool

1. Add a definition in `AssistantTools.AllDefinitions()` (name, description, JSON-schema parameters). Write the description for the model: say *when* to use it.
2. Add a `case` in `RunAsync` that returns a small object. Include a `summary` string and pre-formatted money (`Inr(...)`) for anything the user will read.
3. Keep it read-only, and return only what's needed: less data means fewer tokens and fewer rate-limit waits.

## Files

| File | Role |
|---|---|
| `backend/.../Services/Assistant/AssistantService.cs` | Groq call, tool loop (max 5 rounds), retries, amount check, logging |
| `backend/.../Services/Assistant/AssistantTools.cs` | Tool definitions and implementations |
| `backend/.../Controllers/AssistantController.cs` | `GET /api/assistant/status`, `POST /api/assistant/chat` |
| `frontend/jarvis-ui/public/assistant/orb.webp` · `orb.gif` · `orb-still.png` | Button animation (WebP, GIF fallback, still frame for reduced motion). To change it, replace these files (128×128) |
| `frontend/.../shared/assistant-chat.component.ts` | Floating button and chat panel, suggested questions, tool chips, safe markdown |
