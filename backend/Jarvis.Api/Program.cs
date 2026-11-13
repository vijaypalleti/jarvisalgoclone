using Microsoft.AspNetCore.DataProtection;
using System.Text.Json.Serialization;
using Jarvis.Api.Data;
using Jarvis.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<MockDataStore>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<Jarvis.Api.Controllers.FyersAlertState>();
// In-app Fyers login: encrypted token file (Data Protection keys are DPAPI-protected for the Windows user)
builder.Services.AddDataProtection().SetApplicationName("JarvisDemo");
builder.Services.AddSingleton<FyersTokenStore>();
builder.Services.AddHttpClient<FyersClient>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Fyers:BaseUrl"] ?? "https://api-t1.fyers.in/");
    c.Timeout = TimeSpan.FromSeconds(15);
});

// Delta Exchange — public market data, no login
// Fyers PUBLIC symbol master (name → NSE:RELIANCE-EQ); no login, no account data
builder.Services.AddHttpClient("fyers-public", c => { c.BaseAddress = new Uri("https://public.fyers.in/"); c.Timeout = TimeSpan.FromSeconds(60); });
builder.Services.AddSingleton<FyersSymbols>();
builder.Services.AddHttpClient("delta", c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Delta:RestUrl"] ?? "https://api.india.delta.exchange/");
    c.Timeout = TimeSpan.FromSeconds(15);
    // Delta's CDN rejects requests without a User-Agent (returns an HTML page)
    c.DefaultRequestHeaders.UserAgent.ParseAdd("JarvisOmsClone/1.0");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
// Dev: Redis runs in WSL, which sleeps when idle → start it with the API (registered first so it starts first)
builder.Services.AddHostedService<WslRedisStarter>();

// Redis = real-time layer, SQL Server = history (Delta feed → Redis → MarketPersister → SQL)
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
    StackExchange.Redis.ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false"));
builder.Services.AddSingleton<RedisMarketStore>();
builder.Services.AddSingleton<MarketDb>();

builder.Services.AddSingleton<DeltaMarketFeed>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DeltaMarketFeed>());
builder.Services.AddSingleton<MarketPersister>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MarketPersister>());
// Real-time bus for every other source: Publish → Redis (latest + stream) → RealtimePersister → SQL, + SignalR
builder.Services.AddSingleton<RealtimeBus>();
builder.Services.AddSingleton<RealtimeDb>();
builder.Services.AddSingleton<RealtimePersister>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RealtimePersister>());
builder.Services.AddSingleton<FyersPoller>();   // read-only Fyers → bus, even when no page is open
builder.Services.AddHostedService(sp => sp.GetRequiredService<FyersPoller>());

// AI assistant (floating chat): Groq "openai/gpt-oss-20b" with read-only tool calling. Key: user-secrets "Groq:ApiKey".
builder.Services.AddHttpClient("groq", c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Groq:BaseUrl"] ?? "https://api.groq.com/openai/v1/");
    c.Timeout = TimeSpan.FromSeconds(40);
});
builder.Services.AddSingleton<Jarvis.Api.Services.Assistant.GroqKeyPool>();   // 1..N keys with failover (Groq:ApiKeys:0..N)
builder.Services.AddScoped<Jarvis.Api.Services.Assistant.AssistantTools>();
builder.Services.AddScoped<Jarvis.Api.Services.Assistant.AssistantService>();

// Demo simulator: keeps the fake OMS data (P&L, positions, exits, signals, containers) moving
builder.Services.AddSingleton<MarketSimulator>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MarketSimulator>());
builder.Services.AddSignalR();

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never);

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

// Resume the last in-app Fyers login (if still valid) so a restart doesn't need a new session
using (var scope = app.Services.CreateScope())
{
    var saved = scope.ServiceProvider.GetRequiredService<FyersTokenStore>().Load();
    if (saved is not null && FyersClient.JwtExpiry(saved.AccessToken) > DateTimeOffset.UtcNow)
        scope.ServiceProvider.GetRequiredService<FyersClient>().SetCredentials(saved.ClientId, saved.AccessToken);
}

app.UseCors();
app.MapControllers();
app.MapHub<MarketHub>("/hubs/market");
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", time = MockDataStore.NowIst }));

app.Run();
