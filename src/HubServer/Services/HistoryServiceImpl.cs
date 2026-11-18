namespace HubServer.Services;

using System.Collections.Concurrent;
using Contracts;
using Grpc.Core;

public class HistoryServiceImpl : HistoryService.HistoryServiceBase
{
    #region Methods

    public void AddMessage(string sender, string framework, string message)
    {
        historyQueue.Enqueue(new HistoryMessageResponse
        {
            Sender = sender,
            Framework = framework,
            Message = message,
            TimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        while (historyQueue.Count > MaxHistoryCount && historyQueue.TryDequeue(out _))
        {
        }
    }

    public override async Task GetRecentMessages(
        HistoryRequest request,
        IServerStreamWriter<HistoryMessageResponse> responseStream,
        ServerCallContext context)
    {
        var messages = request.Limit > 0
            ? historyQueue.TakeLast(request.Limit)
            : historyQueue;

        foreach (var message in messages)
        {
            await responseStream.WriteAsync(message, context.CancellationToken);
        }
    }

    #endregion

    #region Fields

    private const int MaxHistoryCount = 100;
    private readonly ConcurrentQueue<HistoryMessageResponse> historyQueue = new();

    #endregion
}
