var builder = DistributedApplication.CreateBuilder(args);

var hubServer = builder.AddProject<Projects.HubServer>("hubserver");

builder.AddProject<Projects.Client_Spectre>("client-spectre")
    .WithReference(hubServer);

await builder.Build().RunAsync();
