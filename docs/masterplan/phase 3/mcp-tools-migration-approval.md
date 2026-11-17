# Phase 3 Migration & Permission Approval Record

**Migration ID:** `Phase3McpToolsAndTraces`  
**Owner Approval:** Approved on November 16, 2026 (additive schema + DML-only `guildhall_runtime` grants)

## Schema Changes

1. **Additive columns on `dbo.AgentDefinitions`:**
   - `EnabledToolIdsJson nvarchar(2000) NOT NULL DEFAULT N'["filesystem-mcp","git-mcp"]'`
   - `PermissionPolicy nvarchar(40) NOT NULL DEFAULT N'ask_unsafe'`
2. **Additive columns on `dbo.Conversations`:**
   - `WorkspaceRootPath nvarchar(500) NULL`
   - `PermissionPolicyOverride nvarchar(40) NULL`
3. **New table `dbo.ChatRunEvents`:**
   - Durable tool-call and approval trace records linked to `dbo.ChatRuns` (`ON DELETE CASCADE`) with unique index on `(RunId, SequenceNumber)`.
4. **New table `dbo.McpServerRegistrations`:**
   - User-registered custom `stdio` and `Streamable HTTP` MCP servers.

## Restricted `guildhall_runtime` Grants (Zero DDL)

```sql
IF DATABASE_PRINCIPAL_ID(N'guildhall_runtime') IS NOT NULL
BEGIN
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.ChatRunEvents TO [guildhall_runtime];
    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.McpServerRegistrations TO [guildhall_runtime];
END
```

`guildhall_runtime` retains **zero** `CREATE`, `ALTER`, `DROP`, or `db_owner` privileges.
