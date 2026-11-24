---
id: sql-backup-restore-verification
title: "SQL COPY_ONLY Backup & Isolated Restore Verification"
area: sql-persistence-migrations
triggers:
  - Verify-SqlBackupRestore.ps1
  - SqlBackupRestoreTests
  - GuildhallRestoreVerify_
  - COPY_ONLY
  - "RESTORE VERIFYONLY"
primary_files:
  - scripts/database/Verify-SqlBackupRestore.ps1
  - tests/Guildhall.AgentHost.Tests/SqlBackupRestoreTests.cs
verified_commit: "51bca7eb5076022dcd1ff89d460d683c1e1e3848"
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - "docs/masterplan/phase 5/verification.md"
uncertainties:
  - boundary: "SQL Server InstanceDefaultBackupPath/DataPath/LogPath and live GuildhallDev table state"
    location: "scripts/database/Verify-SqlBackupRestore.ps1::Quote-SqlIdentifier"
    note: "SQL Server instance directory paths and live GuildhallDev schema/data state are queried via SERVERPROPERTY at runtime on localhost."
    static_or_runtime: "runtime"
---

# SQL COPY_ONLY Backup & Isolated Restore Verification

Traces non-disruptive `COPY_ONLY` backups, `CHECKSUM` verification, ephemeral `GuildhallRestoreVerify_<id>` restores, table parity checks, and deterministic cleanup.

## Execution Spine

| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Ops / Scripts | `scripts/database/Verify-SqlBackupRestore.ps1` | `Get-LocalSqlConnection` | `~L13-L29` | `-Execute` gate + localhost/master Integrated Security connection | Safe `PLAN ONLY` dry-run by default; requires `-Execute` and approved `GuildhallDev` or `GuildhallTest_*` database name |
| 2 | Ops / SQL | `scripts/database/Verify-SqlBackupRestore.ps1` | `COPY_ONLY` | `~L53-L76` | `BACKUP DATABASE ... WITH COPY_ONLY, CHECKSUM, INIT` + `RESTORE VERIFYONLY` | `COPY_ONLY` preserves existing differential/log backup chains; `CHECKSUM` verifies backup media integrity |
| 3 | Ops / SQL | `scripts/database/Verify-SqlBackupRestore.ps1` | `RESTORE FILELISTONLY` | `~L78-L113` | `RESTORE DATABASE [GuildhallRestoreVerify_<id>] WITH MOVE, RECOVERY, REPLACE` | Reads logical data/log names from backup header and restores into isolated `GuildhallRestoreVerify_<id>` files |
| 4 | Ops / SQL | `scripts/database/Verify-SqlBackupRestore.ps1` | `INFORMATION_SCHEMA.TABLES` | `~L115-L152` | Base table count parity + `finally` `SINGLE_USER` `DROP DATABASE` + file cleanup | Throws on source vs. restored base table count mismatch; always drops temp DB and deletes `.bak`/`.mdf`/`.ldf` in `finally` |
| 5 | SQL / EF | `tests/Guildhall.AgentHost.Tests/SqlBackupRestoreTests.cs` | `SqlBackupRestoreTests.IsolatedCopyOnlyBackupAndRestorePreservesMigrationsAndRows` | `~L14-L86` | `GUILDHALL_TEST_SQL_CONNECTION` -> `GuildhallBackupTest_<id>` `MigrateAsync` + `COPY_ONLY` | Requires localhost/master `IntegratedSecurity=true`; isolates test data in `GuildhallBackupTest_<id>`, never `GuildhallDev` |
| 6 | SQL / EF | `tests/Guildhall.AgentHost.Tests/SqlBackupRestoreTests.cs` | `GetPendingMigrationsAsync` | `~L104-L134` | Restored `GuildhallRestoreVerify_<id>` -> `GetPendingMigrationsAsync` + `SqlMcpProfile` parity | Verifies zero pending migrations and exact seeded row parity before dropping both scratch DBs and `.bak` |

## Contracts & Guardrails

- Skills: [codebase-index](../../skills/codebase-index/SKILL.md), [work-work](../../skills/work-work/SKILL.md)
- Standards & Verification: [coding-standards.md](../../../docs/coding-standards.md), [verification.md](<../../../docs/masterplan/phase 5/verification.md>)

## Targeted Verification

- `pwsh -NoProfile -File .\scripts\database\Verify-SqlBackupRestore.ps1 -DatabaseName GuildhallDev`
- `$env:GUILDHALL_TEST_SQL_CONNECTION='Server=localhost;Database=master;Integrated Security=true;Encrypt=True;TrustServerCertificate=True'; dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj -c Release --filter "FullyQualifiedName~SqlBackupRestoreTests.IsolatedCopyOnlyBackupAndRestorePreservesMigrationsAndRows"`
