# Trifold-Chatterbox

A distributed real-time communication sandbox designed to explore, benchmark, and compare terminal user interface (TUI) architectures while implementing production-grade .NET cloud-native patterns.

## Initialize Tools

Mise is used for controlling versions of our tooling.

```pwsh
mise install
```

## Agents Folder Setup

```pwsh
# 1. Create AGENTS.md
New-Item -ItemType File -Name "AGENTS.md"
# 2. Create the Symbolic Link (LinkName -> Target)
New-Item -ItemType SymbolicLink -Path "CLAUDE.md" -Target "AGENTS.md"
```
