using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Web;
using Jarvis.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jarvis.Api.Controllers;

/// <summary>
/// In-app Fyers login, replacing the Python "generate session" script:
///   1. GET  start      → Fyers login URL (opened in a popup by the UI)
///   2a. GET callback   → Fyers redirects here with ?auth_code (one-click; needs Fyers:RedirectUri = this URL in the Fyers app)
///   2b. POST exchange  → or paste the redirected URL / auth_code (works with any redirect URI)
///   3. auth_code + appIdHash(sha256 "appId:secret") → access + refresh token → used live, saved encrypted
///   POST renew {pin}   → next days: refresh token + PIN, no browser login
/// The secret key stays server-side (user-secrets "Fyers:SecretKey"); the browser never sees secret or tokens.
/// </summary>
[ApiController]
[Route("api/fyers/auth")]
public class FyersAuthController(FyersClient fyers, FyersTokenStore store, IConfiguration config, ILogger<FyersAuthController> log) : ControllerBase
{
    public const string DefaultCallback = "http://127.0.0.1:5186/api/fyers/auth/callback";

    private static readonly ConcurrentDictionary<string, DateTime> States = new();   // CSRF: state → issued (10-min validity)

    private string? AppId => config["Fyers:ClientId"] ?? fyers.ClientId;
    private string? Secret => config["Fyers:SecretKey"];
    private string RedirectUri => config["Fyers:RedirectUri"] ?? DefaultCallback;
    private string AfterLoginUrl => config["Fyers:AfterLoginUrl"] ?? "http://localhost:4200/admin/broker-live";
    private bool OneClick => RedirectUri.Contains("/api/fyers/auth/callback", StringComparison.OrdinalIgnoreCase);

    /// <summary>What the UI needs to show the right login controls (no secrets).</summary>
    [HttpGet("config")]
    public object Config()
    {
        var saved = store.Load();
        return new
        {
            appId = AppId, secretConfigured = !string.IsNullOrWhiteSpace(Secret), redirectUri = RedirectUri, oneClick = OneClick,
            saved = saved is null ? null : new
            {
                savedUtc = saved.SavedUtc, accessExpiresAt = FyersClient.JwtExpiry(saved.AccessToken),
                canRenewWithPin = !string.IsNullOrEmpty(saved.RefreshToken), refreshExpiresAt = FyersClient.JwtExpiry(saved.RefreshToken),
            },
        };
    }

    [HttpGet("start")]
    public IActionResult Start()
    {
        if (Missing() is { } err) return BadRequest(new { error = err });
        foreach (var old in States.Where(kv => DateTime.UtcNow - kv.Value > TimeSpan.FromMinutes(10)).Select(kv => kv.Key).ToList()) States.TryRemove(old, out _);
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        States[state] = DateTime.UtcNow;
        return Ok(new { url = fyers.AuthCodeUrl(AppId!, RedirectUri, state), oneClick = OneClick, redirectUri = RedirectUri });
    }

    /// <summary>Fyers redirects the login window here. Returns a tiny page that tells the app and closes itself.</summary>
    [HttpGet("callback")]
    public async Task<ContentResult> Callback([FromQuery(Name = "auth_code")] string? authCode, [FromQuery] string? state, [FromQuery] string? s, [FromQuery] string? message)
    {
        if (string.IsNullOrEmpty(state) || !States.TryRemove(state, out _))
            return Page(false, "Login link expired or not started from the app. Press “Login with Fyers” again.");
        if (string.IsNullOrEmpty(authCode) || (s is not null && s != "ok"))
            return Page(false, $"Fyers did not return an auth code{(message is null ? "" : ": " + message)}.");
        var (ok, msg) = await ExchangeAsync(authCode);
        return Page(ok, msg);
    }

    public record ExchangeRequest(string Input);

    /// <summary>Paste fallback: the full redirected URL (…?auth_code=…) or just the auth_code.</summary>
    [HttpPost("exchange")]
    public async Task<IActionResult> Exchange([FromBody] ExchangeRequest req)
    {
        if (Missing() is { } err) return BadRequest(new { error = err });
        var code = ExtractAuthCode(req.Input);
        if (code is null) return BadRequest(new { error = "Couldn't find an auth_code. Paste the full redirected URL or the auth code (starts with eyJ)." });
        var (ok, msg) = await ExchangeAsync(code);
        return ok ? Ok(new { message = msg }) : BadRequest(new { error = msg });
    }

    public record RenewRequest(string Pin);

    [HttpPost("renew")]
    public async Task<IActionResult> Renew([FromBody] RenewRequest req)
    {
        if (Missing() is { } err) return BadRequest(new { error = err });
        var saved = store.Load();
        if (saved?.RefreshToken is null) return BadRequest(new { error = "No saved refresh token. Use “Login with Fyers” once." });
        if (string.IsNullOrWhiteSpace(req.Pin)) return BadRequest(new { error = "Enter your Fyers PIN." });
        try
        {
            var r = await fyers.ValidateRefreshToken(saved.ClientId, Secret!, saved.RefreshToken, req.Pin.Trim());
            Use(saved.ClientId, r.AccessToken!, r.RefreshToken ?? saved.RefreshToken);
            return Ok(new { message = "Session renewed with PIN" });
        }
        catch (FyersException ex) { return BadRequest(new { error = $"Renew failed: {ex.Message}. Use “Login with Fyers”." }); }
    }

    /// <summary>Forget the saved session (the token in user-secrets, if any, is used again after restart).</summary>
    [HttpDelete("saved")]
    public IActionResult Forget() { store.Delete(); return NoContent(); }

    // ------------------------------------------------------------------ helpers

    private string? Missing() =>
        string.IsNullOrWhiteSpace(AppId) ? "Fyers app id is not set: dotnet user-secrets set \"Fyers:ClientId\" \"<APP_ID>-200\"" :
        string.IsNullOrWhiteSpace(Secret) ? "Fyers secret key is not set: dotnet user-secrets set \"Fyers:SecretKey\" \"<secret>\"" : null;

    private async Task<(bool ok, string message)> ExchangeAsync(string authCode)
    {
        try
        {
            var r = await fyers.ValidateAuthCode(AppId!, Secret!, authCode);
            Use(AppId!, r.AccessToken!, r.RefreshToken);
            log.LogInformation("Fyers session generated via in-app login (expires {Exp})", FyersClient.JwtExpiry(r.AccessToken));
            return (true, "Fyers connected");
        }
        catch (FyersException ex) { return (false, $"Fyers rejected the auth code: {ex.Message}"); }
    }

    private void Use(string appId, string accessToken, string? refreshToken)
    {
        fyers.SetCredentials(appId, accessToken);                                           // live now (poller, pages)
        store.Save(new SavedFyersToken(appId, accessToken, refreshToken, DateTime.UtcNow)); // and after restarts
    }

    public static string? ExtractAuthCode(string? input)
    {
        var t = input?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        if (t.StartsWith("eyJ")) return t;                                                  // raw auth_code (JWT)
        var q = t.Contains('?') ? t[(t.IndexOf('?') + 1)..] : t;
        var code = HttpUtility.ParseQueryString(q)["auth_code"];
        return string.IsNullOrWhiteSpace(code) ? null : code;
    }

    private ContentResult Page(bool ok, string message)
    {
        var msg = WebUtility.HtmlEncode(message);
        var back = WebUtility.HtmlEncode(AfterLoginUrl);
        var html = $$"""
            <!doctype html><html><head><meta charset="utf-8"><title>Fyers login</title>
            <style>body{font-family:system-ui,Segoe UI,sans-serif;display:grid;place-items:center;height:100vh;margin:0;background:#f8fafc;color:#0f172a}
            .c{background:#fff;border:1px solid #e2e8f0;border-radius:14px;padding:28px 34px;text-align:center;box-shadow:0 10px 30px rgba(15,23,42,.08)}
            .i{font-size:34px}.ok{color:#15803d}.bad{color:#b91c1c}a{color:#0e7490}</style></head>
            <body><div class="c"><div class="i {{(ok ? "ok" : "bad")}}">{{(ok ? "✓" : "✕")}}</div><h3>{{msg}}</h3>
            <p>{{(ok ? "This window will close…" : "You can close this window and try again.")}}</p><a href="{{back}}">Back to Broker Live</a></div>
            <script>
              try { window.opener && window.opener.postMessage({ type: 'fyers-auth', ok: {{(ok ? "true" : "false")}} }, '*'); } catch (e) {}
              if ({{(ok ? "true" : "false")}}) setTimeout(function () { if (window.opener) window.close(); else location.href = '{{back}}'; }, 1200);
            </script></body></html>
            """;
        return new ContentResult { Content = html, ContentType = "text/html; charset=utf-8", StatusCode = 200 };
    }
}
