# Trifold-Chatterbox

A distributed real-time communication sandbox designed to explore, benchmark, and compare terminal user interface (TUI) architectures while implementing production-grade .NET cloud-native patterns.

---

## Fresh Clone Quickstart: Step-by-Step

Follow either the **Native .NET** or **Docker Container** workflow below to go from a brand new `git clone` to a running 3-terminal split-pane chat session in seconds.

### Workflow A: Native .NET (Fastest for Local Dev)

```pwsh
# 1. Clone the repository
git clone https://github.com/robispurple/Trifold-Chatterbox.git
cd Trifold-Chatterbox

# 2. Install tooling dependencies (Mise / Winget)
mise install
# Or: winget install Microsoft.Aspire

# 3. Build the entire solution
dotnet build

# 4. Start the Aspire orchestrator backend in Terminal 1
dotnet run --project src/AspireHost

# 5. In a new PowerShell terminal, launch the 3-client split-pane session
.\scripts\launch-terminals.ps1
```

*Result: Windows Terminal opens with 3 tiled panes (`Alice`, `Bob`, and `Charlie`) connected to the live SignalR hub.*

---

### Workflow B: Docker Containers (Hermetic & Isolated)

```pwsh
# 1. Clone the repository
git clone https://github.com/robispurple/Trifold-Chatterbox.git
cd Trifold-Chatterbox

# 2. Ensure Docker Desktop is running, then build container images
docker compose build

# 3. Launch the 3-container split-pane session
.\scripts\launch-terminals.ps1 -Docker
```

*Result: Automatically starts `hubserver` and the `aspire-dashboard` (open `http://localhost:18888` in your browser to view live OTel traces) on `chatterbox-net` and opens 3 tiled Windows Terminal panes, each running an isolated container instance (`Alice`, `Bob`, `Charlie`).*

---

## Tooling Prerequisites & Setup

Mise is used for controlling versions of our tooling:

```pwsh
mise install
```

If Mise has issues with Aspire, install via Winget:

```pwsh
winget install Microsoft.Aspire
```

Restore local .NET CLI tools (`dotnet-repl` and `csharprepl`):

```pwsh
dotnet tool restore
```

---

## Running the Applications

### 1. Aspire Orchestrator (Recommended for Local Dev)

Runs the full distributed stack (`HubServer`, `Client.Spectre`, `Client.TerminalGui`, and `Client.Jumbee`) with automatic service discovery and the Aspire OpenTelemetry dashboard:

```pwsh
# Launch with .NET CLI
dotnet run --project src/AspireHost

# Or launch via Aspire CLI
aspire start
```

### 2. Multi-Client 3-Terminal Sandbox (One-Command Testing)

To immediately launch **3 interactive clients** side-by-side in Windows Terminal (`Alice`, `Bob`, and `Charlie`) connected to the hub, use `-Client` (`Spectre` [default], `TerminalGui`, `Jumbee`, or `Mixed`):

```pwsh
# Native .NET Mode (connects to running Aspire or HubServer):
.\scripts\launch-terminals.ps1                        # 3x Client.Spectre (default)
.\scripts\launch-terminals.ps1 -Client TerminalGui    # 3x Client.TerminalGui
.\scripts\launch-terminals.ps1 -Client Jumbee         # 3x Client.Jumbee
.\scripts\launch-terminals.ps1 -Client Mixed          # Alice (Spectre) + Bob (TerminalGui) + Charlie (Jumbee)

# Docker Container Mode (runs 3 docker containers connected over chatterbox-net):
.\scripts\launch-terminals.ps1 -Docker
.\scripts\launch-terminals.ps1 -Docker -Client TerminalGui
.\scripts\launch-terminals.ps1 -Docker -Client Jumbee
.\scripts\launch-terminals.ps1 -Docker -Client Mixed
```

*Tip: When running `AspireHost`, you can also click **"Launch 3 Interactive Terminals"** on `client-spectre`, **"Launch 3 Terminal.Gui Terminals"** / **"Launch 3 Mixed Terminals"** on `client-terminalgui`, or **"Launch 3 Jumbee Terminals"** / **"Launch 3 Mixed Terminals"** on `client-jumbee` in the Aspire Web Dashboard!*

---

### 3. Running Services Individually (.NET CLI)

You can launch each service standalone in separate terminals:

**Terminal 1 — HubServer:**

```pwsh
dotnet run --project src/HubServer
```

**Terminal 2 — Client.Spectre:**

```pwsh
dotnet run --project src/Client.Spectre
```

**Terminal 3 — Client.TerminalGui:**

```pwsh
dotnet run --project src/Client.TerminalGui
```

**Terminal 4 — Client.Jumbee:**

```pwsh
dotnet run --project src/Client.Jumbee
```

---

### 4. Interactive REPL Diagnostics (`dotnet-repl` & `CSharpRepl`)

Trifold-Chatterbox configures two complementary .NET REPL tools in [.config/dotnet-tools.json](.config/dotnet-tools.json) (`dotnet tool restore`), each tailored to a distinct workflow:

| Capability | `dotnet-repl` ([repl/chat-session.csx](repl/chat-session.csx)) | `CSharpRepl` ([repl/live-inspect.csx](repl/live-inspect.csx)) |
| --- | --- | --- |
| **Primary Strength** | Out-of-process protocol probing, polyglot scripting & CI assertions | In-process live attachment, DI inspection, direct state/hub calls & rich standalone C# REPL |
| **Target Coupling** | None — black-box client over SignalR, gRPC, and HTTP | Opt-in local dev startup hooks via `csharprepl connect init` |
| **Live Memory & DI Access** | No — observes only public endpoints | Yes — exposes `services` (`IServiceProvider`) and `Get<T>()` inside the live process |
| **Runtime Method Detours** | No | Supported on `.NET 10` and earlier (`#wrap`, `#replace`, `#revert`); CoreCLR `11.0.0` is not yet supported by `MonoMod.RuntimeDetour` in `0.9.2` |
| **Scripting & CI Export** | C#, F#, PowerShell, HTTP; `.csx`/`.dib`/`.ipynb`; `--exit-after-run` & `--output-format trx\|ipynb` | C# only; syntax highlighting, IntelliSense, `-r *.csproj`, `--eval`, `--eval-file` |
| **Remote / Docker Targets** | Yes — works against native, Aspire, or Docker stacks | Local PID attach only (`connect`); standalone mode works anywhere |

**When to Use Which:**
- Use **`dotnet-repl`** when testing external contracts as a real client: replaying gRPC history, stress-testing the TUI clients with `SendBurstAsync(10)`, running automated round-trip smoke assertions (`REPL_ASSERT=true`), or probing Docker containers.
- Use **`CSharpRepl`** when debugging inside a running `HubServer` process: reading `HistoryServiceImpl` memory without network calls, mutating state directly (`InjectHistory`), pushing broadcasts via `IHubContext<ChatHub>`, or exploring solution types standalone (`dotnet csharprepl -r src/Contracts/Contracts.csproj`).

#### Workflow A: Out-of-Process Protocol Probing (`dotnet-repl`)

Start `HubServer` (or `AspireHost`), then launch the probing script:

```pwsh
dotnet repl --run repl/chat-session.csx
```

Inside the interactive C# prompt, call the preloaded helpers:

```csharp
await SendAsync("Hello from REPL!")
await SendBurstAsync(10)
await GetHistoryAsync(20)
await AnnounceAsync("Maintenance in 5 minutes")
await AssertRoundTripAsync()
await CheckHealthAsync()
Help()
```

Run the non-interactive smoke-test and assertion suite (exiting `0` on pass, non-zero on failure):

```pwsh
$env:REPL_ASSERT="true"; dotnet repl --run repl/chat-session.csx --exit-after-run
```

Optional environment variable overrides:
- `HubServerUrl` (default `http://localhost:5000`)
- `GrpcServerUrl` (default `http://localhost:5001`)
- `CHAT_USERNAME` (default `ReplProbe`)
- `REPL_FRAMEWORK` (default `REPL`)

#### Workflow B: Live In-Process Debugging (`CSharpRepl`)

1. Start `HubServer` with the local development startup hooks enabled:

```pwsh
.\repl\start-hub-attachable.ps1
```

2. In a second terminal, list attachable processes and attach to `HubServer`'s PID with [repl/live-inspect.csx](repl/live-inspect.csx) preloaded:

```pwsh
dotnet csharprepl connect list
dotnet csharprepl connect <PID> repl/live-inspect.csx
```

*(Tip: Pass `repl/live-inspect.csx` on the CLI so `CSharpRepl` loads it from your repo root. Expression return values display directly in `CSharpRepl`, whereas `Console.WriteLine` prints in `HubServer`'s stdout.)*

3. Inspect live DI singletons and invoke hub/history methods in-process:

```csharp
InspectEnvironment()
DumpHistory()
InjectHistory("Added directly in-process")
await BroadcastNoticeAsync("Notice sent straight via DI")
await BroadcastChatAsync("Live broadcast from CSharpRepl!")
Get<HubServer.Services.HistoryServiceImpl>().Count
```

4. You can also launch `CSharpRepl` standalone against project assemblies without attaching:

```pwsh
dotnet csharprepl -r src/Contracts/Contracts.csproj
```

> **Development Only:** `csharprepl connect` injects a Roslyn scripting engine into the target process with full execution privileges. Never set `DOTNET_STARTUP_HOOKS` or `ASPNETCORE_HOSTINGSTARTUPASSEMBLIES` in production or globally. Note also that `#wrap` / `#replace` method detours in `CSharpRepl 0.9.2` target `.NET 10` and earlier and currently report `CoreCLR version 11.0.0 is not supported` on `.NET 11`.

---

## Running in Docker Containers

`HubServer`, `Client.Spectre`, `Client.TerminalGui`, and `Client.Jumbee` are configured with .NET built-in container support (`Microsoft.NET.Build.Containers`) and Docker Compose. `ServiceDefaults` and `Contracts` are compiled directly into the applications.

### 1. Build Container Images

You can build the container images via Docker Compose or .NET SDK:

**Option A: Using Docker Compose Build (Recommended)**

```pwsh
docker compose build
```

**Option B: Using .NET SDK Container Publishing**

```pwsh
# Build HubServer container image (trifold/hubserver:latest)
dotnet publish src/HubServer/HubServer.csproj -t:PublishContainer

# Build Client.Spectre container image (trifold/client-spectre:latest)
dotnet publish src/Client.Spectre/Client.Spectre.csproj -t:PublishContainer

# Build Client.TerminalGui container image (trifold/client-terminalgui:latest)
dotnet publish src/Client.TerminalGui/Client.TerminalGui.csproj -t:PublishContainer

# Build Client.Jumbee container image (trifold/client-jumbee:latest)
dotnet publish src/Client.Jumbee/Client.Jumbee.csproj -t:PublishContainer
```

---

### 2. Start the Containerized Stack

Start the backend services in the background on the shared `chatterbox-net` network:

```pwsh
docker compose up -d
```

---

### 3. Interacting with the TUI Clients in Docker

Because `Client.Spectre`, `Client.TerminalGui`, and `Client.Jumbee` are interactive console applications:

**Method 1 — Launch a dedicated interactive TUI session (Recommended):**

```pwsh
docker compose run --rm client-spectre
# Or for Terminal.Gui:
docker compose run --rm client-terminalgui
# Or for Jumbee.Console:
docker compose run --rm client-jumbee
```

**Method 2 — Attach to the background client container:**

```pwsh
# Attach directly to the running container's TTY and STDIN
docker attach chatterbox-client-spectre
# Or for Terminal.Gui:
docker attach chatterbox-client-terminalgui
# Or for Jumbee.Console:
docker attach chatterbox-client-jumbee
```

*(To detach without stopping the container, press `Ctrl+P`, then `Ctrl+Q`)*

**Method 3 — Spawn a new shell or second client instance:**

```pwsh
docker exec -it chatterbox-client-spectre dotnet Client.Spectre.dll
# Or for Terminal.Gui:
docker exec -it chatterbox-client-terminalgui dotnet Client.TerminalGui.dll
# Or for Jumbee.Console:
docker exec -it chatterbox-client-jumbee dotnet Client.Jumbee.dll
```

---

### 4. Stop the Docker Environment

```pwsh
docker compose down
```

---

## Testing and Verification

- **Build solution:** `dotnet build`
- **Run tests:** `dotnet test`
- **REPL smoke test (`dotnet-repl`):** `$env:REPL_ASSERT="true"; dotnet repl --run repl/chat-session.csx --exit-after-run` (against a running `HubServer`)
- **Live REPL attach (`CSharpRepl`):** `.\repl\start-hub-attachable.ps1`, then `dotnet csharprepl connect list` and `dotnet csharprepl connect <PID> repl/live-inspect.csx`
- **Check HubServer Health:** `curl http://localhost:5000/alive` (or `http://localhost:5000/health`)
- **Aspire Telemetry:** Check the Aspire dashboard URL printed upon `dotnet run --project src/AspireHost` for live traces, metrics, and structured logs.

---

## Troubleshooting & Tips

### Port 5000 in Use (`Address already in use`)
Both the local `HubServer` (started by `AspireHost`) and the Docker Compose `hubserver` container expose port `5000`. If you previously started the Docker backend and want to switch back to local `AspireHost`:
```pwsh
# Free up port 5000 by stopping Docker containers
docker compose down
```

### Telemetry Differentiation in Aspire Dashboard
Each client instance automatically names its OpenTelemetry service after its framework and username (e.g. `Client.Spectre.Alice`, `Client.TerminalGui.Bob`, `Client.Jumbee.Charlie`). In the Aspire Dashboard, you can filter traces, structured logs, and metrics by individual user clients from the resource dropdown.

### Hybrid Mode (Local AspireHost + Docker Clients)
You can run `dotnet run --project src/AspireHost` on your host machine to get the full .NET 11 Aspire Dashboard, and then run `.\scripts\launch-terminals.ps1 -Docker`. The script will detect the local host backend and seamlessly connect the containerized clients via `host.docker.internal:5000`.
