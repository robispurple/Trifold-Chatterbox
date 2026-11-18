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
            string standardPwsh = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "PowerShell",
                "7",
                "pwsh.exe");
            string fallbackPwsh = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
            string powershellPath = File.Exists(standardPwsh) ? standardPwsh : fallbackPwsh;

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = powershellPath,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(scriptPath);

            string userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
            string machinePath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
            psi.EnvironmentVariables["Path"] = $"{userPath};{machinePath}";

            System.Diagnostics.Process.Start(psi);
            return Task.FromResult(new ExecuteCommandResult { Success = true });
        },
        commandOptions: new CommandOptions
        {
            IconName = "WindowDevTools",
            IconVariant = IconVariant.Filled
        });

await builder.Build().RunAsync();
