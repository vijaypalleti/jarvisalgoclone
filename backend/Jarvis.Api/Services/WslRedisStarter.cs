using System.Diagnostics;
using System.Net.Sockets;

namespace Jarvis.Api.Services;

/// <summary>
/// Local-dev helper: Redis lives inside WSL Ubuntu, and WSL shuts Ubuntu down a few seconds after the last
/// Windows-side wsl.exe process exits, so Redis goes down with it. When the API starts and Redis isn't reachable,
/// this starts Ubuntu (Redis is enabled as a systemd service there, so it boots with it) and keeps one hidden
/// <c>wsl.exe … sleep infinity</c> process alive for as long as the API runs, then ends it when the API stops.
/// Windows only; turn off with Redis:AutoStartWsl=false (e.g. when Redis runs elsewhere).
/// </summary>
public sealed class WslRedisStarter(IConfiguration config, ILogger<WslRedisStarter> log) : IHostedService
{
    private readonly bool _enabled = config.GetValue("Redis:AutoStartWsl", true);
    private readonly string _distro = config["Redis:WslDistro"] ?? "Ubuntu";
    private Process? _keepAlive;

    public string State { get; private set; } = "not started";

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_enabled || !OperatingSystem.IsWindows()) { State = "disabled"; return; }
        if (await PortOpenAsync()) { State = "redis already running"; return; }

        try
        {
            log.LogInformation("Redis not reachable on localhost:6379 — starting WSL '{Distro}' (Redis starts with it)…", _distro);
            _keepAlive = Process.Start(new ProcessStartInfo("wsl.exe", ["-d", _distro, "-e", "sleep", "infinity"])
            {
                CreateNoWindow = true, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true,
            });

            // systemd starts redis-server on boot; give it up to ~10 s
            if (await WaitForPortAsync(TimeSpan.FromSeconds(10), ct)) { State = "started WSL; redis up"; log.LogInformation("Redis is up"); return; }

            // not a systemd setup / service disabled → start it explicitly
            log.LogInformation("Redis didn't come up with WSL — running 'service redis-server start'");
            await RunAsync("wsl.exe", ["-d", _distro, "-u", "root", "--", "service", "redis-server", "start"], ct);
            State = await WaitForPortAsync(TimeSpan.FromSeconds(8), ct) ? "started WSL + redis-server" : "WSL started but Redis still unreachable";
            if (State.Contains("unreachable")) log.LogWarning("Redis still unreachable — live feed works, saving to SQL pauses until it's up");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            State = $"could not start WSL: {ex.Message}";
            log.LogWarning("Could not start WSL/Redis automatically: {Message}", ex.Message);
        }
    }

    public Task StopAsync(CancellationToken ct)
    {
        // ending the keep-alive lets WSL idle out again (Redis stops a few seconds later), mirroring the API's lifetime
        try { if (_keepAlive is { HasExited: false }) _keepAlive.Kill(entireProcessTree: true); } catch { /* already gone */ }
        return Task.CompletedTask;
    }

    private static async Task<bool> PortOpenAsync()
    {
        try
        {
            using var c = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
            await c.ConnectAsync("localhost", 6379, cts.Token);
            return true;
        }
        catch { return false; }
    }

    private static async Task<bool> WaitForPortAsync(TimeSpan timeout, CancellationToken ct)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until && !ct.IsCancellationRequested)
        {
            if (await PortOpenAsync()) return true;
            await Task.Delay(500, ct);
        }
        return false;
    }

    private static async Task RunAsync(string file, string[] args, CancellationToken ct)
    {
        using var p = Process.Start(new ProcessStartInfo(file, args) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
        if (p is not null) await p.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(15), ct);
    }
}
