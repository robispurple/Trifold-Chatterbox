# Trifold-Chatterbox

A distributed real-time communication sandbox benchmarking three TUI architectures against a hybrid SignalR + gRPC .NET 10 backend orchestrated with .NET Aspire.

For full developer workflows, setup, and container guides, see [README.md](README.md).

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

### Playwright MCP

Use the Playwright MCP to run the app in a browser and test the UI.

For UI work, get access first thing **before** you start!
