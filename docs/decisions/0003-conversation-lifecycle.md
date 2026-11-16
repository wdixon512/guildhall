# Decision 0003 — Conversation lifecycle

Status: approved and applied to `localhost` / `GuildhallDev`, November 15, 2026.

## Product behavior

- Conversations can be archived and restored from the sidebar.
- Permanent delete requires a confirmation dialog. The dialog says the operation cannot be undone in Guildhall and that existing SQL backups may retain copies.
- Archive and delete are refused while the conversation has a Running or Cancelling response. The composer now exposes Stop response, and startup reconciliation marks runs from a previous host Interrupted while preserving saved text. The full streaming/recovery acceptance gate remains unfinished.
- Existing conversations remain active after the migration. No conversation data is removed by the migration itself.

## SQL change prepared

Target: `localhost` / `GuildhallDev`, using the existing Windows migration identity. The additive migration adds `ArchivedUtc` and `IsArchived` (default `false`) to `dbo.Conversations`, then grants the runtime user DELETE on that table only. Existing foreign keys cascade deletion of a conversation's runs and messages. Direct DELETE on `ChatRuns` and `ChatMessages` is not granted. Metadata/history permissions, server settings, services, and credentials are unchanged.

The generated SQL is:

```sql
BEGIN TRANSACTION;
ALTER TABLE [Conversations] ADD [ArchivedUtc] datetime2 NULL;
ALTER TABLE [Conversations] ADD [IsArchived] bit NOT NULL DEFAULT CAST(0 AS bit);
IF DATABASE_PRINCIPAL_ID(N'guildhall_runtime') IS NOT NULL
    GRANT DELETE ON OBJECT::dbo.Conversations TO [guildhall_runtime];
INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261115223557_ConversationLifecycle', N'10.0.15');
COMMIT;
```

The migration and cascade permission passed the isolated SQL integration test against a uniquely named `GuildhallTest_*` database. The approved migration was then applied to `GuildhallDev`. Before/after counts were unchanged: 6 conversations, 16 runs, 32 messages, and 1 installation metadata row. All six conversations remained unarchived. Read-only permission checks through the actual runtime login confirmed DELETE on Conversations, no direct DELETE on ChatRuns/ChatMessages, no UPDATE on installation metadata, and no ALTER on the database. No user conversation was deleted during verification. Native archive/restore/delete interaction has not yet been verified.

The approved command used:

```powershell
pwsh -NoProfile -File .\scripts\database\Migrate-Database.ps1 -DatabaseName GuildhallDev
```

The migration is additive and reversible. Its runtime DELETE authority is limited to conversation parent rows and their dependent-row cascades.

## Stuck-run correction

An abandoned run started at 21:39 UTC was confirmed older than the current host (started at 22:54 UTC) and remained Running without checkpoints. Its status was corrected to Interrupted using a conditional update of that one run; messages were preserved. Opening a saved conversation now refreshes its sidebar active-run flag. The new Stop endpoint signals the registered worker after committing Cancelling; terminal updates honor that durable cancellation and do not overwrite an already terminal run. A worker absent from this host can be finalized without restarting inference. A five-minute cancellation token bounds the worker's response-body reads; HTTP EOF without a terminal provider record is treated as failure. Output checkpoints, first-output/idle timers, global admission, shutdown reconciliation, and native cancellation/race verification still need the remaining Phase 1 reliability work.

Checks passed: backend build, frontend typecheck, 33 deterministic backend tests, 16 frontend tests, and isolated SQL checks covering repeated cancellation of an unowned persisted run, preservation of its message pair, and leaving a Completed run unchanged. These checks do not establish native UI acceptance or a live provider cancellation race.
