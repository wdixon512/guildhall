---
id: multi-instance-dev-orchestration
title: "Multi-Instance Dev Session Orchestration"
area: host-security-lifecycle
triggers:
  - Start-Dev.ps1
  - Get-DevStatus.ps1
  - Stop-Dev.ps1
  - New-ChildEnvironment
  - Get-VerifiedGuildhallPortListeners
  - GUILDHALL_INSTANCE_TAG
primary_files:
  - scripts/startup/Start-Dev.ps1
  - scripts/startup/Get-DevStatus.ps1
  - scripts/startup/Stop-Dev.ps1
  - scripts/misc/Common.ps1
verified_commit: "51bca7eb5076022dcd1ff89d460d683c1e1e3848"
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - "docs/masterplan/phase 0/phase-0.md"
  - "docs/masterplan/phase 6/phase-6.md"
uncertainties:
  - boundary: "Local dev session manifests"
    location: "scripts/startup/Start-Dev.ps1::$sharedManifestPath"
    note: "Active worktree session manifests are written at runtime under %LOCALAPPDATA%\\Guildhall\\dev\\sessions\\*.json outside git."
    static_or_runtime: "runtime"
---

# Multi-Instance Dev Session Orchestration

Launches, tracks, and stops concurrent Guildhall dev sessions across worktrees using dynamic loopback ports, window/header instance tags, scrubbed child environments, and shared session manifests.

## Execution Spine

| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Ops / Scripts | `scripts/startup/Start-Dev.ps1` | `Stop-RegisteredDevSession` | `~L40-L64` | `-Replace`, `-AutoPort`, `-HostPort`, `-FrontendPort`, `-WindowTag` -> `Assert-LocalPortFree` | `-Replace` terminates only sessions matching current checkout or target ports plus verified Guildhall listeners; `-AutoPort` selects free loopback ports via `Get-FreeLocalPort` |
| 2 | Ops / Scripts | `scripts/misc/Common.ps1` | `Get-InstanceWindowMetadata` | `~L105-L139` | `tauri.conf.json` version + git branch/worktree + ports -> `WindowTitle`, `InstanceTag` | Formats `GUILDHALL_WINDOW_TITLE` and `GUILDHALL_INSTANCE_TAG` with version, branch/worktree, optional `-WindowTag`, and ports so concurrent windows remain distinguishable |
| 3 | Ops / Scripts | `scripts/misc/Common.ps1` | `New-ChildEnvironment` | `~L312-L353` | Allowlisted OS/toolchain env hashtable -> `Start-OwnedChild` `ProcessStartInfo.Environment` | `Start-OwnedChild` clears inherited `ProcessStartInfo.Environment` and copies only `New-ChildEnvironment` allowlisted keys, preventing accidental parent API key or SQL credential leakage |
| 4 | Ops / Scripts | `scripts/startup/Start-Dev.ps1` | `$sharedManifestPath` | `~L66-L196` | Random 32-byte token + DPAPI SQL conn -> host, clean Vite, desktop; `session.json` + `%LOCALAPPDATA%\Guildhall\dev\sessions\<id>.json` | Vite receives `New-ChildEnvironment` without `GUILDHALL_SESSION_TOKEN` or SQL credentials; locked `guildhall-desktop.exe` is copied per session; manifests exclude secrets and `finally` block cleans children/files |
| 5 | Ops / Scripts | `scripts/startup/Get-DevStatus.ps1` | `Get-RegisteredDevSessions` | `~L7-L50` | `%LOCALAPPDATA%\Guildhall\dev\sessions\*.json` + `Get-SourceFingerprint` + binary SHA-256 | Reports host/SQL snapshot freshness (<15s), working tree cleanliness, source fingerprint drift, and live port listeners across all worktrees |
| 6 | Ops / Scripts | `scripts/startup/Stop-Dev.ps1` | `Stop-RegisteredDevSession` | `~L11-L50` | `-SessionId`, `-Checkout`, `-Port`, `-All` -> `Stop-RegisteredDevSession` + `Stop-VerifiedGuildhallPortListeners` | Stops processes in reverse order (`desktopPid` -> `frontendPid` -> `hostLauncherPid` -> `launcherPid`) verifying allowed executable names before killing and cleaning manifests |
| 7 | Ops / Scripts | `scripts/misc/Common.ps1` | `Get-VerifiedGuildhallPortListeners` | `~L241-L298` | `Get-NetTCPConnection` Listen + `Win32_Process` `CommandLine` -> `IsGuildhall` ownership check | Identifies Guildhall listeners by registered session, checkout path, binary name, or Vite/AgentHost command line; non-Guildhall listeners are reported and left untouched |

## Contracts & Guardrails

- Skills: [codebase-index](../../skills/codebase-index/SKILL.md), [work-work](../../skills/work-work/SKILL.md)
- Standards: [coding-standards.md](../../../docs/coding-standards.md)
- Masterplan: [phase-0.md](<../../../docs/masterplan/phase 0/phase-0.md>), [phase-6.md](<../../../docs/masterplan/phase 6/phase-6.md>)

## Targeted Verification

- `pwsh -NoProfile -File ./scripts/startup/Get-DevStatus.ps1 -Json`
- `pwsh -NoProfile -File ./scripts/startup/Start-Dev.ps1 -HostOnly -AutoPort -RunForSeconds 2`
