# Trifold-Chatterbox

A distributed real-time communication sandbox benchmarking three TUI architectures against a hybrid SignalR + gRPC .NET 10 backend orchestrated with .NET Aspire.

For full developer workflows, setup, and container guides, see [README.md](README.md).

## Tech Stack & Language Conventions

- **Platform:** .NET 10, C# 14.0
- **Package Management:** Central Package Management (CPM) via `Directory.Packages.props`.
- **Language & Style Rules:**
  - Follow existing coding style and preserve `#region` organization (constructors, properties, methods, fields).
  - Indentation: 4 spaces.
  - Method and property names: PascalCase (`CamelCase` with leading capital).
  - Fields: camelCase (backing fields prefixed with `_`).
  - Prefer modern C# 14 features, functional paradigms, and concise constructs.

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
scripts/               # Automated multi-client launch scripts (launch-terminals.ps1)
```

## Protocol & Architecture

- **SignalR (`/chat`):** Ephemeral, high-frequency bidirectional live push (`SendMessage` / `ReceiveMessage`).
- **gRPC (`HistoryService`):** Contract-first HTTP/2 server streaming (`GetRecentMessages`) for history replay on boot.
- **Service Discovery:** Resolved dynamically via Aspire (`services:hubserver:http` / `services:hubserver:https`) or fallback `HubServerUrl`.

## Essential Commands

```shell
dotnet build                                    # Build solution
dotnet test                                     # Run tests
dotnet run --project src/AspireHost             # Launch full Aspire stack
dotnet run --project src/HubServer              # Run HubServer standalone
dotnet run --project src/Client.Spectre         # Run Spectre client standalone
.\scripts\launch-terminals.ps1 [-Docker]        # Launch 3-client split-pane session
```
