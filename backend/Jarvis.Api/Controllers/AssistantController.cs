using Jarvis.Api.Services.Assistant;
using Microsoft.AspNetCore.Mvc;

namespace Jarvis.Api.Controllers;

/// <summary>Floating chat assistant: natural-language questions answered with tool calls on live app data.</summary>
[ApiController]
[Route("api/assistant")]
public class AssistantController(AssistantService assistant) : ControllerBase
{
    [HttpGet("status")]
    /// <summary>Configured? model + per-key health (key values are never returned, only "…last4").</summary>
    public object Status() => new { configured = assistant.Configured, model = assistant.Model, models = assistant.Models, provider = "Groq", keys = assistant.Keys.Status() };

    [HttpPost("chat")]
    public async Task<ActionResult<ChatResponse>> Chat([FromBody] ChatRequest req, CancellationToken ct)
    {
        if (req.Messages is not { Count: > 0 }) return BadRequest(new { error = "messages required" });
        return await assistant.ChatAsync(req, ct);
    }
}
