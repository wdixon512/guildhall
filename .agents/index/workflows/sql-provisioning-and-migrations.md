---
id: sql-provisioning-and-migrations
title: "SQL Provisioning, DPAPI Runtime Identity & Migrations"
area: sql-persistence-migrations
triggers:
  - Provision-Database.ps1
  - Migrate-Database.ps1
  - Invoke-AutoDatabaseMigration
  - Get-LocalSqlConnection
  - ApproveMigrations
  - guildhall_runtime
  - "LocalConfiguration.RuntimeConnection"
primary_files:
  - scripts/database/Provision-Database.ps1
  - scripts/startup/Start-Dev.ps1
  - scripts/misc/Common.ps1
  - scripts/database/Migrate-Database.ps1
  - services/agent-host/Persistence/DesignTimeFactory.cs
  - services/agent-host/Configuration/LocalConfiguration.cs
verified_commit: "51bca7eb5076022dcd1ff89d460d683c1e1e3848"
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - "docs/masterplan/phase 0/setup-approval.md"
  - "docs/masterplan/phase 5/verification.md"
uncertainties:
  - boundary: "Local SQL Server instance permissions and DPAPI credential contents"
    location: "scripts/misc/Common.ps1::Get-LocalSqlConnection"
    note: "Encrypted runtime.credential.xml under %LOCALAPPDATA%\\Guildhall\\dev and live GuildhallDev SQL grants exist only in the local runtime environment outside git."
    static_or_runtime: "runtime"
---

# SQL Provisioning, DPAPI Runtime Identity & Migrations

Traces least-privilege `guildhall_runtime` provisioning with Windows DPAPI, launcher additive auto-migration vs. `-ApproveMigrations` gate, and host SQL validation.

## Execution Spine

| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Ops / SQL | `scripts/database/Provision-Database.ps1` | `guildhall_runtime` | `~L11-L49` | `GuildhallDev` DB + `guildhall_runtime` SQL login + DPAPI `runtime.credential.xml` | Plan-only unless `-Execute`; refuses to overwrite existing DB, login, or DPAPI file; `DENY` DDL to `guildhall_runtime` |
| 2 | Ops / Scripts | `scripts/startup/Start-Dev.ps1` | `Invoke-AutoDatabaseMigration` | `~L81-L88` | `ConnectionStrings__Guildhall` env var + pre-launch migration gate | Host receives DPAPI runtime connection only; auto-migration skips when `-SkipMigrations` is passed |
| 3 | Ops / SQL | `scripts/misc/Common.ps1` | `Get-LocalSqlConnection` | `~L23-L37` | `%LOCALAPPDATA%\Guildhall\dev\runtime.credential.xml` -> SQL connection string | Runtime switch reads shared DPAPI `pscredential` across worktrees; migration calls use Integrated Security |
| 4 | Ops / SQL | `scripts/misc/Common.ps1` | `Invoke-AutoDatabaseMigration` | `~L39-L81` | `__EFMigrationsHistory` vs `Persistence/Migrations/*_*.cs` | Additive migrations auto-apply; `DropTable`/`DropColumn`/`DROP`/`TRUNCATE`/`GRANT`/`REVOKE`/`DENY` block unless `-ApproveMigrations` |
| 5 | Ops / SQL | `scripts/database/Migrate-Database.ps1` | `ConnectionStrings__GuildhallMigrations` | `~L16-L44` | EF migrations script -> `.local/GuildhallDev.migrations.preview.sql` -> `database update` | Preview-only without `-Execute`; builds Release separately so locked Debug host binary is never overwritten |
| 6 | SQL / EF | `services/agent-host/Persistence/DesignTimeFactory.cs` | `DesignTimeFactory` | `~L8-L18` | `dotnet ef` -> `DesignTimeFactory.CreateDbContext` | Throws if `IntegratedSecurity` is false; only Windows migration identity can run EF schema updates |
| 7 | Agent Host / Security | `services/agent-host/Configuration/LocalConfiguration.cs` | `LocalConfiguration.RuntimeConnection` | `~L32-L50` | `ConnectionStrings:Guildhall` validated at host startup | Rejects `IntegratedSecurity=true` for runtime host; enforces localhost, `Encrypt=Mandatory`, `TrustServerCertificate=true` |

## Contracts & Guardrails

- Skills: [codebase-index](../../skills/codebase-index/SKILL.md), [work-work](../../skills/work-work/SKILL.md)
- Standards & Setup: [coding-standards.md](../../../docs/coding-standards.md), [setup-approval.md](<../../../docs/masterplan/phase 0/setup-approval.md>)
- Phase Record: [verification.md](<../../../docs/masterplan/phase 5/verification.md>)

## Targeted Verification

- `pwsh -NoProfile -File .\scripts\database\Migrate-Database.ps1 -DatabaseName GuildhallDev`
- `dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj -c Release --filter "FullyQualifiedName~ConfigurationTests"`
- `$env:GUILDHALL_TEST_SQL_CONNECTION='Server=localhost;Database=master;Integrated Security=true;Encrypt=True;TrustServerCertificate=True'; dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj -c Release --filter "FullyQualifiedName~PersistenceTests"`
