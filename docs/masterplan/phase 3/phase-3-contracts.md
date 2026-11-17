# Phase 3 API & Persistence Contracts

## 1. SQL Entities (`Phase3McpToolsAndTraces`)

### Extended `AgentDefinitions`
- `EnabledToolIdsJson` (`nvarchar(2000)`, default `'["filesystem-mcp","git-mcp"]'`)
- `PermissionPolicy` (`nvarchar(40)`, default `'ask_unsafe'`, values: `'always_ask'`, `'ask_unsafe'`)

### Extended `Conversations`
- `WorkspaceRootPath` (`nvarchar(500)`, nullable)
- `PermissionPolicyOverride` (`nvarchar(40)`, nullable)

### New `ChatRunEvents`
- `Id` (`uniqueidentifier` PK)
- `RunId` (`uniqueidentifier` FK -> `ChatRuns.Id` ON DELETE CASCADE)
- `SequenceNumber` (`int`)
- `EventType` (`nvarchar(40)`: `tool.requested`, `approval.requested`, `approval.resolved`, `tool.completed`, `tool.failed`)
- `ServerId` (`nvarchar(80)`)
- `ToolName` (`nvarchar(120)`)
- `SecurityTier` (`nvarchar(20)`: `Tier0`, `Tier1`, `Tier2`, `Tier3`)
- `Status` (`nvarchar(30)`: `Running`, `PendingApproval`, `Approved`, `Denied`, `Completed`, `Failed`)
- `ArgumentsRedactedJson` (`nvarchar(max)`)
- `ResultRedactedJson` (`nvarchar(max)`, nullable)
- `ErrorCode` (`nvarchar(80)`, nullable)
- `DurationMs` (`bigint`, nullable)
- `CreatedUtc` (`datetime2`)
- `CompletedUtc` (`datetime2`, nullable)

### New `McpServerRegistrations`
- `Id` (`nvarchar(80)` PK)
- `DisplayName` (`nvarchar(120)`)
- `Transport` (`nvarchar(30)`: `stdio`, `streamable_http`)
- `Command` (`nvarchar(500)`, nullable)
- `ArgumentsJson` (`nvarchar(2000)`, default `'[]'`)
- `EndpointUrl` (`nvarchar(500)`, nullable)
- `SecurityTier` (`nvarchar(20)`, default `'Tier3'`)
- `IsEnabled` (`bit`, default `1`)
- `CreatedUtc` (`datetime2`)
- `UpdatedUtc` (`datetime2`)
- `Revision` (`bigint`, concurrency token)

---

## 2. REST Endpoints

### MCP Discovery, Registration & Health
- `GET /mcp/tools` -> `McpCatalogResponse` (`servers[]` with `id`, `displayName`, `transport`, `healthStatus`, `consecutiveFailures`, `lastCheckedUtc`, `lastError`, `tools[]`)
- `POST /mcp/servers` (`CreateMcpServerRequest`) -> registers a custom `stdio` or `streamable_http` MCP server
- `DELETE /mcp/servers/{id}?expectedRevision={rev}` -> removes a custom MCP server registration
- `POST /mcp/servers/{id}/probe` -> forces an immediate Tier 5 health check & `tools/list` refresh

### Run Approvals & Traces
- `POST /runs/{runId}/events/{eventId}/decision` (`{ approved: bool }`) -> resolves a `PendingApproval` `ChatRunEvent` and unblocks or denies the paused tool call in `ChatRunner`
- `MessageView.events` (`RunEventView[]`) -> projected onto assistant messages in `GET /conversations/{id}` so the 250ms snapshot poller delivers live tool traces and approval prompts.

### Extended Context Inspector
- `ContextInspectorResponse` adds:
  - `estimatedToolSchemaTokens` (`int`)
  - `enabledToolCount` (`int`)
  - `mcpServers` (`McpServerHealthView[]`)
