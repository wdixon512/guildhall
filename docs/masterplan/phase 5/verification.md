# Phase 5 Verification Record

**Branch:** `phase_five_infrastructure_sql_mcp`  
**Date:** November 17, 2026  
**Migration:** `20261117152949_Phase5SqlMcpAndRemoteGateways` (applied to `GuildhallDev`; additive `dbo.SqlMcpProfiles` table + `AllowPrivateTunnelHttp` column + DML-only `guildhall_runtime` grants)

---

## 1. Automated Verification (Run After Final Code Edits)

| Suite | Command | Result |
|---|---|---|
| Host Unit Tests (non-SQL) | `dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj -c Release --no-build --filter "Category!=SqlIntegration"` | **131 / 131 passed** (0 failed, 0 skipped) |
| Host SQL Integration Tests | `$env:GUILDHALL_TEST_SQL_CONNECTION='Server=localhost;Database=master;Integrated Security=true;Encrypt=True;TrustServerCertificate=True'; dotnet test tests/Guildhall.AgentHost.Tests/Guildhall.AgentHost.Tests.csproj -c Release --no-build --filter "Category=SqlIntegration"` | **7 / 7 passed** (0 failed, 0 skipped) |
| Desktop Frontend Unit Tests | `npm.cmd --prefix apps/desktop test -- --run` | **60 / 60 passed** across 12 test files |
| Desktop Frontend Typecheck & Build | `npm.cmd --prefix apps/desktop run build` (`tsc --noEmit && vite build`) | **Passed** |
| Tauri Rust Unit Tests | `cargo test --manifest-path apps/desktop/src-tauri/Cargo.toml --locked` | **2 / 2 passed** |

---

## 2. UI Review Status

- **Static Code & Accessibility Review:** Completed via `.cursor/agents/guildhall-ui-reviewer.md`; High & Medium findings resolved (`PrivateInfrastructurePanel` client-side token length validation & partial-failure refresh, separate SQL/network status banners, `.connection-pillar-group` grid spacing, initial `loading` state + `<Suspense>` fallback, Port Exposure Posture status pill, narrow-viewport `min(..., 100%)` + `overflow-wrap: anywhere` guards, two-step delete confirmations with specific `aria-label`s, `allowedObjects` display, and `aria-expanded` toggles & secret cleanup on cancel).
- **Live Render & Functional QC:** Verified interactively by the owner against `GuildhallDev` (optional/server-wide SQL MCP profiles, `sql_list_profiles` / `sql_list_databases` / `sql_list_objects` / `sql_query` against `bdo-xbk-web`, Tailscale & port `1433`/`11434` posture checks, remote Streamable HTTP MCP registration & live tool-count card for `ahprice-mcp`, dynamic member tool list, and live chat tool calls).

---

## 3. Manual QC Sign-Off

- [x] Migration `20261117152949_Phase5SqlMcpAndRemoteGateways` applied cleanly to `GuildhallDev`.
- [x] `Verify-SqlBackupRestore.ps1 -DatabaseName GuildhallDev -Execute` passed (`COPY_ONLY` backup + `RESTORE VERIFYONLY` + isolated `GuildhallRestoreVerify_<guid>` restore parity).
- [x] Read-only SQL Server MCP (`sql-server-mcp`) verified end-to-end with `localhost-readonly` (`bdo-xbk-web` `dbo.CMS_User`) while `GuildhallDev` and destructive mutations remain blocked.
- [x] Private Networking (Tailscale detection + `1433`/`11434` posture) and Remote Streamable HTTP MCP (`ahprice-mcp`) verified end-to-end in Connections, Council Member tool toggles, and live chat tool calls.
