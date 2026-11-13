using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace Jarvis.Api.Services;

/// <summary>
/// Read-only Fyers API v3 client. Credentials come from configuration
/// (user-secrets "Fyers:ClientId" / "Fyers:AccessToken", or env vars Fyers__ClientId / Fyers__AccessToken).
/// The access token can also be swapped at runtime via POST /api/fyers/token (in-memory only).
/// Deliberately exposes NO order placement / modification / cancel calls.
/// </summary>
public class FyersClient(HttpClient http, IConfiguration config, IMemoryCache cache, ILogger<FyersClient> log)
{
    private static string? _runtimeToken;
    private static string? _runtimeClientId;

    public string? ClientId => _runtimeClientId ?? config["Fyers:ClientId"];
    private string? Token => _runtimeToken ?? config["Fyers:AccessToken"];
    public bool Configured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(Token);

    public void SetCredentials(string clientId, string token)
    {
        _runtimeClientId = clientId.Trim();
        _runtimeToken = token.Trim();
        if (cache is MemoryCache mc) mc.Compact(1.0);
    }

    /// <summary>Token expiry read from the JWT "exp" claim (no signature validation; display only).</summary>
    public DateTimeOffset? TokenExpiry => JwtExpiry(Token);

    public static DateTimeOffset? JwtExpiry(string? token)
    {
        {
            try
            {
                var parts = token?.Split('.');
                if (parts is not { Length: 3 }) return null;
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
                return doc.RootElement.TryGetProperty("exp", out var exp) ? DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64()) : null;
            }
            catch { return null; }
        }
    }

    // ------------------------------------------------------------------ login (auth code → access token)
    // These are the only POSTs, and they go to Fyers' auth endpoints, never to order APIs.

    /// <summary>SHA-256 of "appId:secret" (hex), which Fyers v3 calls appIdHash.</summary>
    public static string AppIdHash(string clientId, string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{clientId}:{secret}"))).ToLowerInvariant();

    /// <summary>Login page URL; Fyers redirects back to <paramref name="redirectUri"/> with ?auth_code=…&amp;state=….</summary>
    public string AuthCodeUrl(string clientId, string redirectUri, string state) =>
        new Uri(http.BaseAddress!, $"api/v3/generate-authcode?client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&state={Uri.EscapeDataString(state)}").ToString();

    public Task<FyersTokenResponse> ValidateAuthCode(string clientId, string secret, string authCode) =>
        PostAuth("api/v3/validate-authcode", new { grant_type = "authorization_code", appIdHash = AppIdHash(clientId, secret), code = authCode });

    /// <summary>Daily renewal without a browser login: refresh token (valid ~15 days) + the account PIN.</summary>
    public Task<FyersTokenResponse> ValidateRefreshToken(string clientId, string secret, string refreshToken, string pin) =>
        PostAuth("api/v3/validate-refresh-token", new { grant_type = "refresh_token", appIdHash = AppIdHash(clientId, secret), refresh_token = refreshToken, pin });

    private async Task<FyersTokenResponse> PostAuth(string path, object body)
    {
        using var res = await http.PostAsJsonAsync(path, body);
        var text = await res.Content.ReadAsStringAsync();
        FyersTokenResponse? r;
        try { r = JsonSerializer.Deserialize<FyersTokenResponse>(text, JsonOpts); }
        catch (JsonException) { throw new FyersException($"Unexpected Fyers response ({(int)res.StatusCode}).", 502); }
        if (r is null || r.S != "ok" || string.IsNullOrEmpty(r.AccessToken))
        {
            log.LogWarning("Fyers {Path} failed: {Code} {Message}", path, r?.Code, r?.Message);
            throw new FyersException(r?.Message ?? $"HTTP {(int)res.StatusCode}", 400);
        }
        return r;
    }

    public Task<FyersProfile> Profile() => Get<FyersProfile>("api/v3/profile", TimeSpan.FromMinutes(5));
    public Task<FyersFunds> Funds() => Get<FyersFunds>("api/v3/funds", TimeSpan.FromSeconds(5));
    public Task<FyersPositions> Positions() => Get<FyersPositions>("api/v3/positions", TimeSpan.FromSeconds(3));
    public Task<FyersOrders> Orders() => Get<FyersOrders>("api/v3/orders", TimeSpan.FromSeconds(3));
    public Task<FyersTrades> Trades() => Get<FyersTrades>("api/v3/tradebook", TimeSpan.FromSeconds(3));
    public Task<FyersHoldings> Holdings() => Get<FyersHoldings>("api/v3/holdings", TimeSpan.FromSeconds(30));
    public Task<FyersQuotes> Quotes(string symbols) => Get<FyersQuotes>($"data/quotes?symbols={Uri.EscapeDataString(symbols)}", TimeSpan.FromSeconds(2));
    /// <summary>Historical candles (read-only market data). resolution: "1", "5", "15", "60" or "D"; range as unix seconds.</summary>
    public Task<FyersHistory> History(string symbol, string resolution, long fromUnix, long toUnix) =>
        Get<FyersHistory>($"data/history?symbol={Uri.EscapeDataString(symbol)}&resolution={resolution}&date_format=0&range_from={fromUnix}&range_to={toUnix}&cont_flag=1",
            TimeSpan.FromSeconds(resolution == "D" ? 300 : 15));

    // Fyers Standard plan: max 5 requests/second for the whole app (poller + charts + assistant share it).
    // Space every call ≥ 220 ms apart so bursts queue up instead of being rejected.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _lastCall = DateTime.MinValue;
    private static async Task Pace()
    {
        await Gate.WaitAsync();
        try
        {
            var wait = _lastCall.AddMilliseconds(220) - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
            _lastCall = DateTime.UtcNow;
        }
        finally { Gate.Release(); }
    }

    private async Task<T> Get<T>(string path, TimeSpan ttl) where T : FyersEnvelope
    {
        if (!Configured) throw new FyersException("Fyers credentials are not configured.", 503);

        var key = $"fyers:{ClientId}:{path}";
        if (cache.TryGetValue(key, out T? cached) && cached is not null) return cached;

        HttpResponseMessage res = null!;
        T? parsed = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await Pace();
            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            req.Headers.TryAddWithoutValidation("Authorization", $"{ClientId}:{Token}");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            res?.Dispose();
            res = await http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            try { parsed = JsonSerializer.Deserialize<T>(body, JsonOpts); }
            catch (JsonException) { throw new FyersException($"Unexpected Fyers response ({(int)res.StatusCode}).", 502); }
            // -360 / 429 = "Standard plan limit of 5 requests/second": wait a second and try once more
            if (attempt == 0 && (parsed?.Code == -360 || (int)res.StatusCode == 429)) { await Task.Delay(1100); continue; }
            break;
        }
        using var _ = res;

        if (parsed is null || parsed.S != "ok")
        {
            var msg = parsed?.Message ?? $"HTTP {(int)res.StatusCode}";
            log.LogWarning("Fyers {Path} failed: {Code} {Message}", path, parsed?.Code, msg);
            // -8/-15/-16/-17 & 401 are token problems
            var status = res.StatusCode == System.Net.HttpStatusCode.Unauthorized || parsed?.Code is -8 or -15 or -16 or -17 ? 401 : 502;
            throw new FyersException(msg, status);
        }

        cache.Set(key, parsed, ttl);
        return parsed;
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString };
}

public class FyersTokenResponse : FyersEnvelope
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
}

public class FyersException(string message, int status) : Exception(message)
{
    public int Status { get; } = status;
}

// ---------------- Fyers DTOs (subset of fields; see Fyers API v3 docs) ----------------

public class FyersEnvelope
{
    [JsonPropertyName("s")] public string? S { get; set; }
    [JsonPropertyName("code")] public int? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

public class FyersProfile : FyersEnvelope { [JsonPropertyName("data")] public FyersProfileData? Data { get; set; } }
public class FyersProfileData
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    [JsonPropertyName("fy_id")] public string? FyId { get; set; }
    // email / mobile / PAN intentionally not mapped — never sent to the UI
}

public class FyersFunds : FyersEnvelope { [JsonPropertyName("fund_limit")] public List<FyersFundLine> FundLimit { get; set; } = new(); }
public class FyersFundLine
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("equityAmount")] public decimal EquityAmount { get; set; }
    [JsonPropertyName("commodityAmount")] public decimal CommodityAmount { get; set; }
}

public class FyersPositions : FyersEnvelope
{
    [JsonPropertyName("netPositions")] public List<FyersPosition> NetPositions { get; set; } = new();
    [JsonPropertyName("overall")] public FyersPositionsOverall? Overall { get; set; }
}
public class FyersPositionsOverall
{
    [JsonPropertyName("count_open")] public int CountOpen { get; set; }
    [JsonPropertyName("count_total")] public int CountTotal { get; set; }
    [JsonPropertyName("pl_realized")] public decimal PlRealized { get; set; }
    [JsonPropertyName("pl_unrealized")] public decimal PlUnrealized { get; set; }
    [JsonPropertyName("pl_total")] public decimal PlTotal { get; set; }
}
public class FyersPosition
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("symbol")] public string? Symbol { get; set; }
    [JsonPropertyName("productType")] public string? ProductType { get; set; }
    [JsonPropertyName("side")] public int Side { get; set; }
    [JsonPropertyName("netQty")] public int NetQty { get; set; }
    [JsonPropertyName("netAvg")] public decimal NetAvg { get; set; }
    [JsonPropertyName("buyQty")] public int BuyQty { get; set; }
    [JsonPropertyName("buyAvg")] public decimal BuyAvg { get; set; }
    [JsonPropertyName("sellQty")] public int SellQty { get; set; }
    [JsonPropertyName("sellAvg")] public decimal SellAvg { get; set; }
    [JsonPropertyName("ltp")] public decimal Ltp { get; set; }
    [JsonPropertyName("realized_profit")] public decimal RealizedProfit { get; set; }
    [JsonPropertyName("unrealized_profit")] public decimal UnrealizedProfit { get; set; }
    [JsonPropertyName("pl")] public decimal Pl { get; set; }
}

public class FyersOrders : FyersEnvelope { [JsonPropertyName("orderBook")] public List<FyersOrder> OrderBook { get; set; } = new(); }
public class FyersOrder
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("exchOrdId")] public string? ExchOrdId { get; set; }
    [JsonPropertyName("symbol")] public string? Symbol { get; set; }
    [JsonPropertyName("qty")] public int Qty { get; set; }
    [JsonPropertyName("filledQty")] public int FilledQty { get; set; }
    [JsonPropertyName("remainingQuantity")] public int RemainingQuantity { get; set; }
    [JsonPropertyName("status")] public int Status { get; set; }       // 1 cancelled, 2 traded, 4 transit, 5 rejected, 6 pending, 7 expired
    [JsonPropertyName("side")] public int Side { get; set; }           // 1 buy, -1 sell
    [JsonPropertyName("type")] public int Type { get; set; }           // 1 limit, 2 market, 3 SL-M, 4 SL-L
    [JsonPropertyName("productType")] public string? ProductType { get; set; }
    [JsonPropertyName("limitPrice")] public decimal LimitPrice { get; set; }
    [JsonPropertyName("stopPrice")] public decimal StopPrice { get; set; }
    [JsonPropertyName("tradedPrice")] public decimal TradedPrice { get; set; }
    [JsonPropertyName("orderDateTime")] public string? OrderDateTime { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("orderTag")] public string? OrderTag { get; set; }
}

public class FyersTrades : FyersEnvelope { [JsonPropertyName("tradeBook")] public List<FyersTrade> TradeBook { get; set; } = new(); }
public class FyersTrade
{
    [JsonPropertyName("tradeNumber")] public string? TradeNumber { get; set; }
    [JsonPropertyName("orderNumber")] public string? OrderNumber { get; set; }
    [JsonPropertyName("symbol")] public string? Symbol { get; set; }
    [JsonPropertyName("side")] public int Side { get; set; }
    [JsonPropertyName("tradedQty")] public int TradedQty { get; set; }
    [JsonPropertyName("tradePrice")] public decimal TradePrice { get; set; }
    [JsonPropertyName("tradeValue")] public decimal TradeValue { get; set; }
    [JsonPropertyName("productType")] public string? ProductType { get; set; }
    [JsonPropertyName("orderDateTime")] public string? OrderDateTime { get; set; }
}

public class FyersHoldings : FyersEnvelope { [JsonPropertyName("holdings")] public List<FyersHolding> Holdings { get; set; } = new(); }
public class FyersHolding
{
    [JsonPropertyName("symbol")] public string? Symbol { get; set; }
    [JsonPropertyName("quantity")] public int Quantity { get; set; }
    [JsonPropertyName("costPrice")] public decimal CostPrice { get; set; }
    [JsonPropertyName("ltp")] public decimal Ltp { get; set; }
    [JsonPropertyName("marketVal")] public decimal MarketVal { get; set; }
    [JsonPropertyName("pl")] public decimal Pl { get; set; }
}

/// <summary>data/history: candles = [[epochSeconds, open, high, low, close, volume], …]</summary>
public class FyersHistory : FyersEnvelope { [JsonPropertyName("candles")] public List<List<decimal>> Candles { get; set; } = new(); }
public class FyersQuotes : FyersEnvelope { [JsonPropertyName("d")] public List<FyersQuote> D { get; set; } = new(); }
public class FyersQuote
{
    [JsonPropertyName("n")] public string? N { get; set; }
    [JsonPropertyName("s")] public string? S { get; set; }
    [JsonPropertyName("v")] public FyersQuoteValues? V { get; set; }
}
public class FyersQuoteValues
{
    [JsonPropertyName("lp")] public decimal Lp { get; set; }
    [JsonPropertyName("ch")] public decimal Ch { get; set; }
    [JsonPropertyName("chp")] public decimal Chp { get; set; }
    [JsonPropertyName("open_price")] public decimal Open { get; set; }
    [JsonPropertyName("high_price")] public decimal High { get; set; }
    [JsonPropertyName("low_price")] public decimal Low { get; set; }
    [JsonPropertyName("prev_close_price")] public decimal PrevClose { get; set; }
    [JsonPropertyName("volume")] public long Volume { get; set; }
    [JsonPropertyName("short_name")] public string? ShortName { get; set; }
}
