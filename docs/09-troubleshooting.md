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
