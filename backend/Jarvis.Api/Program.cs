using System.Text.Json.Serialization;
using Jarvis.Api.Data;
using Jarvis.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<MockDataStore>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<Jarvis.Api.Controllers.FyersAlertState>();
builder.Services.AddHttpClient<FyersClient>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Fyers:BaseUrl"] ?? "https://api-t1.fyers.in/");
    c.Timeout = TimeSpan.FromSeconds(15);
});

// Delta Exchange — public market data, no login
builder.Services.AddHttpClient("delta", c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Delta:RestUrl"] ?? "https://api.india.delta.exchange/");
    c.Timeout = TimeSpan.FromSeconds(15);
    // Delta's CDN rejects requests without a User-Agent (returns an HTML page)
    c.DefaultRequestHeaders.UserAgent.ParseAdd("JarvisOmsClone/1.0");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
// Redis = real-time layer, SQL Server = history (Delta feed → Redis → MarketPersister → SQL)
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
    StackExchange.Redis.ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false"));
builder.Services.AddSingleton<RedisMarketStore>();
builder.Services.AddSingleton<MarketDb>();

builder.Services.AddSingleton<DeltaMarketFeed>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DeltaMarketFeed>());
builder.Services.AddSingleton<MarketPersister>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MarketPersister>());
builder.Services.AddSignalR();

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never);

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

app.UseCors();
app.MapControllers();
app.MapHub<MarketHub>("/hubs/market");
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", time = MockDataStore.NowIst }));

app.Run();
