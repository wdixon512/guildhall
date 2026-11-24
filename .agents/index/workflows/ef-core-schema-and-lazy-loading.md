---
id: ef-core-schema-and-lazy-loading
title: "EF Core 10 Schema, Lazy Loading & Readiness"
area: sql-persistence-migrations
triggers:
  - GuildhallDbContext
  - IDbContextFactory
  - ILazyLoader
  - ChatAttachment
  - Revision
  - DatabaseReadiness
  - DesignTimeFactory
  - "GET /ready"
  - "GET /attachments/{id}"
primary_files:
  - services/agent-host/Program.cs
  - services/agent-host/Persistence/GuildhallDbContext.cs
  - services/agent-host/Api/ChatEndpoints.cs
  - services/agent-host/Persistence/DatabaseReadiness.cs
  - services/agent-host/Persistence/DesignTimeFactory.cs
verified_commit: "51bca7eb5076022dcd1ff89d460d683c1e1e3848"
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - docs/decisions/0004-chat-attachments.md
  - "docs/masterplan/phase 5/verification.md"
uncertainties: []
---

# EF Core 10 Schema, Lazy Loading & Readiness

Traces `IDbContextFactory` registration, 16 sealed entities with `ILazyLoader` and OCC `Revision` tokens, deferred `ChatAttachment` blob loading, and `/ready` checks.

## Execution Spine

| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Agent Host | `services/agent-host/Program.cs` | `AddDbContextFactory` | `~L44-L47` | DI `IDbContextFactory<GuildhallDbContext>` + singleton `IDatabaseReadiness` | `ValidateScopes`/`ValidateOnBuild` enabled; singletons create short-lived contexts via factory instead of captive `DbContext` |
| 2 | SQL / EF | `services/agent-host/Persistence/GuildhallDbContext.cs` | `GuildhallDbContext` | `~L325-L342` | 16 `DbSet<T>` tables mapped to `GuildhallDev` | All 16 entities are sealed without `Microsoft.EntityFrameworkCore.Proxies`; 15 EF Core migrations |
| 3 | SQL / EF | `services/agent-host/Persistence/GuildhallDbContext.cs` | `ILazyLoader` | `~L177-L225` | `ILazyLoader` navigations on `ChatRunEvent`, `RegisteredProject`, `ProjectWorktree`, `ScheduledAutomation`, `DurableTaskRun` | Private `ILazyLoader` constructor avoids Castle.Core proxies; lazy navigations must load before `DbContext` disposal |
| 4 | SQL / EF | `services/agent-host/Persistence/GuildhallDbContext.cs` | `IsConcurrencyToken` | `~L348-L378` | `OnModelCreating` OCC `Revision` tokens + `ChatAttachments` `varbinary(max)` | 8 mutable entities enforce OCC `Revision` concurrency tokens; `ChatAttachments.Content` stores `varbinary(max)` |
| 5 | Agent Host / SQL | `services/agent-host/Api/ChatEndpoints.cs` | `ChatAttachments` | `~L395-L452` | `GET /conversations/{id}` projection vs `GET /attachments/{id}` blob load | History queries project metadata with `AsNoTracking` and omit `Content` bytes; binary `varbinary(max)` loads only on download |
| 6 | Agent Host / SQL | `services/agent-host/Persistence/DatabaseReadiness.cs` | `DatabaseReadiness.CheckAsync` | `~L11-L32` | `GET /ready` -> `CanConnectAsync` + `GetAppliedMigrationsAsync` + `ApplicationMetadata` | 4s deadline and 3s SQL timeout; reports `migration_required` or `database_unavailable` without running migrations at runtime |
| 7 | SQL / EF | `services/agent-host/Persistence/DesignTimeFactory.cs` | `DesignTimeFactory.CreateDbContext` | `~L8-L18` | `ConnectionStrings__GuildhallMigrations` -> design-time `GuildhallDbContext` | Requires Windows Integrated Security and approved localhost `GuildhallDev` or `GuildhallTest_*` target |

## Contracts & Guardrails

- Skills: [codebase-index](../../skills/codebase-index/SKILL.md), [work-work](../../skills/work-work/SKILL.md)
- Standards & Decisions: [coding-standards.md](../../../docs/coding-standards.md) (`IDbContextFactory`, `ILazyLoader`, `AsNoTracking` projections), [0004-chat-attachments.md](../../../docs/decisions/0004-chat-attachments.md)
- Phase Record: [verification.md](<../../../docs/masterplan/phase 5/verification.md>)

## Targeted Verification

- `dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj -c Release --filter "FullyQualifiedName~ConfigurationTests"`
- `$env:GUILDHALL_TEST_SQL_CONNECTION='Server=localhost;Database=master;Integrated Security=true;Encrypt=True;TrustServerCertificate=True'; dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj -c Release --filter "FullyQualifiedName~PersistenceTests"`
