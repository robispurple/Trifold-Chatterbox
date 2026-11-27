using HubServer.Hubs;
using HubServer.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.SignalR;
using ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

var httpPort = builder.Configuration.GetValue("ASPNETCORE_HTTP_PORTS", 5000);
var grpcPort = builder.Configuration.GetValue("GrpcPort", 5001);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(httpPort, o => o.Protocols = HttpProtocols.Http1);
    options.ListenAnyIP(grpcPort, o => o.Protocols = HttpProtocols.Http2);
});

builder.AddServiceDefaults();
builder.Services.AddSignalR();
builder.Services.AddGrpc();
builder.Services.AddSingleton<HistoryServiceImpl>();
builder.Services.AddHostedService<ServerHeartbeatService>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapGrpcService<HistoryServiceImpl>();
app.MapHub<ChatHub>("/chat");
app.MapPost("/announce", async (string message, IHubContext<ChatHub> hubContext) =>
{
    await hubContext.Clients.All.SendAsync("SystemNotice", message);
    return Results.Ok(new { sent = message });
});

await app.RunAsync();

