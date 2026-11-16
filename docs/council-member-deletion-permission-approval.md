# Council member deletion permission — approved and applied

Status: explicitly approved by the owner and applied to localhost / GuildhallDev on November 16, 2026 (America/New_York). Scope was limited to the reviewed AgentDefinitions DELETE grant.

## Proposed change

Target: **localhost / GuildhallDev**, using the existing Windows migration identity. The expected current migration is `20261116021040_CouncilMemberArchive`; the proposed target is `20261116134707_CouncilMemberDeletionPermission`.

The migration grants the existing `guildhall_runtime` database principal `DELETE` on `dbo.AgentDefinitions` only. The API deletes that member's `Conversations` rows in the same serializable transaction before deleting the profile. Existing SQL foreign-key cascades remove that member's runs, messages, and attachments. A prior approved lifecycle migration already grants `DELETE` on `dbo.Conversations`; this proposal preserves that existing permission. The new migration grants no direct delete on ChatRuns, ChatMessages, or ChatAttachments and no schema or server permissions.

The migration SQL is:

```sql
IF DATABASE_PRINCIPAL_ID(N'guildhall_runtime') IS NOT NULL
    GRANT DELETE ON OBJECT::dbo.AgentDefinitions TO [guildhall_runtime];
```

Its `Down` operation revokes that table permission. This permission change does not itself delete or rewrite any data. The API deletion is permanent and removes all sessions belonging to the selected member after the frontend asks the user to confirm. It refuses stale profile revisions and any queued, running, or cancelling linked run. The built-in seed assistant is protected while it has no sessions.

## Apply command — requires separate explicit approval

The guarded command below checks that GuildhallDev exists and that its current migration is exactly the expected prior migration. Without `-Execute`, it performs only that read-only preflight and prints the planned scope. Do not pass `-Execute` until this specific change has been approved.

```powershell
pwsh -NoProfile -File G:\repos\guildhall-frontend\scripts\Apply-CouncilMemberDeletionPermission.ps1
```

After explicit approval, the execute form is:

```powershell
pwsh -NoProfile -File G:\repos\guildhall-frontend\scripts\Apply-CouncilMemberDeletionPermission.ps1 -Execute
```

The script is intentionally not run as part of preparing this change.

## Verification plan

Before a GuildhallDev application is considered, run Release backend tests against uniquely owned `GuildhallTest_*` databases on localhost. The SQL checks exercise the additive grant, confirm direct deletes of dependent rows remain denied, verify API active/stale/missing guards, and verify that deletion cascades only through the selected member while unrelated members and sessions remain.

After separate approval and application, use read-only SQL permission checks through the actual runtime login to confirm `DELETE` on AgentDefinitions, preserve the existing `DELETE` on Conversations, and confirm no direct `DELETE` on ChatRuns, ChatMessages, or ChatAttachments. Do not test permanent deletion against GuildhallDev.

## Completed preparation checks

Release build (zero warnings/errors), 33 deterministic tests, and 2 isolated SQL integration tests passed. EF generated SQL and pending-model checks passed; PowerShell parsing passed. The guarded preview was run successfully from `G:/repos/guildhall-frontend`; it generated ignored review SQL and applied no changes to GuildhallDev. UI and responsive fixture results plus owner functional commands are in [frontend-polish-plan.md](frontend-polish-plan.md).

## Applied verification — November 16, 2026

Ran `Apply-CouncilMemberDeletionPermission.ps1 -Execute` from the frontend worktree. EF applied only `20261116134707_CouncilMemberDeletionPermission`. Verified the latest checkpoint through the actual restricted runtime identity. Runtime DELETE is allowed on AgentDefinitions and Conversations; direct DELETE on ChatRuns, ChatMessages and ChatAttachments, database ALTER, db_owner and db_datawriter remain denied/absent.

Before/after SHA-256 hashes and row counts match for all six application tables, including installation metadata, profiles, sessions, runs, messages and attachments. Current counts: 1 metadata row, 9 profiles, 8 sessions, 16 runs, 32 messages and 1 attachment. The migration history entry is the expected additional system record. No member/session deletion or other functional fixture was run against GuildhallDev. No application processes were restarted. Native/live-host UI QC and branch integration remain separate.
