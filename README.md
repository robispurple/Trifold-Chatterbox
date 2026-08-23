# Trifold-Chatterbox

A distributed real-time communication sandbox designed to explore, benchmark, and compare terminal user interface (TUI) architectures while implementing production-grade .NET cloud-native patterns.

---

## Tooling Prerequisites & Setup

Mise is used for controlling versions of our tooling:

```pwsh
mise install
```

If Mise has issues with Aspire, use Winget:

```pwsh
winget install Microsoft.Aspire
```

---

## Running the Applications

### 1. Aspire Orchestrator (Recommended for Local Dev)

Runs the full distributed stack (`HubServer` and `Client.Spectre`) with automatic service discovery and OpenTelemetry dashboard:

```pwsh
# Launch with .NET CLI
dotnet run --project src/AspireHost

# Or launch via Aspire CLI
aspire start
```

### 2. Running Services Individually (.NET CLI)

You can launch each service standalone in separate terminals:

**Terminal 1 — HubServer:**
```pwsh
dotnet run --project src/HubServer
```

**Terminal 2 — Client.Spectre:**
```pwsh
dotnet run --project src/Client.Spectre
```

---

## Running in Docker Containers

Both `HubServer` and `Client.Spectre` are configured with .NET built-in container support (`Microsoft.NET.Build.Containers`) and Docker Compose. `ServiceDefaults` is compiled directly into both applications.

### 1. Build Container Images

You can build the container images via .NET SDK or Docker:

**Option A: Using .NET SDK Container Publishing**
```pwsh
# Build HubServer container image (trifold/hubserver:latest)
dotnet publish src/HubServer/HubServer.csproj -t:PublishContainer

# Build Client.Spectre container image (trifold/client-spectre:latest)
dotnet publish src/Client.Spectre/Client.Spectre.csproj -t:PublishContainer
```

**Option B: Using Docker Compose Build**
```pwsh
docker compose build
```

---

### 2. Start the Containerized Stack

Start the backend services on the shared `chatterbox-net` network:

```pwsh
# Start all containers in the background
docker compose up -d
```

---

### 3. Interacting with the Spectre.Console TUI Client in Docker

Because `Client.Spectre` is an interactive console application:

**Method 1 — Launch a dedicated interactive TUI session (Recommended):**
```pwsh
docker compose run --rm client-spectre
```

**Method 2 — Attach to the background client container:**
```pwsh
# Attach directly to the running container's TTY and STDIN
docker attach chatterbox-client-spectre
```
*(To detach without stopping the container, press `Ctrl+P`, then `Ctrl+Q`)*

**Method 3 — Spawn a new shell or second client instance:**
```pwsh
docker exec -it chatterbox-client-spectre dotnet Client.Spectre.dll
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
