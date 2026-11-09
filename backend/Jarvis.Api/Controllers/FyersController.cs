using System.Collections.Concurrent;
using Jarvis.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jarvis.Api.Controllers;

/// <summary>Live, read-only Fyers data mapped into UI-friendly shapes.</summary>
[ApiController]
[Route("api/fyers")]
public class FyersController(FyersClient fyers) : ControllerBase
{
    public const string DefaultSymbols = "NSE:NIFTY50-INDEX,BSE:SENSEX-INDEX,NSE:NIFTYBANK-INDEX,NSE:FINNIFTY-INDEX";

    public static string OrderStatus(int s) => s switch
    {
        1 => "CANCELLED", 2 => "COMPLETED", 4 => "TRANSIT", 5 => "REJECTED", 6 => "PENDING", 7 => "EXPIRED", _ => $"STATUS {s}"
    };
    public static string OrderType(int t) => t switch { 1 => "LIMIT", 2 => "MARKET", 3 => "SL-M", 4 => "SL-L", _ => t.ToString() };
    public static string Side(int s) => s == 1 ? "BUY" : s == -1 ? "SELL" : "—";

    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var expiry = fyers.TokenExpiry;
        if (!fyers.Configured)
            return Ok(new { configured = false, connected = false, error = "Fyers credentials are not configured." });
        try
        {
            var p = await fyers.Profile();
            return Ok(new
            {
                configured = true, connected = true, clientId = fyers.ClientId,
                name = p.Data?.Name, displayName = p.Data?.DisplayName, fyId = p.Data?.FyId,
                tokenExpiresAt = expiry, error = (string?)null
            });
        }
        catch (FyersException ex)
        {
            return Ok(new { configured = true, connected = false, clientId = fyers.ClientId, tokenExpiresAt = expiry, error = ex.Message });
        }
    }

    [HttpPost("token")]
    public IActionResult SetToken([FromBody] TokenRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ClientId) || string.IsNullOrWhiteSpace(req.AccessToken)) return BadRequest("clientId and accessToken are required");
        fyers.SetCredentials(req.ClientId, req.AccessToken);
        return NoContent();
    }
    public record TokenRequest(string ClientId, string AccessToken);

    [HttpGet("funds")]
    public Task<IActionResult> Funds() => Wrap(async () =>
        (await fyers.Funds()).FundLimit.Select(f => new { f.Id, f.Title, f.EquityAmount, f.CommodityAmount }));

    [HttpGet("positions")]
    public Task<IActionResult> Positions() => Wrap(async () =>
    {
        var p = await fyers.Positions();
        return new
        {
            overall = p.Overall,
            items = p.NetPositions.Select(x => new
            {
                x.Id, x.Symbol, x.ProductType, side = Side(x.Side), x.NetQty, x.NetAvg, x.BuyQty, x.BuyAvg,
                x.SellQty, x.SellAvg, x.Ltp, x.RealizedProfit, x.UnrealizedProfit, x.Pl
            })
        };
    });

    [HttpGet("orders")]
    public Task<IActionResult> Orders() => Wrap(async () =>
        (await fyers.Orders()).OrderBook
            .OrderByDescending(o => o.OrderDateTime)
            .Select(o => new
            {
                o.Id, o.ExchOrdId, o.Symbol, o.Qty, o.FilledQty, o.RemainingQuantity,
                status = OrderStatus(o.Status), side = Side(o.Side), type = OrderType(o.Type),
                o.ProductType, o.LimitPrice, o.StopPrice, o.TradedPrice, o.OrderDateTime, o.Message, o.OrderTag
            }));

    [HttpGet("trades")]
    public Task<IActionResult> Trades() => Wrap(async () =>
        (await fyers.Trades()).TradeBook
            .OrderByDescending(t => t.OrderDateTime)
            .Select(t => new { t.TradeNumber, t.OrderNumber, t.Symbol, side = Side(t.Side), t.TradedQty, t.TradePrice, t.TradeValue, t.ProductType, t.OrderDateTime }));

    [HttpGet("holdings")]
    public Task<IActionResult> Holdings() => Wrap(async () => (await fyers.Holdings()).Holdings);

    [HttpGet("quotes")]
    public Task<IActionResult> Quotes([FromQuery] string? symbols) => Wrap(async () =>
        (await fyers.Quotes(string.IsNullOrWhiteSpace(symbols) ? DefaultSymbols : symbols)).D
            .Select(q => new
            {
                symbol = q.N, name = q.V?.ShortName ?? q.N, ltp = q.V?.Lp, change = q.V?.Ch, changePct = q.V?.Chp,
                open = q.V?.Open, high = q.V?.High, low = q.V?.Low, prevClose = q.V?.PrevClose, volume = q.V?.Volume
            }));

    private async Task<IActionResult> Wrap<T>(Func<Task<T>> fn)
    {
        try { return Ok(await fn()); }
        catch (FyersException ex) { return StatusCode(ex.Status, new { error = ex.Message }); }
        catch (HttpRequestException ex) { return StatusCode(502, new { error = $"Fyers unreachable: {ex.Message}" }); }
    }
}

/// <summary>Remembers which live Fyers rejections have been acknowledged in the console (in-memory).</summary>
public class FyersAlertState
{
    public ConcurrentDictionary<string, bool> Acked { get; } = new();
}
