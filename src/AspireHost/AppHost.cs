var builder = DistributedApplication.CreateBuilder(args);

var hubServer = builder.AddProject<Projects.HubServer>("hubserver")
    .WithEndpoint("http", e =>
    {
        e.Port = 5000;
        e.TargetPort = 5000;
        e.IsProxied = false;
    })
    .WithHttpEndpoint(port: 5001, targetPort: 5001, name: "grpc", isProxied: false)
    .WithExternalHttpEndpoints();

ExecuteCommandResult LaunchTerminals(string clientMode)
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
    psi.ArgumentList.Add("-Client");
    psi.ArgumentList.Add(clientMode);

    string userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
    string machinePath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
    psi.EnvironmentVariables["Path"] = $"{userPath};{machinePath}";

    System.Diagnostics.Process.Start(psi);
    return new ExecuteCommandResult { Success = true };
}

var terminalCommandOptions = new CommandOptions
{
    IconName = "WindowDevTools",
    IconVariant = IconVariant.Filled
};

builder.AddProject<Projects.Client_Spectre>("client-spectre")
    .WithReference(hubServer)
    .WaitFor(hubServer)
    .WithCommand(
        name: "launch-3-terminals",
        displayName: "Launch 3 Interactive Terminals",
        executeCommand: _ => Task.FromResult(LaunchTerminals("Spectre")),
        commandOptions: terminalCommandOptions);

builder.AddProject<Projects.Client_TerminalGui>("client-terminalgui")
    .WithReference(hubServer)
    .WaitFor(hubServer)
    .WithCommand(
        name: "launch-3-terminalgui-terminals",
        displayName: "Launch 3 Terminal.Gui Terminals",
        executeCommand: _ => Task.FromResult(LaunchTerminals("TerminalGui")),
        commandOptions: terminalCommandOptions)
    .WithCommand(
        name: "launch-3-mixed-terminals",
        displayName: "Launch 3 Mixed Terminals",
        executeCommand: _ => Task.FromResult(LaunchTerminals("Mixed")),
        commandOptions: terminalCommandOptions);

builder.AddProject<Projects.Client_Jumbee>("client-jumbee")
    .WithReference(hubServer)
    .WaitFor(hubServer)
    .WithCommand(
        name: "launch-3-jumbee-terminals",
        displayName: "Launch 3 Jumbee Terminals",
        executeCommand: _ => Task.FromResult(LaunchTerminals("Jumbee")),
        commandOptions: terminalCommandOptions)
    .WithCommand(
        name: "launch-3-mixed-terminals",
        displayName: "Launch 3 Mixed Terminals",
        executeCommand: _ => Task.FromResult(LaunchTerminals("Mixed")),
        commandOptions: terminalCommandOptions);

await builder.Build().RunAsync();
