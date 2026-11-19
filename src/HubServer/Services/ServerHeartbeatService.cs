namespace HubServer.Services;

using HubServer.Hubs;
using Microsoft.AspNetCore.SignalR;

public class ServerHeartbeatService(IHubContext<ChatHub> hubContext) : BackgroundService
{
    #region Methods

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var uptime = DateTimeOffset.UtcNow - startedAt;
            await hubContext.Clients.All.SendAsync(
                "SystemNotice",
                $"Server heartbeat — uptime {uptime:hh\\:mm\\:ss}",
                stoppingToken);
        }
    }

    #endregion

    #region Fields

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private readonly DateTimeOffset startedAt = DateTimeOffset.UtcNow;

    #endregion
}
