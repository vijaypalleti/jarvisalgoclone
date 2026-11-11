using Jarvis.Api.Data;

namespace Jarvis.Api.Services;

/// <summary>
/// Drives <see cref="MockDataStore.SimTick"/> on a fixed interval so the fake OMS data keeps moving, and publishes
/// what changed to the real-time bus (Redis → SQL, SignalR):
///   sim.spot   every tick · sim.event   every event · sim.pnl   per account every Realtime:SimPnlSeconds
/// </summary>
public sealed class MarketSimulator(MockDataStore store, RealtimeBus bus, IConfiguration config, ILogger<MarketSimulator> log) : BackgroundService
{
    public int TickMs { get; } = Math.Clamp(config.GetValue("Simulation:TickMs", 2000), 250, 60_000);
    private readonly TimeSpan _pnlEvery = TimeSpan.FromSeconds(Math.Max(1, config.GetValue("Realtime:SimPnlSeconds", 10)));
    private DateTime _lastPnl = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!config.GetValue("Simulation:Enabled", true)) { store.SimPaused = true; log.LogInformation("Demo simulator disabled"); return; }
        if (Enum.TryParse<SimScenario>(config["Simulation:Scenario"], true, out var s)) store.SetScenario(s);
        log.LogInformation("Demo simulator running every {Ms} ms ({Scenario})", TickMs, store.Scenario);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickMs));
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (store.SimPaused) continue;
            try
            {
                store.SimTick();
                Publish();
            }
            catch (Exception ex) { log.LogWarning(ex, "Simulator tick failed"); }
        }
    }

    private void Publish()
    {
        var pnlDue = DateTime.UtcNow - _lastPnl >= _pnlEvery;
        var batch = store.DrainForPublish(pnlDue);

        bus.Publish("sim.spot", "NIFTY", new { spot = batch.Spot, changePct = batch.ChangePct, scenario = batch.Scenario });

        foreach (var e in batch.Events)
            bus.Publish("sim.event", e.ClientId ?? "system",
                new { kind = e.Kind, text = e.Text, clientId = e.ClientId, symbol = e.Symbol, amount = e.Amount, timeIst = e.Time });

        if (batch.Pnl is { } pnl)
        {
            _lastPnl = DateTime.UtcNow;
            foreach (var a in pnl)
                bus.Publish("sim.pnl", a.ClientId, new
                {
                    name = a.Name, broker = a.Broker, realized = a.Realized, unrealized = a.Unrealized,
                    total = a.Total, openPositions = a.OpenPositions,
                });
        }
    }
}
