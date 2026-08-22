using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using ServiceDefaults;
using Spectre.Console;

var hostBuilder = Host.CreateApplicationBuilder(args);
hostBuilder.AddServiceDefaults();
var host = hostBuilder.Build();
await host.StartAsync();

var configuration = hostBuilder.Configuration;
var hubBaseUrl = configuration["services:hubserver:http:0"]
    ?? configuration["services:hubserver:https:0"]
    ?? configuration["HubServerUrl"]
    ?? "http://localhost:5000";

var chatHubUrl = $"{hubBaseUrl.TrimEnd('/')}/chat";

AnsiConsole.Write(new FigletText("Chatterbox").Color(Color.Green));
AnsiConsole.MarkupLine("[bold grey]Spectre.Console TUI Client[/] - [dim]Connected via SignalR[/]");
AnsiConsole.MarkupLine($"[grey]Target Hub:[/] [underline blue]{chatHubUrl}[/]\n");

var connection = new HubConnectionBuilder()
    .WithUrl(chatHubUrl)
    .WithAutomaticReconnect()
    .Build();

connection.On<string, string, string>("ReceiveMessage", (sender, framework, message) =>
{
    var timestamp = DateTime.Now.ToString("HH:mm:ss");
    var frameworkTag = framework switch
    {
        "Spectre" => "[green]Spectre[/]",
        "TerminalGui" => "[blue]TerminalGui[/]",
        "Jumbee" => "[yellow]Jumbee[/]",
        _ => $"[magenta]{Markup.Escape(framework)}[/]"
    };

    AnsiConsole.MarkupLine($"[grey]{timestamp}[/] [[{frameworkTag}]] [bold cyan]{Markup.Escape(sender)}[/]: {Markup.Escape(message)}");
});

connection.Reconnecting += error =>
{
    AnsiConsole.MarkupLine("[bold yellow]⚠ Reconnecting to SignalR Hub...[/]");
    return Task.CompletedTask;
};

connection.Reconnected += connectionId =>
{
    AnsiConsole.MarkupLine("[bold green]✓ Reconnected to SignalR Hub.[/]");
    return Task.CompletedTask;
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

var defaultUsername = $"User_{Random.Shared.Next(1000, 9999)}";
var username = AnsiConsole.Ask<string>("[bold]Enter your display name:[/] ", defaultUsername);

AnsiConsole.MarkupLine($"[dim]Welcome, [bold]{Markup.Escape(username)}[/]! Type a message and hit Enter. Type [red]/quit[/] to exit.[/]\n");

while (true)
{
    var input = Console.ReadLine();
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

await connection.StopAsync();
await host.StopAsync();