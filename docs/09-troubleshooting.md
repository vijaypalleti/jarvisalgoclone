# 9. Troubleshooting

Each entry gives the symptom, its cause and the fix.

## Running the app

### `dotnet run`: "address already in use" (port 5186)
**Cause:** an older API process is still running.
**Fix:**
```powershell
Get-NetTCPConnection -LocalPort 5186 -State Listen | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }
```

### `ng serve` / `npm install`: esbuild errors about the platform
**Cause:** the global `~/.npmrc` has `os=linux`.
**Fix:** the project already contains `frontend/jarvis-ui/.npmrc` with `os=win32`. Delete `node_modules` and run `npm install` again from the `frontend/jarvis-ui` folder.

### `ng build`: "This command is not available when running the Angular CLI outside a workspace"
**Fix:** run it from `frontend/jarvis-ui`, not from the repo root.

### Angular 20 refuses to install
**Cause:** Node 22.11 is installed, and Angular 20 needs ≥ 22.12. The project uses Angular 19 for this reason.

### UI changes to `proxy.conf.json` have no effect / SignalR shows DISCONNECTED
**Cause:** `ng serve` reads the proxy file only at startup.
**Fix:** stop and restart `npx ng serve`.

## Redis

### "Redis is not connected" / keeps disconnecting
The VS Code Redis extension shows `Can't connect to server on 127.0.0.1:6379` or `Reached the max retries per request limit`.

**Cause:** Redis runs inside WSL Ubuntu, and WSL (2.4) shuts Ubuntu down a few seconds after the last Windows-side WSL process closes. Redis stops with it.

**Now automatic:** since 2026-11-11 the API starts WSL and Redis itself (`WslRedisStarter`). If you still see this, check the API console for `Could not start WSL/Redis automatically: …` or `Redis still unreachable`.

**Quick fix by hand:** start Ubuntu again. Redis is enabled as a service and starts with it.
```powershell
wsl -d Ubuntu -e true
wsl -d Ubuntu -- redis-cli ping      # PONG
```
The API reconnects by itself (`abortConnect=false`). Trades that arrived while Redis was down were not cached, but SQL keeps everything that was already in the stream.

**Keep WSL alive for this session:** start a hidden process that never exits.
```powershell
Start-Process wsl.exe -ArgumentList '-d','Ubuntu','-e','sleep','infinity' -WindowStyle Hidden
```

**Keep it alive permanently (every login):**
```powershell
$a = New-ScheduledTaskAction -Execute "wsl.exe" -Argument "-d Ubuntu -e sleep infinity"
$t = New-ScheduledTaskTrigger -AtLogOn
$s = New-ScheduledTaskSettingsSet -ExecutionTimeLimit 0 -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName "KeepWSLAlive" -Action $a -Trigger $t -Settings $s
# remove later: Unregister-ScheduledTask -TaskName KeepWSLAlive
```

### Ubuntu is running but Redis isn't
```powershell
wsl -d Ubuntu -u root -- service redis-server start
```

### VS Code Redis extension can't connect to `172.x.x.x:6379`
**Cause:** that is the WSL VM's IP, and Redis listens only on localhost inside WSL.
**Fix:** connect to `localhost` (or `127.0.0.1`) on port `6379`.

### Forgot the WSL user password
Reset it from Windows by entering Ubuntu as root:
```powershell
wsl -d Ubuntu -u root
passwd vijay
```

### PowerShell helper named `R` doesn't work
**Cause:** `R` is a built-in PowerShell alias (for `Invoke-History`). Use another name, e.g. `RedisCli`.

## SQL Server

### `sqlcmd`: "The certificate chain was issued by an authority that is not trusted"
**Cause:** ODBC Driver 18 encrypts by default, and the local SQL Express certificate is self-signed.
**Fix:** add `-C` (trust the server certificate):
```powershell
sqlcmd -S "localhost\SQLEXPRESS" -E -C -d JarvisMarket -Q "SELECT TOP 5 * FROM dbo.vSimPnl ORDER BY EventTime DESC"
```

### Realtime Data page shows "Pending" rising and SQL rows not growing
**Cause:** SQL Server isn't reachable. The red line on the page shows the error.
**Fix:** start the `SQL Server (SQLEXPRESS)` service. Pending entries are saved automatically once it's back.

### Fyers topics stay at 0 / poller says "token invalid"
**Cause:** the Fyers access token expired (they last one day).
**Fix:** set a new one (see below). The poller retries every minute and picks it up without a restart.

## Delta / Fyers

### Delta candles endpoint returns HTML instead of JSON
**Cause:** Delta's CDN blocks requests that have no `User-Agent`. The `delta` HttpClient in `Program.cs` sets one; keep it.

### Fyers login window shows an error about the redirect URI
**Cause:** the redirect URL in your Fyers app (myapi.fyers.in) differs from `Fyers:RedirectUri`. The default is `http://127.0.0.1:5186/api/fyers/auth/callback`.
**Fix:** make them identical (see [05](05-broker-live-fyers.md#log-in-from-the-app-recommended)), or use the paste mode.

### "Login link expired or not started from the app"
**Cause:** the callback arrived without a known `state`, e.g. you reloaded an old login page, or more than 10 minutes passed.
**Fix:** press **Login with Fyers** again.

### Fyers page shows not connected / 401
**Cause:** the access token expired (they last one day).
**Fix:** set a new one with `dotnet user-secrets set "Fyers:AccessToken" "<token>"`, or use **Update token** on the page.

## Git

### Commits show someone else's name on GitHub
**Fix:** set the identity for this repo, and add that email to your GitHub account:
```powershell
git config user.name  "Vijay Palleti"
git config user.email "pvijaypvijay3333@gmail.com"
```

## AI assistant

### "The assistant is not configured"
Set the key: `dotnet user-secrets set "Groq:ApiKey" "<key>"` in `backend/Jarvis.Api`, then restart the API.

### "Groq limit reached (daily token limit …). Try again in N min"
**Cause:** the **daily** token allowance (e.g. 200,000 tokens/day for gpt-oss-20b) is used up, on every key and every fallback model. Keys from the same Groq account share it.
**Fix:** wait (it's a rolling 24 h window), keep the fallback models configured (`Groq:FallbackModels`) since each has its own allowance, or upgrade the Groq tier. Check `GET /api/assistant/status` to see which key or model is cooling and until when.

### Answers are slow (5–15 s) when asking many questions quickly
**Cause:** Groq's free tier allows about 8,000 tokens per minute. The API waits the time Groq asks for and retries, which adds a few seconds.
**Fix:** ask a little slower, or upgrade to Groq's Dev Tier.

### "Groq rejected the API key"
The key is wrong or was revoked. Create a new one in the Groq console and set it again.

### Voice: "Jarvis" does nothing / no mic button
- Use **Chrome or Edge**; Firefox has no speech recognition, so the mic and wake-word buttons are hidden.
- Turn the wake word on (◎ → ◉ in the chat header) and allow the **microphone** when asked. If you blocked it: click the padlock in the address bar → Microphone → Allow, then reload.
- The page must be served from `localhost` or HTTPS; browsers block the mic on plain-HTTP remote addresses.
- "Microphone permission was denied" in the chat → same fix as above.

### Voice: the answer isn't spoken
Check the system volume and output device. Some browsers need one click on the page before audio can play, so click anywhere once after loading.

## Overview hub

### Rails don't touch the tiles after zooming (shifted sideways)
**Cause:** the rails were drawn for the previous zoom level and not redrawn.
**Fix:** fixed on 2026-11-11 with several redraw triggers (see [03](03-console-pages.md)). If it ever happens again, refresh the page and check `drawConnectors()` / `scheduleDraw()` in `overview-tab.component.ts`.

## Angular template gotchas hit while building

| Error | Fix |
|---|---|
| A literal `@` in a template is parsed as a block | Write it as `&#64;` |
| `@else if (x; as y)` is not allowed in Angular 19 | Use `@let y = x;` before the block |
| A `<select [value]>` shows the first option instead of the bound one | Bind `[selected]` on each `<option>` instead |
| Styles bleed between `.sm` and `.btn.sm` | Use distinct class names (`.tiny`, `.w120`) |

### Index charts: "You've reached the Standard plan limit of 5 requests/second"
**Cause:** the Fyers Standard plan allows 5 API calls per second for the whole app (the poller, Market Charts and the assistant share it).
**Fix:** this is already handled. `FyersClient` spaces calls at least 220 ms apart and retries once after 1 s on error -360. If it keeps happening, raise `Fyers:Poll:Seconds` (poll less often) or close extra chart tabs.

### SQL queries are very slow (30–90 s) or the persisters fall behind
**Cause:** SQL Express is short of memory. When Windows has little free RAM (WSL, Chrome, VS Code, SSMS, ng serve…), SQL Server shrinks its cache. Queries that sort or group, and the app's bulk inserts, then wait on `RESOURCE_SEMAPHORE` (a memory grant).
**Check:** `SELECT session_id, wait_type FROM sys.dm_exec_requests WHERE wait_type = 'RESOURCE_SEMAPHORE'`, and `SELECT process_physical_memory_low FROM sys.dm_os_process_memory` (1 = under pressure).
**Fix:** close what you don't need, or let SQL keep a floor of memory:
```sql
EXEC sp_configure 'show advanced options', 1; RECONFIGURE;
EXEC sp_configure 'min server memory (MB)', 1024; RECONFIGURE;
```
Optionally cap WSL in `%USERPROFILE%\.wslconfig` (`[wsl2]` then `memory=2GB`). When writing your own queries, sort `DeltaTrades` by `Id` (indexed) and filter the `v*` views by `EventTime`.

