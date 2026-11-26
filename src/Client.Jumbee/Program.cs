using Client.Jumbee;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceDefaults;

var configuredUsername = Environment.GetEnvironmentVariable("CHAT_USERNAME");
var defaultUsername = !string.IsNullOrWhiteSpace(configuredUsername)
    ? configuredUsername
    : $"User_{Random.Shared.Next(1000, 9999)}";

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")))
{
    Environment.SetEnvironmentVariable("OTEL_SERVICE_NAME", $"Client.Jumbee.{defaultUsername}");
}

var isRedirected = Console.IsInputRedirected || Console.IsOutputRedirected;

var hostBuilder = Host.CreateApplicationBuilder(args);
if (!isRedirected)
{
    hostBuilder.Logging.ClearProviders();
}

hostBuilder.AddServiceDefaults();
var host = hostBuilder.Build();
await host.StartAsync();

var configuration = hostBuilder.Configuration;
var hubBaseUrl = configuration["services:hubserver:http:0"]
    ?? configuration["services:hubserver:https:0"]
    ?? configuration["services:hubserver:http"]
    ?? configuration["services:hubserver:https"]
    ?? configuration["HubServerUrl"]
    ?? "http://localhost:5000";

var chatHubUrl = $"{hubBaseUrl.TrimEnd('/')}/chat";
var grpcBaseUrl = configuration["services:hubserver:grpc:0"]
    ?? configuration["services:hubserver:grpc"]
    ?? configuration["GrpcServerUrl"]
    ?? "http://localhost:5001";

ChatClientSession CreateSession(
    Action<ChatMessage> onMessage,
    Action<string> onStatusChanged,
    Action<string, long> onHeartbeat) =>
    new(defaultUsername, chatHubUrl, grpcBaseUrl, onMessage, onStatusChanged, onHeartbeat);

if (!isRedirected)
{
    var runner = new JumbeeChatRunner(defaultUsername, chatHubUrl, grpcBaseUrl, CreateSession);
    await runner.RunAsync();
}
else
{
    Console.WriteLine("Chatterbox - Jumbee.Console Client (Non-interactive / background mode)");
    Console.WriteLine($"Target Hub: {chatHubUrl}");
    Console.WriteLine($"Target gRPC: {grpcBaseUrl}");
    Console.WriteLine($"Registered as: {defaultUsername}");

    await using var session = CreateSession(
        message => Console.WriteLine(message.ToPlainText()),
        status => Console.WriteLine($"[STATUS] {status}"),
        (uptime, _) => Console.WriteLine($"[HEARTBEAT] uptime {uptime}"));

    await session.StartAsync();

    var exitTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    AppDomain.CurrentDomain.ProcessExit += (_, _) => exitTcs.TrySetResult();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        exitTcs.TrySetResult();
    };

    await exitTcs.Task;
    await session.StopAsync();
}

await host.StopAsync();
