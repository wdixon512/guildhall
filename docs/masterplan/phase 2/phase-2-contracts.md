# Phase 2 API & Credential Contracts

## Connections Endpoints
- `GET /connections?probe=false|true` -> `ConnectionsCatalogResponse` (`entries[]` across `Local Runtimes`, `Cloud Model APIs`, `Local CLI Agent Harnesses`, `MCP & Service Integrations`)
- `PUT /connections/{id}/api-key` (`{ apiKey }`) -> encrypts with Windows DPAPI, probes endpoint, returns updated `ConnectionEntryResponse` (never returns the raw key)
- `DELETE /connections/{id}/api-key` -> deletes `.dpapi` blob, returns updated `ConnectionEntryResponse`
- `POST /connections/{id}/probe` -> live-probes Ollama, Cloud API, or CLI binary/auth status

## Extended Council & Conversation Contracts
- `AgentProfileResponse` / `CreateAgentProfileRequest` / `UpdateAgentProfileRequest`: optional `defaultProviderId` (`"ollama"` default) and `defaultHarnessMode`
- `ConversationSummary` / `CreateConversationRequest` / `UpdateConversationRequest`: optional `selectedProviderId` / `providerId` and `selectedHarnessMode` / `harnessMode`
- `MessageView`: optional `providerId`, `engineKind`, `inputTokens`, `outputTokens`, `costSource`
