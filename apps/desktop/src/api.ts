import { invoke } from '@tauri-apps/api/core';

export interface Bootstrap { hostUrl: string; token: string; windowTitle?: string; instanceTag?: string }
export interface Readiness { status: 'ready' | 'unavailable'; reason?: string }
export interface Discovery {
  providerId: 'ollama';
  status: 'available' | 'no_models' | 'unavailable' | 'invalid_response';
  models: { id: string; displayName: string }[];
}
export async function bootstrap(): Promise<Bootstrap> { return invoke<Bootstrap>('bootstrap'); }

export class HostRequestError extends Error {
  code?: string;
  serverMessage?: string;
  constructor(code?: string, serverMessage?: string) {
    super('Host request failed');
    this.code = code;
    this.serverMessage = serverMessage;
  }
}

export async function request(config: Bootstrap, path: string, signal: AbortSignal, init?: RequestInit): Promise<unknown> {
  const response = await fetch(new URL(path, config.hostUrl), {
    ...init, headers: { Authorization: `Bearer ${config.token}`, ...(init?.headers ?? {}) }, signal, redirect: 'error', cache: 'no-store',
  });
  if (!response.ok && !(path === '/ready' && response.status === 503)) {
    try {
      const payload = await response.json() as { code?: unknown; message?: unknown };
      throw new HostRequestError(
        typeof payload.code === 'string' ? payload.code : undefined,
        typeof payload.message === 'string' ? payload.message : undefined,
      );
    } catch (reason) {
      if (reason instanceof HostRequestError) throw reason;
      throw new HostRequestError();
    }
  }
  if (response.status === 204) return null;
  return response.json();
}
export async function requestJson<T>(
  config: Bootstrap,
  path: string,
  method: 'GET' | 'POST' | 'PATCH' | 'PUT' | 'DELETE',
  signal: AbortSignal,
  body?: unknown,
): Promise<T> {
  return request(config, path, signal, {
    method,
    ...(body !== undefined ? { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) } : {}),
  }) as Promise<T>;
}

export async function requestBlob(config: Bootstrap, path: string, signal: AbortSignal): Promise<Blob> {
  const response = await fetch(new URL(path, config.hostUrl), { headers: { Authorization: `Bearer ${config.token}` }, signal, redirect: 'error', cache: 'no-store' });
  if (!response.ok) throw new HostRequestError(undefined, `Host request failed (${response.status})`);
  return response.blob();
}

export type AgentProfile = { id: string; name: string; role: string; portraitId: number; customPortrait?: string | null; accent: string; instructions: string; defaultProviderId?: string; defaultModelId?: string | null; defaultHarnessMode?: string | null; enabledToolIds?: string[]; permissionPolicy?: string; revision: number; isArchived: boolean };
export type ConnectionStatus = 'Connected' | 'Not connected' | 'Needs sign-in' | 'Unavailable' | 'Planned';
export type ConnectionModelOption = { id: string; displayName: string; supportsVision: boolean };
export type ConnectionEntry = { id: string; pillar: string; engineKind: string; displayName: string; authType: string; status: ConnectionStatus; statusDetail?: string | null; keyHint?: string | null; defaultModelId?: string | null; models: ConnectionModelOption[]; canConfigureApiKey: boolean; canProbe: boolean; isPlanned: boolean };
export type ConnectionsCatalogData = { entries: ConnectionEntry[]; checkedUtc: string };
export type GuildhallBehavior = { instructions: string; revision: number };
export type MemoryNoteScope = 'AboutYou' | 'Member' | 'Project';
export type MemoryNote = { id: string; scope: MemoryNoteScope; agentDefinitionId?: string | null; projectId?: string | null; body: string; isEnabled: boolean; sourceConversationId?: string | null; createdUtc: string; updatedUtc: string; revision: number };
export type RunningModelDescriptor = { name: string; sizeBytes: number; sizeVramBytes: number; expiresAtUtc?: string | null; contextLength?: number | null };
export type VramResidency = { providerId: string; status: string; useGpu: boolean; keepAliveSeconds: number; models: RunningModelDescriptor[] };
export type ContextMemoryNoteRef = { id: string; scope: MemoryNoteScope; revision: number; estimatedTokens: number; preview: string };
export type CrossTurnBudgetSummary = { estimatedSessionTokens: number; maxSessionTokens: number; estimatedNextTurnTokens: number; maxTurnContextTokens: number; completedTurns: number; isOverTurnLimit: boolean; isOverSessionBudget: boolean };
export type McpServerHealthView = { serverId: string; displayName: string; transport: string; status: string; consecutiveFailures: number; lastCheckedUtc?: string | null; nextRetryUtc?: string | null; lastError?: string | null };
export type ContextInspectorData = { conversationId: string; agentDefinitionId: string; memberName: string; modelId?: string | null; behaviorRevision: number; behaviorChars: number; globalGuidelinesHash?: string | null; globalGuidelinesChars: number; memberInstructionsChars: number; memoryNotes: ContextMemoryNoteRef[]; retainedHistoryTurns: number; retainedHistoryChars: number; budget: CrossTurnBudgetSummary; vram: VramResidency; estimatedToolSchemaTokens?: number; enabledToolCount?: number; mcpServers?: McpServerHealthView[] };
export type Conversation = { id: string; title: string; selectedProviderId?: string | null; selectedModelId?: string | null; selectedHarnessMode?: string | null; workspaceRootPath?: string | null; permissionPolicyOverride?: string | null; projectId?: string | null; activeWorktreeId?: string | null; activeWorktreeBranch?: string | null; activeWorktreePath?: string | null; revision: number; isArchived?: boolean; archivedUtc?: string | null; hasActiveRun?: boolean; agentDefinitionId?: string; lastResponseUtc?: string | null };
export type Attachment = { id: string; fileName: string; contentType: string; length: number };
export type RunEventView = { id: string; sequenceNumber: number; eventType: string; serverId: string; toolName: string; securityTier: string; status: string; argumentsRedactedJson: string; resultRedactedJson?: string | null; errorCode?: string | null; durationMs?: number | null; createdUtc?: string; completedUtc?: string | null; rollbackStatus?: string | null; canRollback?: boolean };
export type RegisteredProject = { id: string; name: string; rootPath: string; isGitRepository: boolean; defaultBranch?: string | null; createdUtc: string; updatedUtc: string; lastOpenedUtc: string; revision: number };
export type ProjectBranches = { branches: string[]; defaultBranch?: string | null };
export type WorktreeView = { id: string; projectId: string; conversationId: string; branchName: string; worktreePath: string; baseCommitSha: string; status: string; createdUtc: string; updatedUtc: string };
export type WorktreeDiff = { worktreeId: string; branchName: string; baseCommitSha: string; diffText: string; changedFiles: string[]; hasChanges: boolean };
export type WorktreeMergeResult = { merged: boolean; mergedCommitSha?: string | null; message: string };
export type WorkspaceAccessPolicy = { policyOverride?: string | null; revision: number };
export type WorktreeSettings = { defaultWorktreeRootPath?: string | null; revision: number };
export type SqlMcpProfileView = { id: string; name: string; server: string; database: string; authMode: 'integrated' | 'sql_login'; allowedSchemas: string[]; allowedObjects: string[]; maxRows: number; commandTimeoutSeconds: number; isEnabled: boolean; createdUtc: string; updatedUtc: string; revision: number };
export type SqlMcpProbeResponse = { profileId: string; name: string; healthy: boolean; detail?: string | null; checkedUtc: string };
export type CustomMcpServerView = { id: string; displayName: string; transport: 'stdio' | 'streamable_http'; endpointUrl?: string | null; allowPrivateTunnelHttp: boolean; securityTier: string; isEnabled: boolean; hasToken: boolean; revision: number; toolCount?: number; healthStatus?: string };
export type TunnelInterfaceInfo = { name: string; kind: string; status: string; addresses: string[] };
export type PortExposureCheck = { port: number; serviceName: string; posture: string; listeningAddresses: string[]; summary: string };
export type PrivateNetworkStatus = { wireGuardDetected: boolean; tailscaleDetected: boolean; tunnelInterfaces: TunnelInterfaceInfo[]; portChecks: PortExposureCheck[]; checkedUtc: string };
export type HealthTierStatus = { tierId: string; displayName: string; status: string; summary: string; consecutiveFailures: number; lastCheckedUtc?: string | null; nextRetryUtc?: string | null; canRecover: boolean };
export type FiveTierHealthSnapshot = { checkedUtc: string; overallStatus: string; tiers: HealthTierStatus[]; vram?: VramResidency | null };
export type ScheduledAutomationView = { id: string; name: string; agentDefinitionId: string; agentName: string; projectId?: string | null; conversationId?: string | null; prompt: string; cronExpression: string; timezoneId: string; misfirePolicy: 'Skip' | 'RunOnce' | 'CatchUpBounded'; maxCatchUpRuns: number; maxBudgetTokens?: number | null; maxDurationSeconds: number; isPaused: boolean; lastRunUtc?: string | null; nextRunUtc?: string | null; createdUtc: string; updatedUtc: string; revision: number; lastRunStatus?: string | null };
export type DurableTaskRunView = { id: string; scheduledAutomationId?: string | null; agentDefinitionId: string; chatRunId?: string | null; conversationId?: string | null; triggerKind: string; scheduledForUtc: string; status: string; attemptCount: number; errorCode?: string | null; summaryText?: string | null; createdUtc: string; startedUtc?: string | null; finishedUtc?: string | null; inputTokens?: number | null; outputTokens?: number | null; durationMs?: number | null };

export async function pickWorkspaceFolder(): Promise<string | null> {
  return invoke<string | null>('pick_workspace_folder');
}
export type MessageView = { id: string; runId?: string; role: string; text: string; status: string; turnNumber: number; modelId?: string; providerId?: string | null; engineKind?: string | null; inputTokens?: number | null; outputTokens?: number | null; costSource?: string | null; errorCode?: string | null; responseDurationSeconds?: number | null; attachments: Attachment[]; events?: RunEventView[] | null };
export type ConversationDetails = { conversation: Conversation; agent?: AgentProfile; messages: MessageView[] };
export type RunAcceptedResponse = { runId: string; conversationRevision: number };

export function parseReadiness(value: unknown): Readiness {
  if (value === null || typeof value !== 'object' || !('status' in value) ||
    !['ready', 'unavailable'].includes(String(value.status))) throw new Error('Invalid readiness response');
  const item = value as Record<string, unknown>;
  const reasons = ['database_not_configured', 'database_unavailable', 'migration_required'];
  if (item.status === 'unavailable' && (typeof item.reason !== 'string' || !reasons.includes(item.reason)))
    throw new Error('Invalid readiness response');
  return { status: item.status as Readiness['status'], reason: item.reason as string | undefined };
}

export function parseDiscovery(value: unknown): Discovery {
  if (value === null || typeof value !== 'object') throw new Error('Invalid model response');
  const item = value as Record<string, unknown>;
  if (item.providerId !== 'ollama' || !['available', 'no_models', 'unavailable', 'invalid_response'].includes(String(item.status)) ||
    !Array.isArray(item.models) || !item.models.every(model => model && typeof model.id === 'string' && typeof model.displayName === 'string'))
    throw new Error('Invalid model response');
  return item as unknown as Discovery;
}

