using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using HubServer.Hubs;
using HubServer.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var History = Get<HistoryServiceImpl>();
var ChatHubContext = Get<IHubContext<ChatHub>>();
var Config = Get<IConfiguration>();
var HostEnv = Get<IHostEnvironment>();

string DumpHistory(int take = 20)
{
    var snapshot = History.GetSnapshot();
    var slice = take > 0 && snapshot.Count > take
        ? snapshot.Skip(snapshot.Count - take).ToList()
        : snapshot;

    var lines = new List<string> { $"In-memory history count: {History.Count} (showing {slice.Count})" };
    foreach (var item in slice)
    {
        var localTime = DateTimeOffset.FromUnixTimeSeconds(item.TimestampUtc).ToLocalTime();
        lines.Add($"  [{localTime:HH:mm:ss}] [{item.Framework}] {item.Sender}: {item.Message}");
    }

    return string.Join(Environment.NewLine, lines);
}

string InjectHistory(string message, string sender = "LiveRepl", string framework = "CSharpRepl")
{
    History.AddMessage(sender, framework, message);
    return $"Injected history entry directly into HistoryServiceImpl (count is now {History.Count}).";
}

async Task<string> BroadcastNoticeAsync(string message)
{
    await ChatHubContext.Clients.All.SendAsync("SystemNotice", message);
    return $"Sent SystemNotice via DI IHubContext<ChatHub>: '{message}'.";
}

async Task<string> BroadcastChatAsync(string message, string sender = "LiveRepl", string framework = "CSharpRepl")
{
    History.AddMessage(sender, framework, message);
    await ChatHubContext.Clients.All.SendAsync("ReceiveMessage", sender, framework, message);
    return $"Recorded to History and broadcast ReceiveMessage via DI: [{framework}] {sender}: {message}";
}

string InspectEnvironment()
{
    var httpPort = Config.GetValue("ASPNETCORE_HTTP_PORTS", 5000);
    var grpcPort = Config.GetValue("GrpcPort", 5001);
    return $"Attached to HubServer PID {Process.GetCurrentProcess().Id} ({HostEnv.EnvironmentName}) | HTTP :{httpPort} | gRPC :{grpcPort} | History count: {History.Count}";
}

string LiveHelp() => string.Join(Environment.NewLine,
[
    string.Empty,
    "=== CSharpRepl Live In-Process Session ===",
    "Bound globals:",
    "  services, Get<T>(), History, ChatHubContext, Config, HostEnv",
    string.Empty,
    "Commands (return values display directly in CSharpRepl):",
    "  InspectEnvironment()                  - PID, ports, and live history count",
    "  DumpHistory(20)                       - Read HistoryServiceImpl memory directly (no gRPC)",
    "  InjectHistory(\"msg\")                  - Mutate HistoryServiceImpl without SignalR broadcast",
    "  await BroadcastNoticeAsync(\"msg\")     - Push SystemNotice via DI IHubContext<ChatHub>",
    "  await BroadcastChatAsync(\"msg\")       - Record to History and broadcast ReceiveMessage",
    "  LiveHelp()                            - Show this summary",
    string.Empty,
    "Tips:",
    "  - Expression return values display in CSharpRepl; Console.WriteLine prints in HubServer's stdout.",
    "  - Runtime method detours (#wrap / #replace) require .NET 10 or earlier until MonoMod supports CoreCLR 11.",
    string.Empty
]);

$"{InspectEnvironment()}{Environment.NewLine}{LiveHelp()}"
