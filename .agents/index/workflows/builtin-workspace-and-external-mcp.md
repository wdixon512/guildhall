---
id: builtin-workspace-and-external-mcp
title: "Built-In Workspace & External SaaS MCP Providers"
area: mcp-tools-security-gate
triggers:
  - WorkspacePathGuard
  - filesystem-mcp
  - git-mcp
  - workspace-exec-mcp
  - github-mcp
  - google-drive-mcp
  - gmail-mcp
primary_files:
  - services/agent-host/Security/WorkspacePathGuard.cs
  - services/agent-host/Mcp/Providers/LocalFilesystemMcpProvider.cs
  - services/agent-host/Mcp/Providers/LocalGitMcpProvider.cs
  - services/agent-host/Mcp/Providers/LocalCommandMcpProvider.cs
  - services/agent-host/Mcp/Providers/GitHubMcpProvider.cs
  - services/agent-host/Mcp/Providers/GoogleDriveMcpProvider.cs
  - services/agent-host/Mcp/Providers/GmailMcpProvider.cs
verified_commit: 51bca7eb5076022dcd1ff89d460d683c1e1e3848
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - "docs/masterplan/phase 3/phase-3.md"
  - "docs/masterplan/phase 3/phase-3-contracts.md"
  - "docs/masterplan/phase 3/verification.md"
uncertainties:
  - boundary: "External provider DPAPI credentials"
    location: "services/agent-host/Configuration/DpapiCredentialStore.cs::DpapiCredentialStore"
    note: "Encrypted *.dpapi credential files for github-mcp, google-drive-mcp, and gmail-mcp live per Windows user under %LOCALAPPDATA%\\Guildhall\\credentials outside source control."
    static_or_runtime: "runtime"
---

# Built-In Workspace & External SaaS MCP Providers

Enforces workspace path containment across built-in filesystem, git, and command providers and tier-separates GitHub, Google Drive, and Gmail integrations.

## Execution Spine

| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Security | `services/agent-host/Security/WorkspacePathGuard.cs` | `WorkspacePathGuard.ValidateWritePath` | `~L37-L175` | `PathValidationResult (ValidateReadPath / ValidateWritePath / IsSensitivePath)` | Rejects null bytes, UNC paths, workspace escapes, reparse-point symlinks/junctions, `.git` writes, and sensitive credential/key files |
| 2 | MCP | `services/agent-host/Mcp/Providers/LocalFilesystemMcpProvider.cs` | `LocalFilesystemMcpProvider.InvokeAsync` | `~L18-L71` | `filesystem-mcp (Tier 1 list/read/search; Tier 2 fs_write_file/fs_edit_file/fs_delete_file)` | Validates every path via `WorkspacePathGuard`, skips `.git`/`node_modules`/`bin`/`obj` during search, and caps reads at 64 KiB |
| 3 | MCP | `services/agent-host/Mcp/Providers/LocalGitMcpProvider.cs` | `LocalGitMcpProvider.InvokeAsync` | `~L36-L124` | `git-mcp (Tier 1 git_status / git_log / git_diff / git_show)` | Checks workspace root with `WorkspacePathGuard`, scrubs child env via `EnvironmentScrubber`, rejects leading-dash args, caps stdout at 32 KiB |
| 4 | MCP | `services/agent-host/Mcp/Providers/LocalCommandMcpProvider.cs` | `LocalCommandMcpProvider.InvokeAsync` | `~L15-L149` | `workspace-exec-mcp (Tier 2 workspace_run_command, CommandTimeout=60s)` | Allows only 10 binaries (no paths/shell), validates `subPath`, scrubs env, kills at 60s timeout, redacts output via `ExfiltrationGuard` |
| 5 | MCP | `services/agent-host/Mcp/Providers/GitHubMcpProvider.cs` | `GitHubMcpProvider.InvokeAsync` | `~L13-L119` | `github-mcp (Tier 1 reads / Tier 3 github_create_issue & github_create_issue_comment)` | Reads DPAPI token via `ICredentialStore`, URI-escapes owner/repo, keeps issue and comment mutations at Tier 3 HITL |
| 6 | MCP | `services/agent-host/Mcp/Providers/GoogleDriveMcpProvider.cs` | `GoogleDriveMcpProvider.InvokeAsync` | `~L14-L108` | `google-drive-mcp (Tier 1 gdrive_search_files / gdrive_read_text_file)` | Uses DPAPI OAuth token from `ICredentialStore`, exports Google Docs as `text/plain`, caps downloaded text at 64 KiB |
| 7 | MCP | `services/agent-host/Mcp/Providers/GmailMcpProvider.cs` | `GmailMcpProvider.InvokeAsync` | `~L13-L111` | `gmail-mcp (Tier 1 search/get / Tier 3 gmail_create_draft)` | Uses DPAPI OAuth token from `ICredentialStore`; `gmail_create_draft` is Tier 3 HITL and creates drafts only without auto-sending |

## Contracts & Guardrails

- Follow [codebase-index](../../skills/codebase-index/SKILL.md), [work-work](../../skills/work-work/SKILL.md), and [coding-standards.md](../../../docs/coding-standards.md).
- Built-in provider tiers, workspace path guards, and SaaS boundaries: [phase-3.md](<../../../docs/masterplan/phase 3/phase-3.md>), [phase-3-contracts.md](<../../../docs/masterplan/phase 3/phase-3-contracts.md>), and [verification.md](<../../../docs/masterplan/phase 3/verification.md>).

## Targeted Verification

- `dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj --filter "FullyQualifiedName~WorkspacePathGuardTests|FullyQualifiedName~McpLocalProvidersTests|FullyQualifiedName~McpExternalProvidersTests"`
