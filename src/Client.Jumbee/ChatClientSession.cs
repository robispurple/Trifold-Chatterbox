using Contracts;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.SignalR.Client;

namespace Client.Jumbee;

/// <summary>
/// Manages gRPC history replay and SignalR real-time messaging for the Jumbee.Console client.
/// </summary>
internal sealed class ChatClientSession : IAsyncDisposable
{
    #region Constructors

    public ChatClientSession(
        string username,
        string chatHubUrl,
        string grpcBaseUrl,
        Action<ChatMessage> onMessage,
        Action<string> onStatusChanged,
        Action<string, long> onHeartbeat)
    {
        this.username = username;
        this.chatHubUrl = chatHubUrl;
        this.grpcBaseUrl = grpcBaseUrl;
        this.onMessage = onMessage;
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
            EmitSystem("Connected to Chat Hub.");
        }
        catch (Exception ex)
        {
            onStatusChanged($"Connection failed: {ex.Message}");
            EmitSystem($"Failed to connect to hub: {ex.Message}");
        }
    }

    public async Task SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        try
        {
            await connection.InvokeAsync("SendMessage", username, "Jumbee", message, cancellationToken);
        }
        catch (Exception ex)
        {
            EmitSystem($"Failed to send message: {ex.Message}");
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
            onMessage(new ChatMessage(
                DateTime.Now.ToString("HH:mm:ss"),
                ChatMessageKind.Chat,
                message,
                sender,
                framework));
        });

        connection.On<string>("SystemNotice", message =>
        {
            onMessage(new ChatMessage(
                DateTime.Now.ToString("HH:mm:ss"),
                ChatMessageKind.Server,
                message));
        });

        connection.On<string, long>("ServerHeartbeat", (uptime, timestampUtc) =>
        {
            onHeartbeat(uptime, timestampUtc);
        });

        connection.Reconnecting += error =>
        {
            var reason = error?.Message ?? "unknown";
            onStatusChanged($"Reconnecting ({reason})...");
            EmitSystem($"Connection lost ({reason}). Reconnecting to SignalR Hub...");
            return Task.CompletedTask;
        };

        connection.Reconnected += async connectionId =>
        {
            onStatusChanged($"Connected ({connectionId ?? "active"})");
            EmitSystem($"Reconnected to SignalR Hub (id: {connectionId ?? "n/a"}) - rehydrating missed history...");
            await ReplayHistoryAsync(Interlocked.Read(ref lastSeenTimestampUtc));
        };

        connection.Closed += error =>
        {
            onStatusChanged("Disconnected");
            var suffix = error is null ? string.Empty : $" ({error.Message})";
            EmitSystem($"Disconnected from SignalR Hub{suffix}.");
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

                onMessage(new ChatMessage(
                    timestamp,
                    ChatMessageKind.Chat,
                    item.Message,
                    item.Sender,
                    item.Framework));
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
        }
        catch (Exception ex)
        {
            EmitSystem($"Failed to load gRPC history: {ex.Message}");
        }
    }

    private void EmitSystem(string message) =>
        onMessage(new ChatMessage(DateTime.Now.ToString("HH:mm:ss"), ChatMessageKind.System, message));

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
    private readonly Action<ChatMessage> onMessage;
    private readonly Action<string> onStatusChanged;
    private readonly Action<string, long> onHeartbeat;
    private readonly HubConnection connection;
    private long lastSeenTimestampUtc;

    #endregion
}
