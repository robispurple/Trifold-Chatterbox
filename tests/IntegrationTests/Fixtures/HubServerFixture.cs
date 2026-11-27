namespace IntegrationTests.Fixtures;

using System.Globalization;
using System.Net;
using Contracts;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;
using Grpc.Net.Client;
using Microsoft.AspNetCore.SignalR.Client;

public sealed class HubServerFixture : IAsyncLifetime
{
    #region Properties

    public Uri HttpBaseUri => Shared.HttpBaseUri;

    public Uri ChatHubUri => Shared.ChatHubUri;

    public Uri GrpcBaseUri => Shared.GrpcBaseUri;

    private HubServerInstance Shared =>
        sharedInstance ?? throw new InvalidOperationException("HubServerFixture has not been initialized.");

    #endregion

    #region Methods

    public async ValueTask InitializeAsync()
    {
        using var cts = new CancellationTokenSource(StartupTimeout);

        var envImage = Environment.GetEnvironmentVariable(ImageEnvVar);
        if (!string.IsNullOrWhiteSpace(envImage))
        {
            imageName = envImage.Trim();
        }
        else
        {
            var repoRoot = FindRepoRoot();
            builtImage = new ImageFromDockerfileBuilder()
                .WithDockerfileDirectory(repoRoot)
                .WithDockerfile("src/HubServer/Dockerfile")
                .WithName($"trifold-hubserver-test:{Guid.NewGuid():N}")
                .WithCleanUp(true)
                .Build();

            await builtImage.CreateAsync(cts.Token).ConfigureAwait(false);
            imageName = builtImage.FullName;
        }

        sharedInstance = await StartContainerAsync(cts.Token).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (sharedInstance is not null)
        {
            await sharedInstance.DisposeAsync().ConfigureAwait(false);
            sharedInstance = null;
        }

        if (builtImage is not null)
        {
            await builtImage.DisposeAsync().ConfigureAwait(false);
            builtImage = null;
        }
    }

    public HttpClient CreateHttpClient() => Shared.CreateHttpClient();

    public HubConnection CreateHubConnection() => Shared.CreateHubConnection();

    public GrpcChannel CreateGrpcChannel() => Shared.CreateGrpcChannel();

    public HistoryService.HistoryServiceClient CreateHistoryClient() => Shared.CreateHistoryClient();

    public Task<HubServerInstance> CreateIsolatedContainerAsync(CancellationToken cancellationToken = default) =>
        StartContainerAsync(cancellationToken);

    private async Task<HubServerInstance> StartContainerAsync(CancellationToken cancellationToken)
    {
        var resolvedImage = imageName
            ?? throw new InvalidOperationException("HubServer image has not been initialized.");

        var container = new ContainerBuilder(resolvedImage)
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
            .WithEnvironment("ASPNETCORE_HTTP_PORTS", HubServerInstance.HttpPort.ToString(CultureInfo.InvariantCulture))
            .WithEnvironment("GrpcPort", HubServerInstance.GrpcPort.ToString(CultureInfo.InvariantCulture))
            .WithPortBinding(HubServerInstance.HttpPort, true)
            .WithPortBinding(HubServerInstance.GrpcPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r
                    .ForPort(HubServerInstance.HttpPort)
                    .ForPath("/alive")
                    .ForStatusCode(HttpStatusCode.OK))
                .UntilExternalTcpPortIsAvailable(HubServerInstance.GrpcPort))
            .Build();

        await container.StartAsync(cancellationToken).ConfigureAwait(false);
        return new HubServerInstance(container);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate '{SolutionFileName}' walking up from '{AppContext.BaseDirectory}'.");
    }

    #endregion

    #region Fields

    private const string ImageEnvVar = "HUBSERVER_TEST_IMAGE";
    private const string SolutionFileName = "Trifold-Chatterbox.slnx";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private IFutureDockerImage? builtImage;
    private string? imageName;
    private HubServerInstance? sharedInstance;

    #endregion
}
