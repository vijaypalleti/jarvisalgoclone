# 5. Broker Live (Fyers)

A sidebar page (`/admin/broker-live`) showing **real** data from a Fyers account through Fyers API v3: profile, funds, positions, order book, trade book, holdings and index quotes.

> **Read-only by design.** The client has no order placement, modification or cancel calls.

## Set the credentials (never in code or git)

Credentials are stored in .NET **user-secrets**, outside the repository:

```powershell
cd "C:\Users\pvija\OneDrive\Documents\CodeLineTech\Jarvis(demo)\backend\Jarvis.Api"
dotnet user-secrets set "Fyers:ClientId"    "GFHMZKHCO7-200"
dotnet user-secrets set "Fyers:AccessToken" "<today's access token>"
```

- Fyers access tokens **expire daily**, so set a new one each trading day.
- To paste a token by hand without restarting, use the **Token** button on the page. That copy lives in memory only and is lost on restart. Easier: use **Login with Fyers** (below).
- Don't paste tokens into chat, docs or commits.

## Log in from the app (recommended)

This replaces the Python script that opened the login page and asked you to paste the redirected URL.

**One-time setup**

```powershell
cd "C:\Users\pvija\OneDrive\Documents\CodeLineTech\Jarvis(demo)\backend\Jarvis.Api"
dotnet user-secrets set "Fyers:ClientId"  "<APP_ID>-200"
dotnet user-secrets set "Fyers:SecretKey" "<app secret>"      # stays on this machine, never sent to the browser
```

Then pick one of two modes:

| Mode | Fyers app redirect URL (myapi.fyers.in → your app → edit) | `Fyers:RedirectUri` | Daily effort |
|---|---|---|---|
| **One click** (default) | `http://127.0.0.1:5186/api/fyers/auth/callback` | leave unset (that's the default) | Click **Login with Fyers**, log in, and the window closes by itself |
| **Paste** | keep `https://trade.fyers.in/api-login/redirect-uri/index.html` | `dotnet user-secrets set "Fyers:RedirectUri" "https://trade.fyers.in/api-login/redirect-uri/index.html"` | Click **Login with Fyers**, log in, copy the redirect page's URL, paste it, then **Generate session** |

The redirect URL in the Fyers app and `Fyers:RedirectUri` **must be identical**, or Fyers refuses the login.

**Every day**

- **Renew with PIN:** when the session has expired but the saved refresh token (about 15 days) is still valid, Broker Live shows a PIN box. Enter your Fyers PIN to get a new session without opening the login page.
- Otherwise, click **Login with Fyers**.

**What happens behind the button**
1. `GET /api/fyers/auth/start` builds the Fyers login URL with a random `state`, which protects against forged callbacks.
2. Fyers redirects to `/api/fyers/auth/callback?auth_code=…&state=…`, or you paste the URL into `POST /api/fyers/auth/exchange`.
3. The API calls Fyers `validate-authcode` with `appIdHash = sha256("appId:secret")` and receives the access token and refresh token.
4. The token is used immediately (pages, background poller) and saved encrypted in `%LOCALAPPDATA%\JarvisDemo\fyers-token.dat`. On the next API start it's picked up automatically while still valid.
5. `POST /api/fyers/auth/renew {pin}` calls `validate-refresh-token` for the PIN renewal.

Remove the saved session with `DELETE /api/fyers/auth/saved`, or delete the file.

## Where Fyers data appears

| Place | What |
|---|---|
| Broker Live page | Funds, positions, orders, trades, holdings, quotes (auto-refresh every 5 s while connected) |
| Overview → Recent Alerts | Rejected Fyers orders, shown as `FYERS <id> · LIVE` |
| Overview → Brokers & Health tile | Whether the Fyers connection is live |

## Files

- Backend: `Services/FyersClient.cs`, `Controllers/FyersController.cs`
- Frontend: `core/fyers.service.ts`, `pages/broker-live/`

## Endpoints

`GET /api/fyers/status | funds | positions | orders | trades | holdings | quotes?symbols=NSE:NIFTY50-INDEX,...` and `POST /api/fyers/token` with `{ clientId, accessToken }`.
