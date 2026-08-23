# Trifold-Chatterbox

A distributed real-time communication sandbox benchmarking three TUI architectures against a hybrid SignalR + gRPC .NET 10 backend orchestrated with .NET Aspire.

## Tech Stack & Language Conventions

- **Platform:** .NET 10, C# 14
- **Language Rules:**
  - When generating new C# code, please follow the existing coding style.
  - All code should be compatible with C# 14.0.
  - Prefer new C# 14.0 features and syntax where applicable.
  - Prefer functional programming paradigms and constructs where appropriate.
  - Prefer concise code over more verbose constructs.
- **Coding Style:**
  - Use the existing #regions in a file to organize class constructors, indexers, events, properties, methods, fields, and child types.
  - Use 4 spaces for indentation.
  - Use camel-case for method and property names. Method and property names should begin with a capital letter.
  - Use camel-case for class fields. Field names should begin with lower-case letters unless they are backing fields for properties which should begin with an underscore.
- **Package Management:** Central Package Management (CPM) via `Directory.Packages.props`.


## Project Structure

```txt
src/
├── AspireHost/        # Aspire Orchestrator (service discovery & telemetry)
├── ServiceDefaults/   # OTel metrics/tracing, health checks (shared class library)
├── Contracts/         # Protobuf contracts (chat_history.proto)
├── HubServer/         # Kestrel host (SignalR /chat + gRPC HistoryService)
├── Client.Spectre/    # Spectre.Console (live canvas / render loop)
├── Client.TerminalGui/# Terminal.Gui (widget tree, UI-thread marshaling)
└── Client.Jumbee/     # Jumbee.Console (differential ANSI frame buffer)
repl/                  # dotnet-repl CSX scripts (stretch)
tests/IntegrationTests/# Testcontainers + xUnit (stretch)
```

## UI Implementations

- Spectre.Console <https://spectreconsole.net/console/>
- Terminal.Gui <https://github.com/tui-cs/Terminal.Gui/blob/develop/llms.txt>
- Jumbee.Console <https://github.com/allisterb/Jumbee.Console/blob/master/llms.txt>

## Protocol & Interaction Model

- **SignalR (`/chat`):** Ephemeral, high-frequency bidirectional live push (`SendMessage` invocation, `ReceiveMessage` event).
- **gRPC (`HistoryService`):** Contract-first HTTP/2 server streaming (`GetRecentMessages`) for history replay on client boot.
- **Client Lifecycle:** Connect via gRPC to stream history -> establish SignalR connection for live messages -> render via TUI engine.

## Tooling & Commands

### .NET CLI

```shell
dotnet build
dotnet test

# Run full Aspire orchestration stack locally
dotnet run --project src/AspireHost

# Run services individually
dotnet run --project src/HubServer
dotnet run --project src/Client.Spectre
```

### Aspire CLI

```shell
aspire -h
# Get resources
aspire ps
# Rebuild a specific resource
aspire resource <resource-name> rebuild
```

### Docker & Container Tooling

Both `HubServer` and `Client.Spectre` support .NET SDK container publishing and Docker Compose orchestration on the `chatterbox-net` network.

```shell
# Build container images with .NET SDK
dotnet publish src/HubServer/HubServer.csproj -t:PublishContainer
dotnet publish src/Client.Spectre/Client.Spectre.csproj -t:PublishContainer

# Or build via Docker Compose
docker compose build

# Start the containerized stack in the background
docker compose up -d

# Interactive TUI session with Client.Spectre (Recommended)
docker compose run --rm client-spectre

# Attach to the running background Client.Spectre container
docker attach chatterbox-client-spectre

# Exec into container / spawn a new client process
docker exec -it chatterbox-client-spectre dotnet Client.Spectre.dll

# Stop container stack
docker compose down
```

### Playwright MCP

Use the Playwright MCP to run the app in a browser and test the UI.

For UI work, get access first thing **before** you start!

