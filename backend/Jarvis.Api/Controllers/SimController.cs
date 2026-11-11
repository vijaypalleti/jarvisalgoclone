using Jarvis.Api.Data;
using Jarvis.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jarvis.Api.Controllers;

/// <summary>Controls the demo market simulator (fake data only — never touches Fyers/Delta).</summary>
[ApiController]
[Route("api/sim")]
public class SimController(MockDataStore store, MarketSimulator sim) : ControllerBase
{
    [HttpGet]
    public SimStatus Status() => store.GetSimStatus(sim.TickMs);

    [HttpPost("pause")]
    public SimStatus Pause() { store.SimPaused = true; return Status(); }

    [HttpPost("resume")]
    public SimStatus Resume() { store.SimPaused = false; return Status(); }

    /// <summary>calm | normal | volatile | rally | crash</summary>
    [HttpPost("scenario/{name}")]
    public ActionResult<SimStatus> Scenario(string name)
    {
        if (!Enum.TryParse<SimScenario>(name, true, out var s)) return BadRequest(new { error = "Use calm, normal, volatile, rally or crash" });
        store.SetScenario(s);
        store.SimPaused = false;
        return Status();
    }
}
