---
area: sql-persistence-migrations
title: "SQL Persistence, Provisioning & Migrations"
summary: "EF Core 10 schema (16 entities, ILazyLoader, OCC), DPAPI/Windows migration split, and backup/restore verification."
primary_files:
  - services/agent-host/Program.cs
  - services/agent-host/Persistence/GuildhallDbContext.cs
  - services/agent-host/Persistence/DatabaseReadiness.cs
  - services/agent-host/Persistence/DesignTimeFactory.cs
  - services/agent-host/Api/ChatEndpoints.cs
  - services/agent-host/Configuration/LocalConfiguration.cs
  - scripts/database/Provision-Database.ps1
  - scripts/database/Migrate-Database.ps1
  - scripts/database/Verify-SqlBackupRestore.ps1
  - scripts/misc/Common.ps1
  - scripts/startup/Start-Dev.ps1
  - tests/Guildhall.AgentHost.Tests/SqlBackupRestoreTests.cs
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - docs/decisions/0004-chat-attachments.md
  - docs/decisions/0005-codebase-workflow-index.md
  - "docs/masterplan/phase 0/setup-approval.md"
  - "docs/masterplan/phase 5/verification.md"
---

# SQL Persistence, Provisioning & Migrations

Owns the EF Core 10 persistence model in `GuildhallDev`, runtime DPAPI vs. Windows Integrated Security migration split, readiness checks, and isolated `COPY_ONLY` backup/restore verification.

## Module & File Ownership

| Module / File | Ownership | Shared State / Locks | Entrypoints |
| :--- | :--- | :--- | :--- |
| `services/agent-host/Persistence/GuildhallDbContext.cs` | 16 EF Core 10 entities, fluent schema mappings, `ILazyLoader` navigations, and OCC `Revision` tokens | `GuildhallDev` tables; 8 `Revision` concurrency tokens; `ChatAttachments.Content` `varbinary(max)` | `GuildhallDbContext`, `OnModelCreating` |
| `services/agent-host/Persistence/DatabaseReadiness.cs` | Runtime SQL connectivity, migration parity, and `ApplicationMetadata` readiness check | 4s `CancellationTokenSource` deadline; 3s SQL `CommandTimeout` | `DatabaseReadiness.CheckAsync` (`/ready`) |
| `services/agent-host/Persistence/DesignTimeFactory.cs` | EF Core design-time factory enforcing Windows Integrated Security for migrations | `ConnectionStrings__GuildhallMigrations` env var | `DesignTimeFactory.CreateDbContext` |
| `services/agent-host/Configuration/LocalConfiguration.cs` | Loopback URL and SQL Server connection validation for runtime vs. migration identities | `GuildhallDev` or `GuildhallTest_*` catalog allowlist; `Encrypt=Mandatory` | `LocalConfiguration.RuntimeConnection`, `ValidateSql` |
| `scripts/database/Provision-Database.ps1` | One-time, fail-closed initial `GuildhallDev` provisioning, `guildhall_runtime` login, and DPAPI credential creation | `%LOCALAPPDATA%\Guildhall\dev\runtime.credential.xml` (DPAPI) | `Provision-Database.ps1 -Execute` |
| `scripts/misc/Common.ps1` | Shared worktree DPAPI/Windows SQL connection builder and additive vs. gated auto-migration helper | `__EFMigrationsHistory` vs `Persistence/Migrations/*_*.cs` | `Get-LocalSqlConnection`, `Invoke-AutoDatabaseMigration` |
| `scripts/database/Migrate-Database.ps1` | SQL preview generator and Windows-identity EF Core migration runner | `.local/GuildhallDev.migrations.preview.sql` | `Migrate-Database.ps1 [-Execute]` |
| `scripts/database/Verify-SqlBackupRestore.ps1` | Isolated `COPY_ONLY` backup, `CHECKSUM` verify, ephemeral restore, and table-count parity check | Ephemeral `GuildhallRestoreVerify_<id>` DB + temp `.bak`/`.mdf`/`.ldf` cleanup | `Verify-SqlBackupRestore.ps1 [-Execute]` |

## Workflows

- [`ef-core-schema-and-lazy-loading`](../workflows/ef-core-schema-and-lazy-loading.md) — Traces `IDbContextFactory` registration, 16 sealed entities with `ILazyLoader` and OCC `Revision` tokens, deferred `ChatAttachment` blob loading, and `/ready` checks.
- [`sql-provisioning-and-migrations`](../workflows/sql-provisioning-and-migrations.md) — Traces least-privilege `guildhall_runtime` provisioning with Windows DPAPI, launcher additive auto-migration vs. `-ApproveMigrations` gate, and host SQL validation.
- [`sql-backup-restore-verification`](../workflows/sql-backup-restore-verification.md) — Traces non-disruptive `COPY_ONLY` backups, `CHECKSUM` verification, ephemeral `GuildhallRestoreVerify_<id>` restores, table parity checks, and deterministic cleanup.

## Contracts & References

- Skills: [codebase-index](../../skills/codebase-index/SKILL.md), [work-work](../../skills/work-work/SKILL.md)
- Standards & Decisions: [coding-standards.md](../../../docs/coding-standards.md), [0004-chat-attachments.md](../../../docs/decisions/0004-chat-attachments.md), [0005-codebase-workflow-index.md](../../../docs/decisions/0005-codebase-workflow-index.md)
- Phase Records: [setup-approval.md](<../../../docs/masterplan/phase 0/setup-approval.md>), [verification.md](<../../../docs/masterplan/phase 5/verification.md>)
