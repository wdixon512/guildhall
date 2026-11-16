import { invoke } from '@tauri-apps/api/core';

export interface Bootstrap { hostUrl: string; token: string }
export interface Readiness { status: 'ready' | 'unavailable'; reason?: string }
export interface Discovery {
  providerId: 'ollama';
  status: 'available' | 'no_models' | 'unavailable' | 'invalid_response';
  models: { id: string; displayName: string }[];
}
export async function bootstrap(): Promise<Bootstrap> { return invoke<Bootstrap>('bootstrap'); }

export async function request(config: Bootstrap, path: string, signal: AbortSignal, init?: RequestInit): Promise<unknown> {
  const response = await fetch(new URL(path, config.hostUrl), {
    ...init, headers: { Authorization: `Bearer ${config.token}`, ...(init?.headers ?? {}) }, signal, redirect: 'error', cache: 'no-store',
  });
  if (!response.ok && !(path === '/ready' && response.status === 503)) throw new Error('Host request failed');
  if (response.status === 204) return null;
  return response.json();
}
export async function requestBlob(config: Bootstrap, path: string, signal: AbortSignal): Promise<Blob> {
  const response = await fetch(new URL(path, config.hostUrl), { headers: { Authorization: `Bearer ${config.token}` }, signal, redirect: 'error', cache: 'no-store' });
  if (!response.ok) throw new Error('Host request failed');
  return response.blob();
}

export type AgentProfile = { id: string; name: string; role: string; portraitId: number; customPortrait?: string | null; accent: string; instructions: string; defaultModelId?: string | null; revision: number; isArchived: boolean };
export type GuildhallBehavior = { instructions: string; revision: number };
export type Conversation = { id: string; title: string; selectedModelId?: string | null; revision: number; isArchived?: boolean; archivedUtc?: string | null; hasActiveRun?: boolean; agentDefinitionId?: string };
export type Attachment = { id: string; fileName: string; contentType: string; length: number };
export type ConversationDetails = { conversation: Conversation; agent?: AgentProfile; messages: { id: string; runId?: string; role: string; text: string; status: string; turnNumber: number; modelId?: string; errorCode?: string | null; attachments: Attachment[] }[] };

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

