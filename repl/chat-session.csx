#r "nuget: Microsoft.AspNetCore.SignalR.Client, 10.0.0"
#r "nuget: Grpc.Net.Client, 2.71.0"
#r "nuget: Google.Protobuf, 3.30.1"
#r "nuget: Grpc.Core.Api, 2.71.0"

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.SignalR.Client;

public record HistoryRequest(int Limit);
public record HistoryMessageResponse(string Sender, string Framework, string Message, long TimestampUtc);
public record ChatEvent(DateTimeOffset ReceivedAt, string Sender, string Framework, string Message);
public record HeartbeatEvent(DateTimeOffset ReceivedAt, string Uptime, long TimestampUtc);

var HubBaseUrl = Environment.GetEnvironmentVariable("HubServerUrl") ?? "http://localhost:5000";
var ChatHubUrl = $"{HubBaseUrl.TrimEnd('/')}/chat";
var GrpcBaseUrl = Environment.GetEnvironmentVariable("GrpcServerUrl") ?? "http://localhost:5001";
var ReplUser = Environment.GetEnvironmentVariable("CHAT_USERNAME") ?? "ReplProbe";
var ReplFramework = Environment.GetEnvironmentVariable("REPL_FRAMEWORK") ?? "REPL";
var AutoAssert = string.Equals(Environment.GetEnvironmentVariable("REPL_ASSERT"), "true", StringComparison.OrdinalIgnoreCase);

var ReceivedMessages = new ConcurrentQueue<ChatEvent>();
var SystemNotices = new ConcurrentQueue<string>();
var Heartbeats = new ConcurrentQueue<HeartbeatEvent>();

var Http = new HttpClient { BaseAddress = new Uri(HubBaseUrl) };
var Grpc = GrpcChannel.ForAddress(GrpcBaseUrl);

var HistoryRequestMarshaller = Marshallers.Create(
    request =>
    {
        using var stream = new MemoryStream();
        using (var output = new CodedOutputStream(stream, leaveOpen: true))
        {
            if (request.Limit != 0)
            {
                output.WriteRawTag(8);
                output.WriteInt32(request.Limit);
            }

            output.Flush();
        }

        return stream.ToArray();
    },
    bytes =>
    {
        var limit = 0;
        var input = new CodedInputStream(bytes);
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 8:
                    limit = input.ReadInt32();
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }

        return new HistoryRequest(limit);
    });

var HistoryResponseMarshaller = Marshallers.Create(
    response =>
    {
        using var stream = new MemoryStream();
        using (var output = new CodedOutputStream(stream, leaveOpen: true))
        {
            if (!string.IsNullOrEmpty(response.Sender))
            {
                output.WriteRawTag(10);
                output.WriteString(response.Sender);
            }

            if (!string.IsNullOrEmpty(response.Framework))
            {
                output.WriteRawTag(18);
                output.WriteString(response.Framework);
            }

            if (!string.IsNullOrEmpty(response.Message))
            {
                output.WriteRawTag(26);
                output.WriteString(response.Message);
            }

            if (response.TimestampUtc != 0)
            {
                output.WriteRawTag(32);
                output.WriteInt64(response.TimestampUtc);
            }

            output.Flush();
        }

        return stream.ToArray();
    },
    bytes =>
    {
        var sender = string.Empty;
        var framework = string.Empty;
        var message = string.Empty;
        long timestampUtc = 0;

        var input = new CodedInputStream(bytes);
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 10:
                    sender = input.ReadString();
                    break;
                case 18:
                    framework = input.ReadString();
                    break;
                case 26:
                    message = input.ReadString();
                    break;
                case 32:
                    timestampUtc = input.ReadInt64();
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }

        return new HistoryMessageResponse(sender, framework, message, timestampUtc);
    });

var GetRecentMessagesMethod = new Method<HistoryRequest, HistoryMessageResponse>(
    MethodType.ServerStreaming,
    "chathistory.HistoryService",
    "GetRecentMessages",
    HistoryRequestMarshaller,
    HistoryResponseMarshaller);

var Hub = new HubConnectionBuilder()
    .WithUrl(ChatHubUrl)
    .WithAutomaticReconnect()
    .Build();

Hub.On<string, string, string>("ReceiveMessage", (sender, framework, message) =>
{
    var chatEvent = new ChatEvent(DateTimeOffset.Now, sender, framework, message);
    ReceivedMessages.Enqueue(chatEvent);
    Console.WriteLine($"[{chatEvent.ReceivedAt:HH:mm:ss}] [{framework}] {sender}: {message}");
});

Hub.On<string>("SystemNotice", message =>
{
    SystemNotices.Enqueue(message);
    Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] [SERVER] {message}");
});

Hub.On<string, long>("ServerHeartbeat", (uptime, timestampUtc) =>
{
    Heartbeats.Enqueue(new HeartbeatEvent(DateTimeOffset.Now, uptime, timestampUtc));
});

async Task ConnectAsync(CancellationToken ct = default)
{
    if (Hub.State == HubConnectionState.Connected)
    {
        Console.WriteLine($"Already connected to SignalR Hub (id: {Hub.ConnectionId ?? "n/a"}).");
        return;
    }

    await Hub.StartAsync(ct);
    Console.WriteLine($"Connected to SignalR Hub at {ChatHubUrl} (id: {Hub.ConnectionId ?? "n/a"}).");
}

async Task DisconnectAsync(CancellationToken ct = default)
{
    if (Hub.State == HubConnectionState.Disconnected)
    {
        return;
    }

    await Hub.StopAsync(ct);
    Console.WriteLine("Disconnected from SignalR Hub.");
}

async Task SendAsync(string message, string? sender = null, string? framework = null, CancellationToken ct = default)
{
    if (Hub.State != HubConnectionState.Connected)
    {
        await ConnectAsync(ct);
    }

    await Hub.InvokeAsync("SendMessage", sender ?? ReplUser, framework ?? ReplFramework, message, ct);
}

async Task SendBurstAsync(int count, string prefix = "Burst", int delayMs = 50, CancellationToken ct = default)
{
    for (var i = 1; i <= count; i++)
    {
        await SendAsync($"{prefix} #{i}/{count}", ct: ct);
        if (delayMs > 0 && i < count)
        {
            await Task.Delay(delayMs, ct);
        }
    }
}

async Task<IReadOnlyList<HistoryMessageResponse>> GetHistoryAsync(int limit = 20, bool print = true, CancellationToken ct = default)
{
    var callInvoker = Grpc.CreateCallInvoker();
    using var call = callInvoker.AsyncServerStreamingCall(
        GetRecentMessagesMethod,
        host: null,
        options: new CallOptions(cancellationToken: ct),
        request: new HistoryRequest(limit));

    var results = new List<HistoryMessageResponse>();
    await foreach (var item in call.ResponseStream.ReadAllAsync(ct))
    {
        results.Add(item);
        if (print)
        {
            var localTime = DateTimeOffset.FromUnixTimeSeconds(item.TimestampUtc).ToLocalTime();
            Console.WriteLine($"[HISTORY {localTime:HH:mm:ss}] [{item.Framework}] {item.Sender}: {item.Message}");
        }
    }

    if (print)
    {
        Console.WriteLine($"Loaded {results.Count} history message(s) from gRPC.");
    }

    return results;
}

async Task AnnounceAsync(string message, CancellationToken ct = default)
{
    using var response = await Http.PostAsync($"/announce?message={Uri.EscapeDataString(message)}", content: null, ct);
    response.EnsureSuccessStatusCode();
    Console.WriteLine($"Broadcasted system notice via POST /announce: '{message}'.");
}

async Task CheckHealthAsync(CancellationToken ct = default)
{
    foreach (var path in new[] { "/alive", "/health" })
    {
        using var response = await Http.GetAsync(path, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Health probe '{path}' returned {(int)response.StatusCode} ({response.ReasonPhrase}): {body}");
        }

        Console.WriteLine($"[OK] GET {path} -> {(int)response.StatusCode} ({body.Trim()})");
    }
}

async Task AssertRoundTripAsync(string? probeMessage = null, int timeoutSeconds = 5, CancellationToken ct = default)
{
    var probe = probeMessage ?? $"repl-probe-{Guid.NewGuid():N}";
    await SendAsync(probe, ct: ct);

    var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
    var received = false;
    while (DateTimeOffset.UtcNow < deadline)
    {
        if (ReceivedMessages.Any(m => m.Message == probe && m.Sender == ReplUser && m.Framework == ReplFramework))
        {
            received = true;
            break;
        }

        await Task.Delay(50, ct);
    }

    if (!received)
    {
        throw new InvalidOperationException($"Timed out after {timeoutSeconds}s waiting for SignalR ReceiveMessage for probe '{probe}'.");
    }

    Console.WriteLine($"[PASS] SignalR SendMessage -> ReceiveMessage round-trip verified for '{probe}'.");

    var history = await GetHistoryAsync(limit: 50, print: false, ct: ct);
    if (!history.Any(h => h.Message == probe && h.Sender == ReplUser && h.Framework == ReplFramework))
    {
        throw new InvalidOperationException($"Probe '{probe}' was not returned by gRPC HistoryService.GetRecentMessages.");
    }

    Console.WriteLine($"[PASS] gRPC HistoryService.GetRecentMessages verified ({history.Count} entries checked, probe found).");
}

async Task AssertAnnounceAsync(string? notice = null, int timeoutSeconds = 5, CancellationToken ct = default)
{
    var expected = notice ?? $"repl-notice-{Guid.NewGuid():N}";
    await AnnounceAsync(expected, ct);

    var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
    while (DateTimeOffset.UtcNow < deadline)
    {
        if (SystemNotices.Any(n => n == expected))
        {
            Console.WriteLine($"[PASS] POST /announce -> SignalR SystemNotice verified for '{expected}'.");
            return;
        }

        await Task.Delay(50, ct);
    }

    throw new InvalidOperationException($"Timed out after {timeoutSeconds}s waiting for SignalR SystemNotice '{expected}'.");
}

void Help()
{
    Console.WriteLine();
    Console.WriteLine("=== Trifold-Chatterbox REPL Session ===");
    Console.WriteLine($"  SignalR Hub : {ChatHubUrl}");
    Console.WriteLine($"  gRPC Host   : {GrpcBaseUrl}");
    Console.WriteLine($"  Identity    : {ReplUser} [{ReplFramework}]");
    Console.WriteLine();
    Console.WriteLine("State variables:");
    Console.WriteLine("  Hub, Grpc, Http, ReceivedMessages, SystemNotices, Heartbeats");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  await ConnectAsync()                       - Connect SignalR Hub");
    Console.WriteLine("  await DisconnectAsync()                    - Disconnect SignalR Hub");
    Console.WriteLine("  await SendAsync(\"Hello!\")                  - Send SignalR chat message");
    Console.WriteLine("  await SendBurstAsync(10)                   - Send burst of numbered messages");
    Console.WriteLine("  await GetHistoryAsync(20)                  - Stream recent history via gRPC");
    Console.WriteLine("  await AnnounceAsync(\"Maintenance soon\")    - Trigger POST /announce SystemNotice");
    Console.WriteLine("  await CheckHealthAsync()                   - Probe /alive and /health endpoints");
    Console.WriteLine("  await AssertRoundTripAsync()               - Verify SignalR + gRPC round-trip");
    Console.WriteLine("  await AssertAnnounceAsync()                - Verify /announce -> SystemNotice");
    Console.WriteLine("  Help()                                     - Show this summary");
    Console.WriteLine();
}

Console.WriteLine($"Chatterbox REPL initializing (Hub: {ChatHubUrl}, gRPC: {GrpcBaseUrl}, User: {ReplUser} [{ReplFramework}])");

if (AutoAssert)
{
    await CheckHealthAsync();
    await ConnectAsync();
    await AssertRoundTripAsync();
    await AssertAnnounceAsync();
    await DisconnectAsync();
}
else
{
    try
    {
        await ConnectAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[WARN] Could not connect to HubServer at {ChatHubUrl}: {ex.Message}");
        Console.WriteLine("       Start HubServer (or AspireHost), then run: await ConnectAsync()");
    }

    Help();
}
