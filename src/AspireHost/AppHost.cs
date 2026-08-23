var builder = DistributedApplication.CreateBuilder(args);

var hubServer = builder.AddProject<Projects.HubServer>("hubserver")
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.Client_Spectre>("client-spectre")
    .WithReference(hubServer)
    .WaitFor(hubServer)
    .WithCommand(
        name: "launch-3-terminals",
        displayName: "Launch 3 Interactive Terminals",
        executeCommand: _ =>
        {
            string scriptPath = Path.GetFullPath("../../scripts/launch-terminals.ps1");
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "pwsh.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = false
            };
            string userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
            string machinePath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
            psi.EnvironmentVariables["Path"] = $"{userPath};{machinePath}";

            System.Diagnostics.Process.Start(psi);
            return Task.FromResult(new ExecuteCommandResult { Success = true });
        },
        iconName: "WindowDevTools",
        iconVariant: IconVariant.Filled);

await builder.Build().RunAsync();
