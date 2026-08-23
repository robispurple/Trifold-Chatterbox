var builder = DistributedApplication.CreateBuilder(args);

var hubServer = builder.AddProject<Projects.HubServer>("hubserver")
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.Client_Spectre>("client-spectre")
    .WithReference(hubServer)
    .WaitFor(hubServer);

await builder.Build().RunAsync();
