using Contracts;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.SignalR.Client;

namespace Client.TerminalGui;

/// <summary>
/// Manages gRPC history replay and SignalR real-time messaging for the Terminal.Gui client.
/// </summary>
internal sealed class ChatClientSession : IAsyncDisposable
{
    #region Constructors

    public ChatClientSession(
        string username,
        string chatHubUrl,
        string grpcBaseUrl,
        Action<string> onMessageLine,
        Action<string> onStatusChanged,
        Action<string, long> onHeartbeat)
    {
        this.username = username;
        this.chatHubUrl = chatHubUrl;
        this.grpcBaseUrl = grpcBaseUrl;
        this.onMessageLine = onMessageLine;
        this.onStatusChanged = onStatusChanged;
        this.onHeartbeat = onHeartbeat;

        connection = new HubConnectionBuilder()
            .WithUrl(chatHubUrl)
            .WithAutomaticReconnect(new InfiniteRetryPolicy())
            .Build();

        RegisterHubHandlers();
    }

    #endregion

    #region Properties

    public string Username => username;

    public string ChatHubUrl => chatHubUrl;

    public string GrpcBaseUrl => grpcBaseUrl;

    #endregion

    #region Methods

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        onStatusChanged("Replaying gRPC history...");
        await ReplayHistoryAsync(sinceTimestampUtc: 0, cancellationToken);

        try
        {
            onStatusChanged("Connecting to SignalR Hub...");
            await connection.StartAsync(cancellationToken);
            onStatusChanged($"Connected ({connection.ConnectionId ?? "active"})");
            onMessageLine($"[{DateTime.Now:HH:mm:ss}] [SYSTEM] Connected to Chat Hub.");
        }
        catch (Exception ex)
        {
            onStatusChanged($"Connection failed: {ex.Message}");
            onMessageLine($"[{DateTime.Now:HH:mm:ss}] [SYSTEM] Failed to connect to hub: {ex.Message}");
        }
    }

    public async Task SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        try
        {
            await connection.InvokeAsync("SendMessage", username, "TerminalGui", message, cancellationToken);
        }
        catch (Exception ex)
        {
            onMessageLine($"[{DateTime.Now:HH:mm:ss}] [SYSTEM] Failed to send message: {ex.Message}");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (connection.State != HubConnectionState.Disconnected)
        {
            await connection.StopAsync(cancellationToken);
        }
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();

    private void RegisterHubHandlers()
    {
        connection.On<string, string, string>("ReceiveMessage", (sender, framework, message) =>
        {
            TrackTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            onMessageLine($"[{DateTime.Now:HH:mm:ss}] [{framework}] {sender}: {message}");
        });

        connection.On<string>("SystemNotice", message =>
        {
            onMessageLine($"[{DateTime.Now:HH:mm:ss}] [SERVER] {message}");
        });

        connection.On<string, long>("ServerHeartbeat", (uptime, timestampUtc) =>
        {
            onHeartbeat(uptime, timestampUtc);
        });

        connection.Reconnecting += error =>
        {
            var reason = error?.Message ?? "unknown";
            onStatusChanged($"Reconnecting ({reason})...");
            onMessageLine($"[{DateTime.Now:HH:mm:ss}] [SYSTEM] Connection lost ({reason}). Reconnecting to SignalR Hub...");
            return Task.CompletedTask;
        };

        connection.Reconnected += async connectionId =>
        {
            onStatusChanged($"Connected ({connectionId ?? "active"})");
            onMessageLine($"[{DateTime.Now:HH:mm:ss}] [SYSTEM] Reconnected to SignalR Hub (id: {connectionId ?? "n/a"}) - rehydrating missed history...");
            await ReplayHistoryAsync(Interlocked.Read(ref lastSeenTimestampUtc));
        };

        connection.Closed += error =>
        {
            onStatusChanged("Disconnected");
            var suffix = error is null ? string.Empty : $" ({error.Message})";
            onMessageLine($"[{DateTime.Now:HH:mm:ss}] [SYSTEM] Disconnected from SignalR Hub{suffix}.");
            return Task.CompletedTask;
        };
    }

    private async Task ReplayHistoryAsync(long sinceTimestampUtc, CancellationToken cancellationToken = default)
    {
        try
        {
            using var channel = GrpcChannel.ForAddress(grpcBaseUrl);
            var historyClient = new HistoryService.HistoryServiceClient(channel);
            using var historyCall = historyClient.GetRecentMessages(
                new HistoryRequest { Limit = 50 },
                cancellationToken: cancellationToken);

            await foreach (var item in historyCall.ResponseStream.ReadAllAsync(cancellationToken))
            {
                if (item.TimestampUtc <= sinceTimestampUtc)
                {
                    continue;
                }

                TrackTimestamp(item.TimestampUtc);
                var timestamp = DateTimeOffset
                    .FromUnixTimeSeconds(item.TimestampUtc)
                    .ToLocalTime()
                    .ToString("HH:mm:ss");
                onMessageLine($"[{timestamp}] [{item.Framework}] {item.Sender}: {item.Message}");
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
        }
        catch (Exception ex)
        {
            onMessageLine($"[{DateTime.Now:HH:mm:ss}] [SYSTEM] Failed to load gRPC history: {ex.Message}");
        }
    }

    private void TrackTimestamp(long timestampUtc)
    {
        long current;
        while (timestampUtc > (current = Interlocked.Read(ref lastSeenTimestampUtc)))
        {
            if (Interlocked.CompareExchange(ref lastSeenTimestampUtc, timestampUtc, current) == current)
            {
                break;
            }
        }
    }

    #endregion

    #region Fields

    private readonly string username;
    private readonly string chatHubUrl;
    private readonly string grpcBaseUrl;
    private readonly Action<string> onMessageLine;
    private readonly Action<string> onStatusChanged;
    private readonly Action<string, long> onHeartbeat;
    private readonly HubConnection connection;
    private long lastSeenTimestampUtc;

    #endregion
}
