---
id: detached-background-host
title: "Detached Background Host & Attach Handoff"
area: host-security-lifecycle
triggers:
  - Start-BackgroundHost.ps1
  - AttachBackground
  - background-host.json
  - GuildhallAgentHostBackground
  - RegisterLogonTask
primary_files:
  - scripts/startup/Start-BackgroundHost.ps1
  - scripts/startup/Start-Dev.ps1
  - scripts/misc/Common.ps1
  - services/agent-host/Program.cs
verified_commit: "51bca7eb5076022dcd1ff89d460d683c1e1e3848"
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - "docs/masterplan/phase 6/phase-6.md"
uncertainties:
  - boundary: "Detached host manifest & logon task"
    location: "scripts/startup/Start-BackgroundHost.ps1::$bgManifestPath"
    note: "Detached manifest (%LOCALAPPDATA%\\Guildhall\\dev\\background-host.json with DPAPI-encrypted token) and Task Scheduler registration (GuildhallAgentHostBackground) exist only in local Windows user state."
    static_or_runtime: "runtime"
---

# Detached Background Host & Attach Handoff

Runs `Guildhall.AgentHost` detached for background scheduled automations using a DPAPI-protected token manifest, optional Windows Task Scheduler logon registration, and `Start-Dev.ps1 -AttachBackground` reuse.

## Execution Spine

| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Ops / Scripts | `scripts/startup/Start-BackgroundHost.ps1` | `Register-ScheduledTask` | `~L23-L38` | `-RegisterLogonTask` / `-UnregisterLogonTask` -> Task Scheduler `GuildhallAgentHostBackground` | Registers an `AtLogOn` `pwsh.exe` hidden task with `-SkipMigrations` and unlimited `ExecutionTimeLimit` so background schedules survive desktop closure without prompting |
| 2 | Ops / Scripts | `scripts/startup/Start-BackgroundHost.ps1` | `Protect-BgToken` | `~L40-L82` | `ProtectedData` `Protect`/`Unprotect` (`CurrentUser`) -> `-Status` `/health` probe and `-Stop` cleanup | Encrypts the bearer token per Windows user via DPAPI (`DataProtectionScope.CurrentUser`) and never writes plaintext tokens into `background-host.json` |
| 3 | Ops / Scripts | `scripts/startup/Start-BackgroundHost.ps1` | `Start-OwnedChild` | `~L84-L133` | `Assert-LocalPortFree` + `Get-LocalSqlConnection -Runtime` + `Invoke-AutoDatabaseMigration` -> `dotnet` AgentHost | No-ops if an existing manifest PID is still alive; starts `Guildhall.AgentHost` under `New-ChildEnvironment` and kills the child if `/health` fails within 15 seconds |
| 4 | Ops / Scripts | `scripts/startup/Start-BackgroundHost.ps1` | `$bgManifestPath` | `~L135-L147` | Writes `%LOCALAPPDATA%\Guildhall\dev\background-host.json` (`pid`, `hostUrl`, `hostPort`, `checkout`, `protectedToken`) | Clears in-memory `$token` and `$runtimeConnection` immediately after persisting the DPAPI-protected manifest |
| 5 | Ops / Scripts | `scripts/startup/Start-Dev.ps1` | `AttachBackground` | `~L18-L40` | `Start-Dev.ps1 -AttachBackground` -> reads `background-host.json`, unprotects DPAPI token, probes `/health` | Reuses the detached host port and unprotected token when PID and `/health` succeed; gracefully warns and falls back to launching an owned host on any error |
| 6 | Agent Host | `services/agent-host/Program.cs` | `DurableTaskSchedulerService` | `~L99-L112` | `AddHostedService(DurableTaskSchedulerService)` + `UseLocalBoundary(hostUri, token)` | Detached host enforces identical `LocalApiBoundary` token/loopback guards while running durable scheduled automations independently of any Tauri window |

## Contracts & Guardrails

- Skills: [codebase-index](../../skills/codebase-index/SKILL.md), [work-work](../../skills/work-work/SKILL.md)
- Standards: [coding-standards.md](../../../docs/coding-standards.md)
- Masterplan: [phase-6.md](<../../../docs/masterplan/phase 6/phase-6.md>)

## Targeted Verification

- `pwsh -NoProfile -File ./scripts/startup/Start-BackgroundHost.ps1 -Status`
