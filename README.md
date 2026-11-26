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
