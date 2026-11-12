using Jarvis.Api.Data;
using Microsoft.AspNetCore.Mvc;

namespace Jarvis.Api.Controllers;

/// <summary>
/// Performance → User Management. Demo data: every change here edits the in-memory demo store only
/// (no broker, no real accounts). Broker login / credentials / IP configuration are not available in the demo.
/// </summary>
[ApiController]
[Route("api/console/users")]
public class UsersController(MockDataStore store) : ControllerBase
{
    public record UserEdit(string Name, string Email, string Mobile);
    public record StatusChange(bool Active);
    public record AccountChange(bool? AlgoOn, int? Lot, bool? Active);
    public record MultiplierChange(int Multiplier);
    public record NewSubscription(string Basket, int Months, string ClientId, int Multiplier);

    [HttpGet]
    public SystemUserList List([FromQuery] string? search) => store.ListUsers(search);

    [HttpGet("by-client/{clientId}")]
    public ActionResult<object> ByClient(string clientId) => store.UserIdForClient(clientId) is { } id ? new { id } : NotFound();

    [HttpGet("{id:int}")]
    public ActionResult<UserWorkspace> Get(int id) => Ok404(store.GetUserWorkspace(id));

    [HttpPost]
    public ActionResult<object> Create(UserEdit e) => store.CreateUser(e.Name, e.Email, e.Mobile) is { } id ? new { id } : BadRequest(new { error = "Name and email are required." });

    [HttpPut("{id:int}")]
    public ActionResult<UserWorkspace> Update(int id, UserEdit e) => Ok400(store.UpdateUser(id, e.Name, e.Email, e.Mobile));

    [HttpPost("{id:int}/status")]
    public ActionResult<UserWorkspace> Status(int id, StatusChange s) => Ok400(store.SetUserStatus(id, s.Active));

    [HttpPost("{id:int}/accounts/{clientId}")]
    public ActionResult<UserWorkspace> Account(int id, string clientId, AccountChange c) => Ok400(store.UpdateBrokerAccount(id, clientId, c.AlgoOn, c.Lot, c.Active));

    [HttpPost("{id:int}/subscriptions")]
    public ActionResult<UserWorkspace> AddSub(int id, NewSubscription n) => Ok400(store.AddSubscription(id, n.Basket, n.Months, n.ClientId, n.Multiplier));

    [HttpPost("{id:int}/subscriptions/{subId:int}/{op}")]   // not {action}: that name is reserved by ASP.NET routing
    public ActionResult<UserWorkspace> SubAction(int id, int subId, string op, [FromQuery] int months = 1) => Ok400(store.SubscriptionAction(id, subId, op, months));

    [HttpDelete("{id:int}/subscriptions/{subId:int}")]
    public ActionResult<UserWorkspace> SubDelete(int id, int subId) => Ok400(store.DeleteSubscription(id, subId));

    [HttpPut("{id:int}/subscriptions/{subId:int}")]
    public ActionResult<UserWorkspace> SubUpdate(int id, int subId, MultiplierChange m) => Ok400(store.UpdateSubscription(id, subId, m.Multiplier));

    [HttpPost("{id:int}/baskets/multiplier")]
    public ActionResult<UserWorkspace> BasketMult(int id, [FromQuery] string basket, MultiplierChange m) => Ok400(store.SetBasketMultiplier(id, basket, m.Multiplier));

    [HttpPost("{id:int}/payments/{paymentId:int}/paid")]
    public ActionResult<UserWorkspace> PaymentPaid(int id, int paymentId) => Ok400(store.MarkPaymentPaid(id, paymentId));

    private ActionResult<UserWorkspace> Ok404(UserWorkspace? w) => w is null ? NotFound() : w;
    private ActionResult<UserWorkspace> Ok400(UserWorkspace? w) => w is null ? BadRequest(new { error = "Not found or invalid value." }) : w;
}
