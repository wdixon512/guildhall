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
export async function requestBlob(config: Bootstrap, path: string, signal: AbortSignal): Promise<Blob> {
  const response = await fetch(new URL(path, config.hostUrl), { headers: { Authorization: `Bearer ${config.token}` }, signal, redirect: 'error', cache: 'no-store' });
  if (!response.ok) throw new Error('Host request failed');
  return response.blob();
}

export type AgentProfile = { id: string; name: string; role: string; portraitId: number; customPortrait?: string | null; accent: string; instructions: string; defaultProviderId?: string; defaultModelId?: string | null; defaultHarnessMode?: string | null; enabledToolIds?: string[]; permissionPolicy?: string; revision: number; isArchived: boolean };
export type ConnectionStatus = 'Connected' | 'Not connected' | 'Needs sign-in' | 'Unavailable' | 'Planned';
export type ConnectionModelOption = { id: string; displayName: string; supportsVision: boolean };
export type ConnectionEntry = { id: string; pillar: string; engineKind: string; displayName: string; authType: string; status: ConnectionStatus; statusDetail?: string | null; keyHint?: string | null; defaultModelId?: string | null; models: ConnectionModelOption[]; canConfigureApiKey: boolean; canProbe: boolean; isPlanned: boolean };
export type ConnectionsCatalogData = { entries: ConnectionEntry[]; checkedUtc: string };
export type GuildhallBehavior = { instructions: string; revision: number };
export type MemoryNoteScope = 'AboutYou' | 'Member';
export type MemoryNote = { id: string; scope: MemoryNoteScope; agentDefinitionId?: string | null; body: string; isEnabled: boolean; sourceConversationId?: string | null; createdUtc: string; updatedUtc: string; revision: number };
export type RunningModelDescriptor = { name: string; sizeBytes: number; sizeVramBytes: number; expiresAtUtc?: string | null; contextLength?: number | null };
export type VramResidency = { providerId: string; status: string; useGpu: boolean; keepAliveSeconds: number; models: RunningModelDescriptor[] };
export type ContextMemoryNoteRef = { id: string; scope: MemoryNoteScope; revision: number; estimatedTokens: number; preview: string };
export type CrossTurnBudgetSummary = { estimatedSessionTokens: number; maxSessionTokens: number; estimatedNextTurnTokens: number; maxTurnContextTokens: number; completedTurns: number; isOverTurnLimit: boolean; isOverSessionBudget: boolean };
export type McpServerHealthView = { serverId: string; displayName: string; transport: string; status: string; consecutiveFailures: number; lastCheckedUtc?: string | null; nextRetryUtc?: string | null; lastError?: string | null };
export type ContextInspectorData = { conversationId: string; agentDefinitionId: string; memberName: string; modelId?: string | null; behaviorRevision: number; behaviorChars: number; globalGuidelinesHash?: string | null; globalGuidelinesChars: number; memberInstructionsChars: number; memoryNotes: ContextMemoryNoteRef[]; retainedHistoryTurns: number; retainedHistoryChars: number; budget: CrossTurnBudgetSummary; vram: VramResidency; estimatedToolSchemaTokens?: number; enabledToolCount?: number; mcpServers?: McpServerHealthView[] };
export type Conversation = { id: string; title: string; selectedProviderId?: string | null; selectedModelId?: string | null; selectedHarnessMode?: string | null; workspaceRootPath?: string | null; permissionPolicyOverride?: string | null; revision: number; isArchived?: boolean; archivedUtc?: string | null; hasActiveRun?: boolean; agentDefinitionId?: string; lastResponseUtc?: string | null };
export type Attachment = { id: string; fileName: string; contentType: string; length: number };
export type RunEventView = { id: string; sequenceNumber: number; eventType: string; serverId: string; toolName: string; securityTier: string; status: string; argumentsRedactedJson: string; resultRedactedJson?: string | null; errorCode?: string | null; durationMs?: number | null; createdUtc?: string; completedUtc?: string | null };
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

