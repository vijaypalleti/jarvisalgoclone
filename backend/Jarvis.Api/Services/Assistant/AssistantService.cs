using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jarvis.Api.Data;

namespace Jarvis.Api.Services.Assistant;

public record ChatTurn(string Role, string Content);          // "user" | "assistant"
public record ChatRequest(List<ChatTurn> Messages, string? Page);
public record ToolUse(string Name, string Args, long Ms, bool Ok);
public record ChatResponse(string Answer, List<ToolUse> Tools, string Model, long Ms, string? Error, UiAction? Action = null);
/// <summary>Browser instruction from show_on_screen: open Route?Query, apply Focus filters, highlight each [data-ai-target=Key].</summary>
public record UiTarget(string Key, string Label);
public record UiAction(string Route, Dictionary<string, string> Query, List<UiTarget> Targets, Dictionary<string, string>? Focus);

/// <summary>
/// Natural-language questions → Groq (OpenAI-compatible chat completions) with tool calling:
///   model asks for a tool → we run it on our own data → send the result back → repeat (max 5) → final answer.
/// The Groq key stays here (user-secrets "Groq:ApiKey"); the browser never sees it. Tools are read-only.
/// </summary>
public class AssistantService(IHttpClientFactory http, IConfiguration config, AssistantTools tools, FyersClient fyers,
    RealtimeBus bus, GroqKeyPool keys, ILogger<AssistantService> log)
{
    public string Model => config["Groq:Model"] ?? "openai/gpt-oss-20b";
    /// <summary>Primary model, then fallbacks (Groq limits are per model, so a fallback has its own daily allowance).</summary>
    public List<string> Models => new[] { Model }
        .Concat(config.GetSection("Groq:FallbackModels").GetChildren().Select(c => c.Value ?? ""))
        .Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().ToList();
    private string? _usedModel;
    public bool Configured => keys.Count > 0;
    public GroqKeyPool Keys => keys;
    private const int MaxToolRounds = 5;

    public async Task<ChatResponse> ChatAsync(ChatRequest req, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var used = new List<ToolUse>();
        if (!Configured)
            return new(string.Empty, used, Model, 0, "The assistant is not configured: run  dotnet user-secrets set \"Groq:ApiKeys:0\" \"<key>\"  (and :1, :2, :3 for more keys) in backend/Jarvis.Api and restart the API.");

        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = await SystemPromptAsync(req.Page) } };
        foreach (var m in req.Messages.TakeLast(12).Where(m => m.Role is "user" or "assistant" && !string.IsNullOrWhiteSpace(m.Content)))
            messages.Add(new JsonObject { ["role"] = m.Role, ["content"] = m.Content.Length > 4000 ? m.Content[..4000] : m.Content });
        var question = req.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? "";
        // "navigate / show / take me to / open / point at …": the user wants the page, not just text. Small models sometimes
        // answer from a list tool instead, so say it explicitly for this turn (typos like "naviagate" included).
        if (NavigationIntent.IsMatch(question))
            messages.Add(new JsonObject { ["role"] = "system", ["content"] =
                "The user asked to navigate to / show / point at something on screen. You MUST call show_on_screen for it " +
                "(e.g. target subscriptions_expiring for expiring subscriptions, subscription for one subscriber), then answer briefly." });
        // tool routing looks at the last two user messages, so follow-ups ("and for 8616?") keep the context
        var recentUserText = string.Join(" ", req.Messages.Where(m => m.Role == "user").TakeLast(2).Select(m => m.Content));

        var toolText = new StringBuilder();   // everything the tools returned, to verify amounts in the answer
        var corrected = false;
        try
        {
            for (var round = 0; round <= MaxToolRounds + 1; round++)
            {
                var msg = await CompleteAsync(messages, round < MaxToolRounds ? recentUserText : null, ct);
                var calls = msg["tool_calls"] as JsonArray;
                if (calls is null || calls.Count == 0)
                {
                    var answer = msg["content"]?.GetValue<string>()?.Trim();
                    if (string.IsNullOrEmpty(answer)) answer = "Sorry, I couldn't produce an answer. Please rephrase.";

                    // small models sometimes mangle digits while copying ("₹304.20" → "₹30,404.20"):
                    // every ₹/$ amount in the answer must exist in the tool results, else ask for one rewrite
                    var bad = UnverifiedAmounts(answer, toolText.ToString());
                    if (bad.Count > 0 && !corrected && used.Count > 0)
                    {
                        corrected = true;
                        log.LogInformation("Assistant answer had unverified amounts {Amounts}; asking for a correction", string.Join(", ", bad));
                        messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = answer });
                        messages.Add(new JsonObject
                        {
                            ["role"] = "user",
                            ["content"] = $"Check your numbers: {string.Join(", ", bad)} do not appear in the tool results. " +
                                          "Rewrite the same answer using the amounts exactly as the tools returned them. Reply with the corrected answer only.",
                        });
                        continue;
                    }
                    if (bad.Count > 0) answer += "\n\n_Note: some amounts could not be verified against live data._";
                    Log(question, used, sw.ElapsedMilliseconds, true);
                    return new(answer, used, _usedModel ?? Model, sw.ElapsedMilliseconds, null, tools.Action);
                }

                // keep the assistant's tool request in the transcript, then answer each call
                messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = msg["content"]?.DeepClone() ?? "", ["tool_calls"] = calls.DeepClone() });
                foreach (var call in calls.OfType<JsonObject>())
                {
                    var name = call["function"]?["name"]?.GetValue<string>() ?? "";
                    var rawArgs = call["function"]?["arguments"]?.GetValue<string>() ?? "{}";
                    var t = Stopwatch.StartNew();
                    object result; var ok = true;
                    try { result = await tools.RunAsync(name, ParseArgs(rawArgs)); }
                    catch (Exception ex) { ok = false; result = new { error = ex.Message }; log.LogWarning(ex, "Tool {Tool} failed", name); }
                    used.Add(new ToolUse(name, rawArgs, t.ElapsedMilliseconds, ok));
                    var json = JsonSerializer.Serialize(result, ToolJson);   // keeps ₹ readable (default escapes it to ₹)
                    toolText.Append(json).Append('\n');
                    messages.Add(new JsonObject
                    {
                        ["role"] = "tool", ["tool_call_id"] = call["id"]?.GetValue<string>() ?? name,
                        ["content"] = json,
                    });
                }
            }
            return new("I needed too many steps for that. Try a more specific question.", used, Model, sw.ElapsedMilliseconds, null);
        }
        catch (GroqException ex)
        {
            Log(question, used, sw.ElapsedMilliseconds, false);
            return new(string.Empty, used, Model, sw.ElapsedMilliseconds, ex.Message);
        }
    }

    /// <param name="toolsFor">user text used to pick relevant tools; null = no tools this round</param>
    private async Task<JsonObject> CompleteAsync(JsonArray messages, string? toolsFor, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = Model,
            ["messages"] = messages.DeepClone(),
            ["temperature"] = 0.2,
            ["max_completion_tokens"] = 1200,
            ["reasoning_effort"] = config["Groq:ReasoningEffort"] ?? "low",   // fewer hidden reasoning tokens → faster, fewer rate-limit hits
        };
        if (toolsFor is not null) { body["tools"] = AssistantTools.Definitions(toolsFor); body["tool_choice"] = "auto"; }

        // Failover order: every key for the primary model, then every key for each fallback model.
        //  • 429 → park that key+model until Groq's "try again in …", try the next key / next model at once
        //  • 401 → park the key (all models) for an hour
        //  • model missing/decommissioned → skip that model for 6 h
        //  • nothing usable → wait for the soonest key on any model (≤ 20 s, twice), else a clear message
        //  • 400 tool_use_failed (malformed tool call) → retry once
        var toolRetry = false; var waits = 0; string? lastLimit = null;
        while (true)
        {
            DateTime? soonestAll = null;
            foreach (var model in Models.Where(keys.ModelAvailable))
            {
                for (var attempt = 0; attempt < keys.Count * 2 + 2; attempt++)
                {
                    var pick = keys.Next(model, out var soonest);
                    if (pick is null) { if (soonest is { } t && (soonestAll is null || t < soonestAll)) soonestAll = t; break; }
                    var (idx, key) = pick.Value;

                    var payload = body.DeepClone().AsObject();
                    payload["model"] = model;
                    if (!model.StartsWith("openai/gpt-oss")) payload.Remove("reasoning_effort");   // only gpt-oss accepts it

                    var client = http.CreateClient("groq");
                    using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
                    {
                        Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
                    };
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    using var res = await client.SendAsync(req, ct);
                    var text = await res.Content.ReadAsStringAsync(ct);
                    if (res.IsSuccessStatusCode)
                    {
                        if (model != Model) log.LogInformation("Answered by fallback model {Model}", model);
                        _usedModel = model;
                        return JsonNode.Parse(text)?["choices"]?[0]?["message"]?.AsObject() ?? throw new GroqException("Empty response from Groq.");
                    }

                    var err = TryError(text);
                    if (res.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        var after = RetryAfter(res, err) ?? TimeSpan.FromSeconds(10);
                        var why = err.Contains("per day") ? "daily token limit" : err.Contains("per minute") ? "per-minute limit" : "rate limited";
                        keys.MarkRateLimited(idx, model, after, why);
                        lastLimit = $"{why} on {model}";
                        log.LogInformation("Groq {Key} {Model}: {Why} for {Seconds:N0}s ({Error})", keys.Hint(idx), model, why, after.TotalSeconds, err);
                        continue;
                    }
                    if (res.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        keys.MarkInvalid(idx);
                        log.LogWarning("Groq {Key} rejected (401); parked for 1 h", keys.Hint(idx));
                        if (keys.AllInvalid) throw new GroqException("Groq rejected every configured API key. Replace them with dotnet user-secrets set \"Groq:ApiKeys:0\" …");
                        continue;
                    }
                    if (res.StatusCode == HttpStatusCode.NotFound || err.Contains("model_not_found") || err.Contains("decommissioned") || err.Contains("does not exist"))
                    {
                        keys.MarkModelDown(model);
                        log.LogWarning("Groq model {Model} unavailable ({Error}); skipping it", model, err);
                        break;
                    }
                    if (res.StatusCode == HttpStatusCode.BadRequest && !toolRetry && text.Contains("tool_use_failed")) { toolRetry = true; attempt--; continue; }
                    log.LogWarning("Groq {Status} on {Key} {Model}: {Error}", (int)res.StatusCode, keys.Hint(idx), model, err);
                    throw new GroqException($"Groq error {(int)res.StatusCode}: {err}");
                }
            }

            var wait = (soonestAll ?? DateTime.UtcNow.AddSeconds(30)) - DateTime.UtcNow;
            if (waits >= 2 || wait > TimeSpan.FromSeconds(20))
                throw new GroqException($"Groq limit reached ({lastLimit ?? "all keys/models busy"}). " +
                    $"Try again in {FormatWait(wait)}. More capacity: add fallback models (Groq:FallbackModels) or upgrade the Groq tier.");
            waits++;
            log.LogInformation("All Groq keys/models cooling; waiting {Seconds:N1}s", wait.TotalSeconds);
            await Task.Delay(wait + TimeSpan.FromMilliseconds(250), ct);
        }
    }

    private static string FormatWait(TimeSpan t) =>
        t.TotalSeconds < 90 ? $"{Math.Max(1, t.TotalSeconds):0} s" : t.TotalMinutes < 90 ? $"{t.TotalMinutes:0} min" : $"{t.TotalHours:0.#} h";

    private async Task<string> SystemPromptAsync(string? page)
    {
        string owner = "Vijay";
        try { if (fyers.Configured) owner = (await fyers.Profile()).Data?.Name ?? owner; } catch { /* token expired → tool will say so */ }
        var now = MockDataStore.NowIst;
        return $"""
            You are Jarvis Assistant inside the Jarvis Algo OMS admin console. Answer questions about live trading data by calling tools.

            Data you can reach (all read-only):
            1. Vijay's REAL Fyers broker account (account holder: {owner}). Use get_fyers_account whenever the user says Vijay, "my account", Fyers, or asks about their own funds/P&L/positions/holdings/orders.
            2. The OMS demo book: broker accounts identified by client id (e.g. 1023, 8616), positions, P&L, signals, open issues, containers.
               "Broker account 1023" / "client 1023" means client id 1023: use find_broker_account / get_positions.
            3. Live BTCUSD from Delta Exchange: get_btc_price.

            Rules:
            - Always call a tool for any number. Never guess or reuse numbers from memory when they may have changed.
            - If a tool returns found=false or an error (e.g. Fyers session expired), say so plainly and suggest the fix.
            - When the user asks to show / point at / highlight / take them to something (a failing container, a signal, an account), call show_on_screen; the console then opens that page and highlights it. Say what is highlighted and why in one or two sentences. It only shows things; it never restarts or changes anything.
            - You cannot place, modify or exit orders. If asked, say this assistant is read-only and point to the console page.
            - Money: OMS and Fyers amounts are INR (₹, Indian grouping like ₹1,23,456). BTC prices are US dollars: write $74,069, never ₹. Times are IST.
            - Tools return money already formatted (e.g. "-₹2,603.25", "₹1,23,456.00"). Copy these strings exactly; never re-group or re-calculate digits. Never add up rows yourself to make a total: lists are often truncated. Use the tool's total field (e.g. grandTotal) or say no total was given.
            - When a tool result has a 'summary' field, base your answer on it (you may format it as a short list).
            - Be brief: 1–3 sentences, or a short bullet list / small table when listing items. Put key numbers in **bold**.
            Now: {now:dd MMM yyyy, HH:mm} IST.{(string.IsNullOrWhiteSpace(page) ? "" : $" The user is on page: {page}.")}
            """;
    }

    private static readonly System.Text.RegularExpressions.Regex NavigationIntent = new(
        @"\b(navi\w*|nagiv\w*|show\s+me|take\s+me|go\s+to|open\s+(the|that|those|his|her)|point\s+(at|to|the|out)?|highlight|locate)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly JsonSerializerOptions ToolJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // tool results go to the model, not into HTML
    };

    private static readonly System.Text.RegularExpressions.Regex Amount =
        new(@"[₹$]\s?-?\d[\d,]*(?:\.\d+)?", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex Number =
        new(@"\d[\d,]*(?:\.\d+)?", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>₹/$ amounts in the answer whose value appears nowhere in the tool output (formatted or raw).</summary>
    private static List<string> UnverifiedAmounts(string answer, string toolOutput)
    {
        static decimal? Val(string s) =>
            decimal.TryParse(s.Replace(",", "").Replace("₹", "").Replace("$", "").Replace("-", "").Trim(),
                System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) ? Math.Round(v, 2) : null;
        // accept exact values and values the model rounded to whole units (₹74,060 for 74,060.47)
        var known = Number.Matches(toolOutput).Select(m => Val(m.Value)).Where(v => v is not null)
                          .SelectMany(v => new[] { v!.Value, Math.Round(v.Value, 0) }).ToHashSet();
        return Amount.Matches(answer).Select(m => m.Value.Trim())
            .Where(a => Val(a) is { } v && !known.Contains(v))
            .Distinct().ToList();
    }

    private static JsonObject ParseArgs(string raw)
    {
        try { return JsonNode.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { return new JsonObject(); }
    }

    /// <summary>Retry-After header, or "Please try again in 7.36s" / "in 1m2.5s" from the error message.</summary>
    private static TimeSpan? RetryAfter(HttpResponseMessage res, string err)
    {
        if (res.Headers.RetryAfter?.Delta is { } d) return d;
        var m = System.Text.RegularExpressions.Regex.Match(err, @"try again in (?:(\d+)m)?([\d.]+)s");
        if (!m.Success) return TimeSpan.FromSeconds(5);
        var mins = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0;
        return TimeSpan.FromMinutes(mins) + TimeSpan.FromSeconds(double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string TryError(string text)
    {
        try { return JsonNode.Parse(text)?["error"]?["message"]?.GetValue<string>() ?? text; }
        catch { return text.Length > 300 ? text[..300] : text; }
    }

    private void Log(string question, List<ToolUse> used, long ms, bool ok) =>
        bus.Publish("ai.chat", "assistant", new { question, tools = string.Join(",", used.Select(u => u.Name)), ms, ok, model = Model });
}

public class GroqException(string message) : Exception(message);
