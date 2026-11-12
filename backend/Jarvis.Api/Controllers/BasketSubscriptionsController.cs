using Jarvis.Api.Data;
using Microsoft.AspNetCore.Mvc;

namespace Jarvis.Api.Controllers;

/// <summary>
/// Basket Management → Basket Subscriptions: every user's subscriptions in one list (demo data).
/// Changes go through the per-user endpoints in UsersController (pause / activate / extend / cancel / multiplier / delete).
/// </summary>
[ApiController]
[Route("api/console/basket-subscriptions")]
public class BasketSubscriptionsController(MockDataStore store) : ControllerBase
{
    [HttpGet]
    public SubscriptionPage List([FromQuery] string? status, [FromQuery] string? search, [FromQuery] string? basket, [FromQuery] string? broker,
        [FromQuery] int? expiringDays, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => store.ListSubscriptions(status, search, basket, broker, expiringDays, page, pageSize);
}
