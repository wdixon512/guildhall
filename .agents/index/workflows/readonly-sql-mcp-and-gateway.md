---
id: readonly-sql-mcp-and-gateway
title: "Read-Only SQL Server MCP & Gateway"
area: infra-scheduling-health
triggers:
  - "/mcp/sql/profiles"
  - "POST /mcp/gateway"
  - sql-server-mcp
  - sql_query
  - SqlReadOnlyGuard
  - SqlServerMcpProvider
  - SqlMcpProfile
primary_files:
  - services/agent-host/Api/SqlMcpEndpoints.cs
  - services/agent-host/Api/McpGatewayEndpoints.cs
  - services/agent-host/Mcp/McpRegistry.cs
  - services/agent-host/Mcp/Providers/SqlReadOnlyGuard.cs
  - services/agent-host/Mcp/Providers/SqlServerMcpProvider.cs
verified_commit: 51bca7eb5076022dcd1ff89d460d683c1e1e3848
related_skills:
  - codebase-index
  - work-work
related_docs:
  - docs/coding-standards.md
  - "docs/masterplan/phase 5/private-networking.md"
  - "docs/masterplan/phase 5/verification.md"
uncertainties:
  - boundary: "DPAPI SQL MCP credentials and external SQL Server permissions"
    location: "services/agent-host/Mcp/Providers/SqlServerMcpProvider.cs::SqlServerMcpProvider"
    note: "DPAPI sql-mcp-*.dpapi files live under %LOCALAPPDATA%\\Guildhall\\credentials outside git, and target SQL Server permissions depend on the local/tunnel instance."
    static_or_runtime: "runtime"
---

# Read-Only SQL Server MCP & Gateway

Manages read-only SQL Server MCP profiles and executes Tier 1 read-only queries via `sql-server-mcp` or `POST /mcp/gateway` while blocking `GuildhallDev` and mutating SQL.

## Execution Spine

| Hop | Layer | File | Symbol / Anchor | Line Hint | State / Wire | Invariant / Trap |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Agent Host / SQL | `services/agent-host/Api/SqlMcpEndpoints.cs` | `SqlMcpEndpoints.MapSqlMcpEndpoints` | `~L12-L144` | `GET/POST/DELETE /mcp/sql/profiles`, `POST /mcp/sql/profiles/{id}/probe`, `GuildhallDbContext.SqlMcpProfiles`, DPAPI `sql-mcp-{id:N}` | `LocalApiBoundary` bearer token; `SqlReadOnlyGuard` blocks `GuildhallDev`; `MaxRows` clamped `[1,500]`, timeout `[1,30]s`; `sql_login` credentials stored only in DPAPI. |
| 2 | Security | `services/agent-host/Mcp/Providers/SqlReadOnlyGuard.cs` | `SqlReadOnlyGuard.ValidateDatabaseTarget` | `~L23-L33` | `BlockedDatabases` (`GuildhallDev`) | Case-insensitive reject of `GuildhallDev`; permits empty/`*` (server-wide) or user DB names. |
| 3 | Agent Host / MCP | `services/agent-host/Api/McpGatewayEndpoints.cs` | `McpGatewayEndpoints.MapMcpGatewayEndpoints` | `~L15-L127` | `POST /mcp/gateway` JSON-RPC 2.0 (`initialize`, `tools/list`, `tools/call`) -> `McpRegistry` | `LocalApiBoundary` bearer token; `GatewayAllowedServerIds` exposes ONLY `sql-server-mcp` and ONLY `ToolSecurityTiers.Tier1` tools. |
| 4 | MCP | `services/agent-host/Mcp/McpRegistry.cs` | `McpRegistry.InvokeToolAsync` | `~L114-L132` | `_builtIns["sql-server-mcp"]`, `McpHealthSupervisor.CanAttempt` / `RecordSuccess` / `RecordFailure` | Enforces Tier 5 health circuit breaker before invocation and applies `ExfiltrationGuard.TruncateAndRedact` to output/errors. |
| 5 | MCP | `services/agent-host/Mcp/Providers/SqlServerMcpProvider.cs` | `SqlServerMcpProvider.InvokeAsync` | `~L113-L160` | `sql_list_profiles`, `sql_list_databases`, `sql_list_objects`, `sql_describe_object`, `sql_query`; `GuildhallDbContext.SqlMcpProfiles.AsNoTracking()` | All 5 tools are Tier 1; re-validates `profile.Database`; `sql_list_databases` excludes `database_id<=4` and `GuildhallDev`; schema/object allowlists enforced on `sql_list_objects` & `sql_describe_object` (`sql_query` does not enforce schema/object allowlists). |
| 6 | Security | `services/agent-host/Mcp/Providers/SqlReadOnlyGuard.cs` | `SqlReadOnlyGuard.ValidateQuery` | `~L35-L144` | `StripCommentsAndLiterals`, `BlockedKeywords`, `IdentifierRegex` | Rejects multi-statement `;` batches, non-SELECT/WITH leaders, DDL/DML/EXEC/INTO/OPENROWSET, `xp_`/`sp_` procs, `sql_logins`/`syslogins`/`credentials`, and `GuildhallDev` refs. |
| 7 | SQL / EF | `services/agent-host/Mcp/Providers/SqlServerMcpProvider.cs` | `SqlServerMcpProvider.ExecuteQueryAsync` | `~L296-L359` | `SqlConnection`, `SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; SET ROWCOUNT {effectiveMaxRows}`, `ExfiltrationGuard.TruncateAndRedact` | Clamps `maxRows` `[1,500]` and `CommandTimeout` `[1,30]s`; uses `CommandBehavior.SequentialAccess` and redacts/summarizes binary columns. |
| 8 | Security | `services/agent-host/Mcp/Providers/SqlServerMcpProvider.cs` | `SqlServerMcpProvider.BuildConnectionStringAsync` | `~L401-L448` | `SqlConnectionStringBuilder`, `ICredentialStore` DPAPI key `sql-mcp-{profileId:N}` | Forces `ApplicationIntent=ReadOnly`, `Encrypt=Mandatory`, `Pooling=false`; reads `sql_login` credentials from DPAPI without logging. |

## Contracts & Guardrails

- Follow [coding-standards.md](../../../docs/coding-standards.md) (`sealed` services, `AsNoTracking()`, DPAPI secret storage, no secret logging).
- Enforce Phase 5 read-only SQL MCP and gateway boundaries per [private-networking.md](<../../../docs/masterplan/phase 5/private-networking.md>) and [verification.md](<../../../docs/masterplan/phase 5/verification.md>).
- Index maintenance and workflow execution follow [codebase-index](../../skills/codebase-index/SKILL.md) and [work-work](../../skills/work-work/SKILL.md).

## Targeted Verification

- `dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj -c Release --filter "FullyQualifiedName~SqlReadOnlyGuardTests|FullyQualifiedName~Phase5SqlAndGatewayTests"`
- `npm.cmd --prefix apps/desktop test -- --run src/PrivateInfrastructurePanel.test.tsx`
