using Contracts;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using ServiceDefaults;
using Spectre.Console;

var configuredUsername = Environment.GetEnvironmentVariable("CHAT_USERNAME");
var defaultUsername = !string.IsNullOrWhiteSpace(configuredUsername)
    ? configuredUsername
    : $"User_{Random.Shared.Next(1000, 9999)}";
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")))
{
    Environment.SetEnvironmentVariable("OTEL_SERVICE_NAME", $"Client.Spectre.{defaultUsername}");
}

var hostBuilder = Host.CreateApplicationBuilder(args);
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

AnsiConsole.Write(new FigletText("Chatterbox").Color(Color.Green));
AnsiConsole.MarkupLine("[bold grey]Spectre.Console TUI Client[/] - [dim]Connected via SignalR + gRPC[/]");
AnsiConsole.MarkupLine($"[grey]Target Hub:[/] [underline blue]{chatHubUrl}[/]");
AnsiConsole.MarkupLine($"[grey]Target gRPC:[/] [underline blue]{grpcBaseUrl}[/]\n");

var username = (!Console.IsInputRedirected && string.IsNullOrWhiteSpace(configuredUsername))
    ? AnsiConsole.Ask<string>("[bold]Enter your display name:[/] ", defaultUsername)
    : defaultUsername;

long lastSeenTimestampUtc = 0;

void TrackTimestamp(long timestampUtc)
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

static void RenderMessage(string timestamp, string sender, string framework, string message)
{
    var frameworkTag = framework switch
    {
        "Spectre" => "[green]Spectre[/]",
        "TerminalGui" => "[blue]TerminalGui[/]",
        "Jumbee" => "[yellow]Jumbee[/]",
        _ => $"[magenta]{Markup.Escape(framework)}[/]"
    };

    AnsiConsole.MarkupLine($"[grey]{timestamp}[/] [[{frameworkTag}]] [bold cyan]{Markup.Escape(sender)}[/]: {Markup.Escape(message)}");
}

async Task ReplayHistoryAsync(long sinceTimestampUtc)
{
    try
    {
        using var channel = GrpcChannel.ForAddress(grpcBaseUrl);
        var historyClient = new HistoryService.HistoryServiceClient(channel);
        using var historyCall = historyClient.GetRecentMessages(new HistoryRequest { Limit = 50 });

        await foreach (var item in historyCall.ResponseStream.ReadAllAsync())
        {
            if (item.TimestampUtc <= sinceTimestampUtc)
            {
                continue;
            }

            TrackTimestamp(item.TimestampUtc);
            var timestamp = DateTimeOffset.FromUnixTimeSeconds(item.TimestampUtc).ToLocalTime().ToString("HH:mm:ss");
            RenderMessage(timestamp, item.Sender, item.Framework, item.Message);
        }
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[bold yellow]⚠ Failed to load gRPC history:[/] {Markup.Escape(ex.Message)}");
    }
}

await ReplayHistoryAsync(sinceTimestampUtc: 0);

var connection = new HubConnectionBuilder()
    .WithUrl(chatHubUrl)
    .WithAutomaticReconnect(new InfiniteRetryPolicy())
    .Build();

connection.On<string, string, string>("ReceiveMessage", (sender, framework, message) =>
{
    TrackTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    RenderMessage(DateTime.Now.ToString("HH:mm:ss"), sender, framework, message);
});

connection.On<string>("SystemNotice", message =>
{
    AnsiConsole.MarkupLine($"[grey]{DateTime.Now:HH:mm:ss}[/] [bold magenta][[SERVER]][/] {Markup.Escape(message)}");
});

connection.Reconnecting += error =>
{
    AnsiConsole.MarkupLine($"[bold yellow]⚠ Connection lost ({Markup.Escape(error?.Message ?? "unknown")}). Reconnecting to SignalR Hub...[/]");
    return Task.CompletedTask;
};

connection.Reconnected += async connectionId =>
{
    AnsiConsole.MarkupLine($"[bold green]✓ Reconnected to SignalR Hub[/] [dim](id: {Markup.Escape(connectionId ?? "n/a")})[/] — [dim]rehydrating missed history...[/]");
    await ReplayHistoryAsync(Interlocked.Read(ref lastSeenTimestampUtc));
};

connection.Closed += error =>
{
    AnsiConsole.MarkupLine("[bold red]✕ Disconnected from SignalR Hub.[/]");
    return Task.CompletedTask;
};

try
{
    await connection.StartAsync();
    AnsiConsole.MarkupLine("[bold green]✓ Connected to Chat Hub![/]");
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[bold red]✕ Failed to connect to hub:[/] {Markup.Escape(ex.Message)}");
}

if (!Console.IsInputRedirected)
{
    AnsiConsole.MarkupLine($"[dim]Welcome, [bold]{Markup.Escape(username)}[/]! Type a message and hit Enter. Type [red]/quit[/] to exit.[/]\n");

    while (true)
    {
        var input = Console.ReadLine();
        if (input == null)
        {
            break;
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            continue;
        }

        if (string.Equals(input.Trim(), "/quit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input.Trim(), "/exit", StringComparison.OrdinalIgnoreCase))
        {
            break;
        }

        try
        {
            await connection.InvokeAsync("SendMessage", username, "Spectre", input);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]✕ Failed to send message:[/] {Markup.Escape(ex.Message)}");
        }
    }
}
else
{
    AnsiConsole.MarkupLine($"[dim]Non-interactive / background mode detected. Registered as [bold cyan]{Markup.Escape(username)}[/]. Listening for messages...[/]\n");

    var tcs = new TaskCompletionSource();
    AppDomain.CurrentDomain.ProcessExit += (_, _) => tcs.TrySetResult();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        tcs.TrySetResult();
    };

    await tcs.Task;
}

await connection.StopAsync();
await host.StopAsync();

sealed class InfiniteRetryPolicy : IRetryPolicy
{
    #region Methods

    public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
        retryContext.PreviousRetryCount < Delays.Length
            ? Delays[retryContext.PreviousRetryCount]
            : Delays[^1];

    #endregion

    #region Fields

    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10)
    ];

    #endregion
}