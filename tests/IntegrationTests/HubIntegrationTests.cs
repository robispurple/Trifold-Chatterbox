namespace IntegrationTests;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Contracts;
using Grpc.Core;
using IntegrationTests.Fixtures;
using Microsoft.AspNetCore.SignalR.Client;

[Collection(HubServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class HubIntegrationTests(HubServerFixture fixture)
{
    #region Methods

    [Fact]
    public async Task HealthEndpoints_Return200Healthy()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateHttpClient();

        var aliveResponse = await client.GetAsync("/alive", ct).WaitAsync(DefaultTimeout, ct);
        var healthResponse = await client.GetAsync("/health", ct).WaitAsync(DefaultTimeout, ct);

        Assert.Equal(HttpStatusCode.OK, aliveResponse.StatusCode);
        Assert.Equal("Healthy", await aliveResponse.Content.ReadAsStringAsync(ct));
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
        Assert.Equal("Healthy", await healthResponse.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task MultiClientSignalRFanOut_DeliversMessagesToAllThreeClients()
    {
        var ct = TestContext.Current.CancellationToken;
        var runTag = NewRunTag();
        string[] frameworks = ["Spectre", "TerminalGui", "Jumbee"];
        List<ClientObserver> observers = [];

        try
        {
            foreach (var framework in frameworks)
            {
                observers.Add(CreateObserver(fixture.CreateHubConnection(), runTag, expectedCount: frameworks.Length));
            }

            await Task.WhenAll(observers.Select(o => o.Connection.StartAsync(ct))).WaitAsync(DefaultTimeout, ct);

            var expected = frameworks
                .Select(f => new ReceivedChatMessage($"user-{f}-{runTag}", f, $"[{runTag}] hello from {f}"))
                .ToArray();

            for (var i = 0; i < expected.Length; i++)
            {
                var item = expected[i];
                await observers[i].Connection.InvokeAsync("SendMessage", item.Sender, item.Framework, item.Message, ct)
                    .WaitAsync(DefaultTimeout, ct);
            }

            await Task.WhenAll(observers.Select(o => o.Completion.Task)).WaitAsync(DefaultTimeout, ct);

            foreach (var observer in observers)
            {
                var actual = observer.Messages.ToArray();
                Assert.Equal(expected.Length, actual.Length);
                foreach (var item in expected)
                {
                    Assert.Contains(item, actual);
                }
            }
        }
        finally
        {
            await DisposeObserversAsync(observers);
        }
    }

    [Fact]
    public async Task ConcurrentMultiClientBurst_DeliversAllMessagesWithoutDrops()
    {
        var ct = TestContext.Current.CancellationToken;
        var runTag = NewRunTag();
        string[] frameworks = ["Spectre", "TerminalGui", "Jumbee"];
        const int messagesPerClient = 25;
        var totalExpected = frameworks.Length * messagesPerClient;
        List<ClientObserver> observers = [];

        try
        {
            foreach (var _ in frameworks)
            {
                observers.Add(CreateObserver(fixture.CreateHubConnection(), runTag, totalExpected));
            }

            await Task.WhenAll(observers.Select(o => o.Connection.StartAsync(ct))).WaitAsync(DefaultTimeout, ct);

            var expectedMessages = new ConcurrentBag<ReceivedChatMessage>();
            var sendTasks = observers.Select((observer, clientIndex) => Task.Run(async () =>
            {
                var framework = frameworks[clientIndex];
                var sender = $"burst-{framework}-{runTag}";
                var perClientTasks = Enumerable.Range(0, messagesPerClient).Select(index =>
                {
                    var text = $"[{runTag}] {framework} #{index:D2}";
                    var chatMessage = new ReceivedChatMessage(sender, framework, text);
                    expectedMessages.Add(chatMessage);
                    return observer.Connection.InvokeAsync("SendMessage", sender, framework, text, ct);
                });

                await Task.WhenAll(perClientTasks);
            }, ct));

            await Task.WhenAll(sendTasks).WaitAsync(LoadTimeout, ct);
            await Task.WhenAll(observers.Select(o => o.Completion.Task)).WaitAsync(LoadTimeout, ct);

            var expectedSet = expectedMessages.ToHashSet();
            Assert.Equal(totalExpected, expectedSet.Count);

            foreach (var observer in observers)
            {
                var receivedSet = observer.Messages.ToHashSet();
                Assert.Equal(totalExpected, observer.Messages.Count);
                Assert.True(expectedSet.SetEquals(receivedSet), "Observer missed or corrupted burst messages.");
            }
        }
        finally
        {
            await DisposeObserversAsync(observers);
        }
    }

    [Fact]
    public async Task GrpcColdStart_EmptyHistory_ReturnsZeroItemsAndCompletesCleanly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var server = await fixture.CreateIsolatedContainerAsync(ct).WaitAsync(ContainerTimeout, ct);

        var items = await ReadHistoryAsync(server.CreateHistoryClient(), limit: 20, DefaultTimeout, ct);

        Assert.Empty(items);
    }

    [Fact]
    public async Task GrpcHistoryReplay_RespectsOrderingTimestampAndLimit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var server = await fixture.CreateIsolatedContainerAsync(ct).WaitAsync(ContainerTimeout, ct);
        await using var connection = server.CreateHubConnection();
        await connection.StartAsync(ct).WaitAsync(DefaultTimeout, ct);

        const int totalSeeded = 10;
        var beforeSendUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 5;

        for (var i = 1; i <= totalSeeded; i++)
        {
            await connection.InvokeAsync("SendMessage", $"sender-{i:D2}", "Spectre", $"ordered-{i:D2}", ct)
                .WaitAsync(DefaultTimeout, ct);
        }

        var afterSendUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 5;
        var historyClient = server.CreateHistoryClient();

        var allItems = await ReadHistoryAsync(historyClient, limit: 0, DefaultTimeout, ct);
        Assert.Equal(totalSeeded, allItems.Count);

        for (var i = 0; i < totalSeeded; i++)
        {
            var expectedIndex = i + 1;
            Assert.Equal($"sender-{expectedIndex:D2}", allItems[i].Sender);
            Assert.Equal("Spectre", allItems[i].Framework);
            Assert.Equal($"ordered-{expectedIndex:D2}", allItems[i].Message);
            Assert.InRange(allItems[i].TimestampUtc, beforeSendUnix, afterSendUnix);
        }

        const int limit = 4;
        var limitedItems = await ReadHistoryAsync(historyClient, limit, DefaultTimeout, ct);
        Assert.Equal(limit, limitedItems.Count);
        Assert.Equal(
            Enumerable.Range(totalSeeded - limit + 1, limit).Select(i => $"ordered-{i:D2}"),
            limitedItems.Select(m => m.Message));
    }

    [Fact]
    public async Task GrpcHistoryBuffer_EvictsOldestMessagesBeyondMaxHistoryCount()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var server = await fixture.CreateIsolatedContainerAsync(ct).WaitAsync(ContainerTimeout, ct);
        await using var connection = server.CreateHubConnection();
        await connection.StartAsync(ct).WaitAsync(DefaultTimeout, ct);

        const int totalSent = 120;
        const int maxHistoryCount = 100;

        for (var i = 1; i <= totalSent; i++)
        {
            await connection.InvokeAsync("SendMessage", "evict-tester", "Jumbee", $"msg-{i:D3}", ct)
                .WaitAsync(DefaultTimeout, ct);
        }

        var history = await ReadHistoryAsync(server.CreateHistoryClient(), limit: 0, DefaultTimeout, ct);

        Assert.Equal(maxHistoryCount, history.Count);
        Assert.Equal(
            Enumerable.Range(21, maxHistoryCount).Select(i => $"msg-{i:D3}"),
            history.Select(m => m.Message));
    }

    [Fact]
    public async Task HybridReplayThenLiveBoot_AndProtocolCoexistenceUnderLoad_CompleteCleanly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var server = await fixture.CreateIsolatedContainerAsync(ct).WaitAsync(ContainerTimeout, ct);

        await using var seeder = server.CreateHubConnection();
        await seeder.StartAsync(ct).WaitAsync(DefaultTimeout, ct);

        const int seededCount = 15;
        for (var i = 1; i <= seededCount; i++)
        {
            await seeder.InvokeAsync("SendMessage", "seeder", "TerminalGui", $"seed-{i:D2}", ct)
                .WaitAsync(DefaultTimeout, ct);
        }

        // Late-joining client replays history via gRPC first, then subscribes to live SignalR feed.
        var replayed = await ReadHistoryAsync(server.CreateHistoryClient(), limit: 50, DefaultTimeout, ct);
        Assert.Equal(
            Enumerable.Range(1, seededCount).Select(i => $"seed-{i:D2}"),
            replayed.Select(m => m.Message));

        const int burstWriters = 4;
        const int messagesPerWriter = 20;
        const int totalBurstMessages = burstWriters * messagesPerWriter;
        const int grpcReaders = 6;
        const int callsPerReader = 4;

        var liveTag = NewRunTag();
        var lateJoinObserver = CreateObserver(server.CreateHubConnection(), liveTag, totalBurstMessages);
        List<HubConnection> writers = [];

        try
        {
            await lateJoinObserver.Connection.StartAsync(ct).WaitAsync(DefaultTimeout, ct);

            for (var w = 0; w < burstWriters; w++)
            {
                writers.Add(server.CreateHubConnection());
            }

            await Task.WhenAll(writers.Select(w => w.StartAsync(ct))).WaitAsync(DefaultTimeout, ct);

            var expectedLive = new ConcurrentBag<ReceivedChatMessage>();

            var writerTasks = writers.Select((writer, writerIndex) => Task.Run(async () =>
            {
                var sender = $"writer-{writerIndex}-{liveTag}";
                for (var m = 0; m < messagesPerWriter; m++)
                {
                    var text = $"[{liveTag}] w{writerIndex}-m{m:D2}";
                    expectedLive.Add(new ReceivedChatMessage(sender, "Spectre", text));
                    await writer.InvokeAsync("SendMessage", sender, "Spectre", text, ct);
                }
            }, ct));

            var grpcClient = server.CreateHistoryClient();
            var readerTasks = Enumerable.Range(0, grpcReaders).Select(_ => Task.Run(async () =>
            {
                for (var c = 0; c < callsPerReader; c++)
                {
                    var snapshot = await ReadHistoryAsync(grpcClient, limit: 50, LoadTimeout, ct);
                    Assert.InRange(snapshot.Count, seededCount, 50);
                    Assert.All(snapshot, item => Assert.True(item.TimestampUtc > 0));
                }
            }, ct));

            await Task.WhenAll([.. writerTasks, .. readerTasks]).WaitAsync(LoadTimeout, ct);
            await lateJoinObserver.Completion.Task.WaitAsync(LoadTimeout, ct);

            var actualLive = lateJoinObserver.Messages.ToHashSet();
            Assert.Equal(totalBurstMessages, lateJoinObserver.Messages.Count);
            Assert.True(expectedLive.ToHashSet().SetEquals(actualLive));
        }
        finally
        {
            await lateJoinObserver.Connection.DisposeAsync();
            foreach (var writer in writers)
            {
                await writer.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task PostAnnounce_ReturnsOkWithSentPayloadAndBroadcastsSystemNotice()
    {
        var ct = TestContext.Current.CancellationToken;
        var notice = $"Maintenance window {NewRunTag()}";
        using var httpClient = fixture.CreateHttpClient();
        await using var clientA = fixture.CreateHubConnection();
        await using var clientB = fixture.CreateHubConnection();

        var receivedA = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedB = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        clientA.On<string>("SystemNotice", message =>
        {
            if (message == notice)
            {
                receivedA.TrySetResult(message);
            }
        });

        clientB.On<string>("SystemNotice", message =>
        {
            if (message == notice)
            {
                receivedB.TrySetResult(message);
            }
        });

        await Task.WhenAll(clientA.StartAsync(ct), clientB.StartAsync(ct)).WaitAsync(DefaultTimeout, ct);

        using var response = await httpClient.PostAsync(
            $"/announce?message={Uri.EscapeDataString(notice)}",
            content: null,
            ct).WaitAsync(DefaultTimeout, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AnnounceResponse>(ct).WaitAsync(DefaultTimeout, ct);
        Assert.NotNull(payload);
        Assert.Equal(notice, payload.Sent);

        var notices = await Task.WhenAll(receivedA.Task, receivedB.Task).WaitAsync(DefaultTimeout, ct);
        Assert.Equal([notice, notice], notices);
    }

    private static ClientObserver CreateObserver(HubConnection connection, string runTag, int expectedCount)
    {
        var messages = new ConcurrentQueue<ReceivedChatMessage>();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        connection.On<string, string, string>("ReceiveMessage", (sender, framework, message) =>
        {
            if (!message.Contains(runTag, StringComparison.Ordinal))
            {
                return;
            }

            messages.Enqueue(new ReceivedChatMessage(sender, framework, message));
            if (messages.Count >= expectedCount)
            {
                completion.TrySetResult();
            }
        });

        return new ClientObserver(connection, messages, completion);
    }

    private static async Task DisposeObserversAsync(IEnumerable<ClientObserver> observers)
    {
        foreach (var observer in observers)
        {
            await observer.Connection.DisposeAsync();
        }
    }

    private static async Task<List<HistoryMessageResponse>> ReadHistoryAsync(
        HistoryService.HistoryServiceClient client,
        int limit,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        using var call = client.GetRecentMessages(new HistoryRequest { Limit = limit }, cancellationToken: timeoutCts.Token);
        List<HistoryMessageResponse> items = [];

        await foreach (var item in call.ResponseStream.ReadAllAsync(timeoutCts.Token))
        {
            items.Add(item);
        }

        return items;
    }

    private static string NewRunTag() => Guid.NewGuid().ToString("N")[..8];

    #endregion

    #region Fields

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ContainerTimeout = TimeSpan.FromMinutes(2);

    #endregion

    #region Child Types

    private sealed record ReceivedChatMessage(string Sender, string Framework, string Message);

    private sealed record AnnounceResponse(string Sent);

    private sealed record ClientObserver(
        HubConnection Connection,
        ConcurrentQueue<ReceivedChatMessage> Messages,
        TaskCompletionSource Completion);

    #endregion
}
