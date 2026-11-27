namespace IntegrationTests.Fixtures;

using System.Collections.Concurrent;
using Contracts;
using DotNet.Testcontainers.Containers;
using Grpc.Net.Client;
using Microsoft.AspNetCore.SignalR.Client;

public sealed class HubServerInstance : IAsyncDisposable
{
    #region Constructors

    public HubServerInstance(IContainer container)
    {
        this.container = container;
        var host = container.Hostname;
        HttpBaseUri = new Uri($"http://{host}:{container.GetMappedPublicPort(HttpPort)}");
        ChatHubUri = new Uri(HttpBaseUri, "/chat");
        GrpcBaseUri = new Uri($"http://{host}:{container.GetMappedPublicPort(GrpcPort)}");
    }

    #endregion

    #region Properties

    public Uri HttpBaseUri { get; }

    public Uri ChatHubUri { get; }

    public Uri GrpcBaseUri { get; }

    #endregion

    #region Methods

    public HttpClient CreateHttpClient() => new() { BaseAddress = HttpBaseUri };

    public HubConnection CreateHubConnection() =>
        new HubConnectionBuilder()
            .WithUrl(ChatHubUri)
            .Build();

    public GrpcChannel CreateGrpcChannel() => GrpcChannel.ForAddress(GrpcBaseUri);

    public HistoryService.HistoryServiceClient CreateHistoryClient()
    {
        var channel = CreateGrpcChannel();
        ownedChannels.Add(channel);
        return new HistoryService.HistoryServiceClient(channel);
    }

    public async ValueTask DisposeAsync()
    {
        while (ownedChannels.TryTake(out var channel))
        {
            channel.Dispose();
        }

        await container.DisposeAsync().ConfigureAwait(false);
    }

    #endregion

    #region Fields

    internal const ushort HttpPort = 8080;
    internal const ushort GrpcPort = 5001;

    private readonly IContainer container;
    private readonly ConcurrentBag<GrpcChannel> ownedChannels = [];

    #endregion
}
