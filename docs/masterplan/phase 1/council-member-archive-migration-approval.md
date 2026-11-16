# Council member archive migration — applied

Reviewed and applied November 15, 2026 (America/New_York), after explicit owner approval.

Target: **localhost / GuildhallDev**, using the existing Windows migration identity. The required pre-upgrade checkpoint was `20261116012111_CouncilProfiles`; the applied target is `20261116021040_CouncilMemberArchive`.

The migration adds `dbo.AgentDefinitions.IsArchived` as a non-null SQL `bit`, default `0`. Existing profiles start unarchived. It records the migration in `dbo.__EFMigrationsHistory` inside the same transaction. It does not archive any member automatically, delete or rewrite profiles/history, change foreign keys, grant permissions, create accounts, or alter SQL Server configuration. Existing runtime table-specific SELECT/UPDATE permissions cover the column.

Generated forward SQL:

```sql
BEGIN TRANSACTION;
ALTER TABLE [AgentDefinitions] ADD [IsArchived] bit NOT NULL DEFAULT CAST(0 AS bit);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261116021040_CouncilMemberArchive', N'10.0.15');

COMMIT;
GO
```

The existing guarded command applies **all pending migrations**, so recheck the source migration list and database checkpoint immediately before execution; stop if anything beyond this reviewed migration is pending. Approval covers this migration only.

```powershell
pwsh -NoProfile -File G:\repos\guildhall\scripts\database\Migrate-Database.ps1 -DatabaseName GuildhallDev -Execute
```

## Completed backend verification

- `dotnet test tests/Guildhall.AgentHost.Tests -c Release --filter 'Category=SqlIntegration'`: 2 passed, 0 skipped, using uniquely owned `GuildhallTest_*` databases on localhost under the approved Windows identity. Tests clean up their own databases. No real model calls.
- `dotnet test tests/Guildhall.AgentHost.Tests -c Release --no-build --filter 'Category!=SqlIntegration'`: 33 passed, 0 skipped.
- The new archive-upgrade test starts at CouncilProfiles with a populated member/session/run/message/attachment fixture; compares every pre-existing application-table column before and after migration; verifies default false, repeat migration, readiness transition, and runtime archive/restore with denied DDL and direct dependent-row deletes.
- Existing SQL-backed HTTP API checks cover active/cancelling/queued archive refusal, revision conflicts, idempotent archival, new-session rejection, retained history/provenance, independent members, and restored session creation.
- Read-only GuildhallDev preflight: 8 profiles, 5 sessions, 6 runs, 12 messages, 0 attachments, 1 installation metadata row; no active/queued work. These are current observations, not a replacement for the earlier seven-session profile-migration record. The archive column is absent.
- Read-only checks through the actual runtime login: profile SELECT/UPDATE allowed; profile DELETE, database ALTER, db_owner and db_datawriter denied/absent.

UI/native interaction review belongs to the separate frontend session. This backend record does not certify UI rendering or startup timing.

## Application result

The original command failed before migration execution because the running Debug host locked its executable during build. The wrapper now uses separate Release output and previews SQL by default; `-Execute` applies the displayed pending range through a pinned target. Preview mode was run successfully while CouncilMemberArchive remained pending, then execution applied only that migration. A subsequent default invocation reported no pending migrations.

Read-only verification through the actual runtime login confirmed the latest migration is CouncilMemberArchive, 8 profiles with zero archived, 5 sessions, 6 runs, 12 messages, 0 attachments, and 1 metadata row. Counts match the preflight. The existing launcher's authenticated readiness snapshot changed to SQL ready. The existing desktop/host PIDs remained running; neither was restarted. No user profile or conversation was mutated for functional testing. Exact data comparison was performed on isolated fixtures; live GuildhallDev verification used counts rather than private-content hashes.

Owner functional QC after successful application: use the existing window's Refresh/Connections control to confirm SQL Ready, open a retained session, then exercise member archive/restore once the frontend session confirms its UI is ready. The backend agent runs automated tests; the owner does not need to run them.

Independent UI review used `.cursor/agents/guildhall-ui-reviewer.md`: no blocking findings after inspecting wide/narrow renders, confirmation and rejection screens. The reviewer independently checked Tab dismissal. A minor recovery-copy finding was fixed: wait for queued/running replies before retrying; reopen only for a member changed elsewhere. Native WebView rendering remains unverified for this follow-up until the approved upgrade.

Exact automated commands run (agent work; owner QC is the functional checklist above):

```powershell
dotnet build Guildhall.slnx --no-restore -c Release
dotnet test tests/Guildhall.AgentHost.Tests -c Release --no-build --filter 'Category!=SqlIntegration'
$env:GUILDHALL_TEST_SQL_CONNECTION = 'Server=localhost;Database=master;Integrated Security=true;Encrypt=true;TrustServerCertificate=true'
dotnet test tests/Guildhall.AgentHost.Tests -c Release --no-build --filter 'Category=SqlIntegration'
npm.cmd --prefix apps/desktop run build
npm.cmd --prefix apps/desktop run test -- --run
cargo test --locked --manifest-path apps/desktop/src-tauri/Cargo.toml
dotnet ef migrations has-pending-model-changes --project services/agent-host --configuration Release --no-build
```

## Approval follow-up verification

Following the owner's approval in the frontend chat, its preflight already found `20261116021040_CouncilMemberArchive` applied. `Apply-CouncilMemberArchive.ps1 -Execute` returned the guarded already-applied/no-change result. No duplicate upgrade, desktop restart, or live archive/session mutation was performed in this verification.

Read-only checks confirmed `IsArchived` is non-null `bit` with default false, all 8 profiles are active, and current counts remain 5 sessions, 6 runs, 12 messages, 0 attachments and 1 installation metadata row. There are no running/queued replies. The actual runtime identity reads the new column and retains profile UPDATE; profile DELETE, database ALTER, db_owner and db_datawriter remain denied/absent. The existing development launcher's authenticated snapshot reports host ready and SQL ready. This turn did not capture a pre-migration data hash and does not claim a live before/after checksum comparison.

Native member lifecycle interaction has not been exercised against GuildhallDev in this follow-up. The browser fixture and independent rendered UI review recorded above remain the interaction evidence. Keep the existing window open for owner functional QC:

```powershell
pwsh -NoProfile -File G:\repos\guildhall\scripts\startup\Get-DevStatus.ps1
```

1. Right-click a member heading or use **Member actions for …**. Choose **Archive member**, check that the confirmation says saved sessions are kept, then Cancel once.
2. Confirm archive. Expand **Archived members**, open the member's saved session and confirm its history. Selecting that member disables **New session**.
3. Choose **Restore member** from its actions. Confirm it returns to the active council and can start new sessions. Use Tab/Enter/Escape for the same controls and check a narrow window.
