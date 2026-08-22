# Product Requirements Document (PRD): Trifold-Chatterbox (v2 — Learning-First Revision)

**Project Name:** Trifold-Chatterbox
**Target Framework:** .NET 10
**Stack Components:** ASP.NET Core SignalR, gRPC, .NET Aspire, Testcontainers, dotnet-repl, Spectre.Console, Terminal.Gui, Jumbee.Console

---

## 0. Revision Notes (v1 → v2)

This revision does not change the target architecture. It reorders implementation around the project's actual goal: **learning each technology deeply**, not shipping a finished product on the first pass. Key changes:

- Implementation is restructured into **learning milestones** rather than strictly top-down phases. Each milestone produces a working, observable system before the next is added.
- **.NET Aspire is introduced in Milestone 1**, alongside the first SignalR client, rather than bolted on at the end. Aspire's OpenTelemetry dashboard gives early visibility into what SignalR (and later gRPC) are doing under the hood, which is directly useful for debugging while those protocols are still unfamiliar. The risk of conflating an Aspire wiring bug with a SignalR bug is judged smaller than the value of tracing from day one, since Aspire failures tend to be loud (dashboard red, service won't launch) rather than subtle.
- **TUI clients are added one at a time**, each against an already-working backend, so every new client is purely "learn this rendering paradigm" rather than "debug new infrastructure and a new paradigm simultaneously."
- **Testcontainers and dotnet-repl are moved to an optional stretch milestone.** They are testing/tooling concerns, not among the four core technologies the project exists to teach (TUI frameworks, SignalR, gRPC, Aspire).
- Acceptance criteria are now attached per-milestone instead of only at the end, so progress is checkable throughout.

---

## 1. Executive Summary & Goals

Trifold-Chatterbox is a distributed real-time communication sandbox designed to explore, benchmark, and compare terminal user interface (TUI) architectures while implementing production-grade .NET cloud-native patterns.

### Core Objectives

- **TUI Paradigm Comparison:** Implement three distinct terminal frontend architectures (Spectre.Console, Terminal.Gui, Jumbee.Console) consuming identical real-time event streams, and gain hands-on experience with each.
- **Hybrid Protocol Transport:** Combine SignalR (bi-directional WebSocket push for active chat) with gRPC (contract-first HTTP/2 server streaming for initial message history replay and state sync), and understand why each is suited to its role.
- **Modern Orchestration & Telemetry:** Use .NET Aspire for local service discovery, OpenTelemetry collection, and dynamic dependency wiring — learned early enough to be a debugging aid, not just a wrapper applied at the end.
- **Interactive Diagnostics:** Enable live probing, state injection, and RPC execution via dotnet-repl scripts (stretch goal).
- **Hermetic Integration Testing:** Leverage Testcontainers for reproducible, self-contained CI testing (stretch goal).

---

## 2. System Architecture & Topology

**Topology overview:**

- **Aspire AppHost / Runner** — Service orchestration & telemetry; supervises all projects below.
  - **Interactive REPL** (dotnet-repl) — connects directly to both the SignalR Hub and the gRPC service for manual probing.
  - **HubServer (API)** — single Kestrel instance, multiple endpoints:
    - **SignalR Hub** (`/chat`, WebSocket/WSS) — real-time, bidirectional live push.
    - **gRPC Service** (`/history`, HTTP/2 Protobuf) — history sync / streaming on connect.
  - **Integration Tests** (Testcontainers) — spins up HubServer in a container for hermetic end-to-end validation.
  - **TUI Clients** — each opens a gRPC channel for history replay on startup, then a SignalR connection for live updates:
    - **Client.Spectre** — live canvas / streaming render loop.
    - **Client.TerminalGui** — object-oriented widget tree.
    - **Client.Jumbee** — differential ANSI frame buffer engine.

---

## 3. Architectural Decisions & Rationale

### 3.1 Why SignalR Requires a Central Hub (vs. P2P Mesh)

- **Hub-and-Spoke Lifecycle:** SignalR is fundamentally a client-server RPC framework. The Hub tracks connection IDs, handles keep-alive heartbeats, negotiates connection fallbacks (WebSockets → Server-Sent Events → Long Polling), and multiplexes group broadcasting.
- **NAT Traversal & Security:** SignalR lacks native P2P broker protocols (ICE/STUN/TURN). A centralized Kestrel instance provides a stable ingress point across NAT boundaries without client port-forwarding.

### 3.2 Role of Redis: Backplane vs. Direct Client Access

- **The Redis Backplane Pattern:** SignalR tracks connection state in local server memory. When scaling out to multiple Hub replicas behind a load balancer, Redis acts as a pub/sub backplane so that Server A publishes messages to Redis, which fans out to Server B and Server C.
- **Why Direct Client-to-Redis Is Prohibited:**
  - Exposes raw database ports (6379) and database credentials directly to client applications.
  - Lacks application-tier authorization, authentication, rate limiting, and input sanitization.
  - Cannot perform business validation or relational database persistence inline prior to dispatch.

### 3.3 Hybrid SignalR + gRPC Model

- **SignalR:** Dedicated to ephemeral, high-frequency, bidirectional live events (typing indicators, new messages, presence).
- **gRPC:** Dedicated to strongly typed, high-throughput binary streaming. Used upon client boot to stream chunked message history (`GetRecentMessages`) over HTTP/2 using Protobuf payloads.

### 3.4 Why Aspire Is Introduced Early (New in v2)

- **Observability while learning is more valuable than observability after the fact.** The Aspire dashboard surfaces OpenTelemetry traces for SignalR negotiation/upgrade and gRPC calls as they happen, which shortens the feedback loop while these protocols are still new.
- **Service discovery removes a class of accidental bugs.** Manually tracking HubServer's port across three separate client projects invites typos and stale config; Aspire injects endpoints via environment variables instead.
- **Low integration cost.** Adding a project to `DistributedApplication` and calling `.WithReference(hub)` is a small, additive change — it does not require restructuring code written without Aspire in mind, so introducing it early does not create rework later.

---

## 4. Technical Requirements & Specifications

### 4.1 HubServer (Backend Core)

- **Host Engine:** ASP.NET Core Minimal API / Kestrel.
- **Protocols:** Combined HTTP/1.1 (SignalR fallback), HTTP/2 (gRPC), and WebSockets.
- **Endpoints:**
  - `/chat` → SignalR Hub (`ChatHub`)
  - `HistoryService` → gRPC Service (`HistoryServiceImpl`)
- **State Management:** In-memory circular buffer for the initial tier; prepared for Redis backplane scale-out.

### 4.2 TUI Client Matrix

| Client Project | Paradigm / Mental Model | Rendering Strategy | User Interaction Loop |
|---|---|---|---|
| Client.Spectre | Streaming / Live Render Loop | `AnsiConsole.Live(table)` background task refresh | Standard `Console.ReadLine()` or key listener |
| Client.TerminalGui | Object-Oriented Widget Tree | Main UI thread marshaling via `Application.Invoke(...)` | Modal UI event loop with `ListView`, `TextField`, `Button` |
| Client.Jumbee | Differential ANSI Frame Buffer | Viewport dirty tracking, `screen.RequestRender()` | Direct ANSI escape sequence buffer renderer |

> **Note:** Confirm Jumbee.Console (allisterb/Jumbee.Console) builds cleanly against the target .NET 10 TFMs before Milestone 3 — it is a smaller, less widely-used project than Spectre.Console or Terminal.Gui, so verify compatibility early rather than discovering issues mid-milestone.

### 4.3 Orchestration & Tooling

- **.NET Aspire (AppHost):** Programmatic topology orchestration, dependency injection of endpoints via environment variables, and centralized dashboard observability.
- **Testcontainers:** Containerized integration test execution spinning up ephemeral instances for end-to-end client/server validation. *(Stretch)*
- **dotnet-repl:** CSX script (`repl/chat-session.csx`) linking NuGet packages dynamically to provide an interactive CLI for testing message schemas without launching a full TUI. *(Stretch)*

---

## 5. Interface Contracts & Data Schemas

### 5.1 Protobuf Contract (`Protos/chat_history.proto`)

```protobuf
syntax = "proto3";

option csharp_namespace = "Trifold-Chatterbox.Contracts";

package chathistory;

service HistoryService {
  rpc GetRecentMessages (HistoryRequest) returns (stream HistoryMessageResponse);
}

message HistoryRequest {
  int32 limit = 1;
}

message HistoryMessageResponse {
  string sender = 1;
  string framework = 2;
  string message = 3;
  int64 timestamp_utc = 4;
}
```

### 5.2 SignalR Hub Invocations & Events

- **Client Invocation:** `SendMessage(string sender, string framework, string message)`
- **Client Broadcast Listener:** `ReceiveMessage(string sender, string framework, string message)`

---

## 6. Repository & Project Structure

```
Trifold-Chatterbox/
├── Directory.Build.props
├── Directory.Packages.props
├── Trifold-Chatterbox.sln
├── src/
│   ├── Trifold-Chatterbox.AppHost/              # Aspire Orchestrator
│   │   ├── Program.cs
│   │   └── Trifold-Chatterbox.AppHost.csproj
│   ├── Trifold-Chatterbox.ServiceDefaults/     # OpenTelemetry, Health Probes, Metrics
│   │   ├── Extensions.cs
│   │   └── Trifold-Chatterbox.ServiceDefaults.csproj
│   ├── Trifold-Chatterbox.Contracts/           # Protobuf contracts & Grpc.Tools generated stubs
│   │   ├── Protos/
│   │   │   └── chat_history.proto
│   │   └── Trifold-Chatterbox.Contracts.csproj
│   ├── HubServer/                           # Combined SignalR & gRPC Service
│   │   ├── Hubs/
│   │   │   └── ChatHub.cs
│   │   ├── Services/
│   │   │   └── HistoryServiceImpl.cs
│   │   ├── Program.cs
│   │   └── HubServer.csproj
│   ├── Client.Spectre/                      # Spectre.Console Client
│   │   ├── Program.cs
│   │   └── Client.Spectre.csproj
│   ├── Client.TerminalGui/                  # Terminal.Gui Client
│   │   ├── Program.cs
│   │   └── Client.TerminalGui.csproj
│   └── Client.Jumbee/                       # Jumbee.Console Client
│       ├── Program.cs
│       └── Client.Jumbee.csproj
├── repl/
│   └── chat-session.csx                    # Dotnet-Repl Interactive Script
└── tests/
    └── Trifold-Chatterbox.IntegrationTests/    # Testcontainers + XUnit
        ├── HubIntegrationTests.cs
        └── Trifold-Chatterbox.IntegrationTests.csproj
```

---

## 7. Implementation Breakdown (Learning Milestones)

### Milestone 1: SignalR + Aspire, End-to-End Hello World

*Goal: a working, observable live chat between one server and one client.*

- Configure `Directory.Packages.props` for Central Package Management (CPM) pinning the initial package set: `Aspire.Hosting.AppHost`, `Microsoft.Extensions.ServiceDiscovery`, `Microsoft.AspNetCore.SignalR.Client`, `Spectre.Console`.
- Create `HubServer` with `builder.Services.AddSignalR()` and `app.MapHub<ChatHub>("/chat")` only — no gRPC yet.
- Create `Client.Spectre` with `HubConnectionBuilder.WithUrl(hubUrl).WithAutomaticReconnect().Build()`, bound to `ReceiveMessage`, rendering via `AnsiConsole.Live(table)`.
- Create `Trifold-Chatterbox.AppHost` and `Trifold-Chatterbox.ServiceDefaults`; wire HubServer and Client.Spectre into `DistributedApplication.CreateBuilder`, with the client referencing the hub via `.WithReference(hub)`.
- **Learning focus:** SignalR hub lifecycle, connection negotiation, group broadcast; Aspire project wiring and dashboard basics.

### Milestone 2: Add gRPC History Streaming

*Goal: understand gRPC server streaming and see it alongside SignalR in the same dashboard.*

- Create `Trifold-Chatterbox.Contracts` and configure `chat_history.proto` with `<Protobuf Include="Protos\chat_history.proto"/>`.
- Add `Grpc.AspNetCore` and `Grpc.Net.Client` to CPM.
- Implement `HistoryServiceImpl` backed by an in-memory bounded `ConcurrentQueue<HistoryMessageResponse>`; register with `builder.Services.AddGrpc()` and `app.MapGrpcService<HistoryServiceImpl>()`.
- Update `Client.Spectre` to open a `GrpcChannel.ForAddress(hubUrl)`, call `GetRecentMessages()` on startup, and populate the render buffer with history before subscribing to live SignalR events.
- **Learning focus:** Protobuf contract-first design, HTTP/2 server streaming, protocol coexistence on a single Kestrel instance, verifying both protocols in the Aspire trace view.

### Milestone 3: Add Terminal.Gui Client

*Goal: learn a second, structurally different TUI paradigm against an already-working backend.*

- Add `Terminal.Gui` to CPM; create `Client.TerminalGui` following the same bootstrap sequence as Milestone 1–2 (gRPC history replay, then SignalR live subscription).
- Implement window layout with `ListView` and input controls; marshal SignalR callbacks onto the UI thread via `Application.Invoke(...)`.
- Add the project to the AppHost with `.WithReference(hub)`.
- **Learning focus:** object-oriented widget tree model, UI-thread marshaling from background network callbacks — a different concern than Spectre's live-render-loop model.

### Milestone 4: Add Jumbee.Console Client

*Goal: learn a third TUI paradigm — direct ANSI diff-buffer rendering.*

- Confirm Jumbee.Console compatibility with the target TFM (see §4.2 note) before starting.
- Create `Client.Jumbee` following the same bootstrap sequence; implement dirty-region tracking and `screen.RequestRender()`-driven redraws.
- Add the project to the AppHost.
- **Learning focus:** manual buffer diffing vs. the higher-level abstractions in Spectre and Terminal.Gui — this is the most "close to the metal" of the three clients.

### Milestone 5 (Stretch): REPL Tooling

- Configure `repl/chat-session.csx` with `#r` NuGet bindings for rapid manual testing and assertion injection against the running HubServer.
- **Learning focus:** dotnet-repl scripting workflow. Verify the tool is still actively maintained before investing time here.

### Milestone 6 (Stretch): Hermetic Integration Testing

- Add `Testcontainers` to CPM.
- Build an integration test suite (`Trifold-Chatterbox.IntegrationTests`) that spins up HubServer in a container and verifies multi-client message delivery and gRPC stream completion under load via `dotnet test`.
- **Learning focus:** hermetic test design, container lifecycle management from test code.

---

## 8. Verification & Acceptance Criteria

### Per-Milestone

- [ ] **M1:** AppHost launches HubServer + Client.Spectre cleanly; a message sent from Client.Spectre is received back via `ReceiveMessage`; Aspire dashboard shows a SignalR connection trace.
- [ ] **M2:** A newly launched Client.Spectre streams and displays prior history via gRPC before receiving any live messages; Aspire dashboard shows both HTTP/2 gRPC and WebSocket traffic on the same HubServer instance.
- [ ] **M3:** Client.TerminalGui joins the same session; messages sent from either client appear in both in real time.
- [ ] **M4:** Client.Jumbee joins the same session with the same parity guarantee as M3.

### Final (All Milestones)

- [ ] **Aspire Dashboard:** AppHost launches cleanly; all projects register and report OpenTelemetry traces and health metrics.
- [ ] **Protocol Coexistence:** Single Kestrel instance handles simultaneous HTTP/2 gRPC streaming and HTTP/1.1 WebSockets on port 5000.
- [ ] **TUI Client Parity:** Messages entered in any of the three TUI clients (or via dotnet-repl, if built) are fanned out and rendered correctly across all active terminals in real time.
- [ ] **Historical Replay:** New clients receive past messages streamed via gRPC before joining the live broadcast feed.
- [ ] **Hermetic Testing (if built):** Integration tests run cleanly via `dotnet test` using Testcontainers without needing pre-existing external servers.