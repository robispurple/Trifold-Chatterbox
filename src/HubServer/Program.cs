using HubServer.Hubs;
using ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddSignalR();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapHub<ChatHub>("/chat");

await app.RunAsync();

