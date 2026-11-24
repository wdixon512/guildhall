---
id: council-member-lifecycle
title: "Council Member Lifecycle & Profile Editor"
area: council-memory-context
triggers:
  - "/agents"
  - "/agents/{id}/lifecycle"
  - "CouncilSidebar"
  - "CouncilMember"
  - "AgentDefinition"
  - "saveMemberProfile"
  - "setGuildMemberArchived"
  - "deleteGuildMember"
primary_files:
  - apps/desktop/src/CouncilSidebar.tsx
  - apps/desktop/src/CouncilMember.tsx
  - apps/desktop/src/App.tsx
  - apps/desktop/src/ConfirmationDialogs.tsx
  - services/agent-host/Api/ChatEndpoints.cs
  - services/agent-host/Persistence/GuildhallDbContext.cs
verified_commit: 51bca7eb5076022dcd1ff89d460d683c1e1e3848
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - "docs/masterplan/phase 1/council-profiles-and-memory.md"
  - "docs/masterplan/phase 1/council-member-archive-migration-approval.md"
  - .agents/roster.md
uncertainties: []
---

# Council Member Lifecycle & Profile Editor

Create, edit, archive, restore, and delete persistent council member profiles with dirty-draft protection, bounded portraits, Markdown import/export, and MCP tool permission policies.

## Execution Spine

| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Desktop / UI | `apps/desktop/src/CouncilSidebar.tsx` | `renderMember` | `~L232-L414` | `collapsedMembers`, `memberMenuId` portal menu, `onAddGuildMember`, `onNewSession`, `onRequestArchiveMember`, `onRequestDeleteMember`, `onRestoreMember` | New sessions require an active non-archived member; member archive and delete menu items are disabled while `profileHasPendingChanges` or `chatBusy` is true. |
| 2 | Desktop / UI | `apps/desktop/src/CouncilMember.tsx` | `CouncilMember` | `~L89-L327` | `upload()` 512x512 JPEG <=350KB data URI, `importMarkdown()`/`exportMarkdown()`, `GET /mcp/servers/custom`, `onUpdateToolPolicy` | `confirmUnrestricted` requires explicit Danger confirmation before switching `permissionPolicy` to unrestricted; portrait uploads cancel stale async decodes via `uploadVersion`; Markdown import strips frontmatter and caps files at 64 KB / 8,192 chars. |
| 3 | Desktop / UI | `apps/desktop/src/App.tsx` | `saveMemberProfile` | `~L402-L509` | `profileDraftsRef`, `profileToolDraftsRef`, `PATCH /agents/{id}` (`expectedRevision`), `discardMemberProfile` | Per-member dirty draft protection (`profileDirty`, `profileHasPendingChanges`) preserves unsaved edits across switches and blocks archive/delete until saved or discarded. |
| 4 | Desktop / UI | `apps/desktop/src/ConfirmationDialogs.tsx` | `ConfirmationDialogs` | `~L42-L207` | `deleteMemberTarget`, `archiveMemberTarget` -> `onConfirmDeleteMember` / `onConfirmArchiveMember` | Archiving a member keeps all saved sessions under Archived members; permanent delete warns that sessions, messages, and attachments are removed and requires active replies to finish. |
| 5 | Agent Host | `services/agent-host/Api/ChatEndpoints.cs` | `ValidateProfile` | `~L283-L318` | `GET /agents`, `POST /agents`, `PATCH /agents/{id:guid}` (`AgentProfileResponse`, `expectedRevision`; helper `ValidateProfile` at L588-L599) | `LocalApiBoundary` bearer auth; `ValidateProfile` (L588-L599) enforces name/role 1-40 chars, portrait 0-3, JPEG/PNG/WebP <=350,000 chars, allowed accent hex, and instructions <=8,192 chars; `PATCH` enforces `Revision` OCC. |
| 6 | Agent Host / SQL | `services/agent-host/Api/ChatEndpoints.cs` | `GuildMemberLifecycleRequest` | `~L319-L365` | `DELETE /agents/{id:guid}?expectedRevision=`, `PATCH /agents/{id:guid}/lifecycle`, `ChatRunner.MutationGate` | `MutationGate` + `Serializable` transaction; rejects with `409 member_has_active_run` if any `ChatRun` is `Running`, `Cancelling`, or `Queued`; built-in `AssistantId` cannot be deleted without a saved session. |
| 7 | SQL / EF | `services/agent-host/Persistence/GuildhallDbContext.cs` | `AgentDefinition` | `~L352-L360` | `dbo.AgentDefinitions` (`IsArchived`, `EnabledToolIdsJson`, `PermissionPolicy`, `Revision` concurrency token), `dbo.Conversations` (`DeleteBehavior.Restrict`) | `AgentDefinition.Revision` is an EF concurrency token; `Conversations` uses `DeleteBehavior.Restrict` so `DELETE /agents/{id}` explicitly executes `ExecuteDeleteAsync` on conversations before removing the profile while `guildhall_runtime` stays DDL-free. |

## Contracts & Guardrails

- Follow [coding-standards.md](../../../docs/coding-standards.md) for `.NET 10` / `EF Core 10` concurrency tokens, `Serializable` lifecycle transactions, and DDL-free `guildhall_runtime` access.
- Enforce member profile validation, portrait/Markdown bounds, archive vs. permanent delete semantics, and MCP policy gates per [council-profiles-and-memory.md](<../../../docs/masterplan/phase 1/council-profiles-and-memory.md>) and [council-member-archive-migration-approval.md](<../../../docs/masterplan/phase 1/council-member-archive-migration-approval.md>).
- Coordinate index refreshes and worktree workflows via [codebase-index](../../skills/codebase-index/SKILL.md), [work-work](../../skills/work-work/SKILL.md), and [roster.md](../../../.agents/roster.md).

## Targeted Verification

- `npm --prefix apps/desktop run test -- --run src/CouncilMember.test.tsx src/App.test.tsx`
- `dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj --filter "FullyQualifiedName~CouncilMemberArchiveSqlTests"`
