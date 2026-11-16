import type { AgentProfile, MessageView } from './api';

export function formatResponseDuration(seconds?: number | null): string | null {
  if (seconds == null || !Number.isFinite(seconds) || seconds < 0) return null;
  if (seconds < 1) return `Response time: ${Math.round(seconds * 1000)} ms`;
  return `Response time: ${seconds < 10 ? seconds.toFixed(1) : Math.round(seconds)} s`;
}

export function starterSuggestions(profile?: Pick<AgentProfile, 'role' | 'instructions'>): string[] {
  const context = `${profile?.role ?? ''} ${profile?.instructions ?? ''}`.toLowerCase();
  if (/code|software|develop|program|debug|engineer/.test(context)) return ['Help me debug a problem', 'Review this code', 'Plan a small implementation'];
  if (/write|edit|story|copy|editor|author/.test(context)) return ['Help me shape an idea', 'Review this draft', 'Find a clearer way to say this'];
  if (/research|analy|data|science|investigat/.test(context)) return ['Break down this question', 'Compare the evidence', 'Suggest a research plan'];
  if (/design|visual|product|ux/.test(context)) return ['Explore a design direction', 'Review this experience', 'Map the user flow'];
  if (/jester|meme|image|illustrat|artist/.test(context)) return ['Draft an image prompt for this idea', 'Turn this into a playful meme', 'Describe the visual direction'];
  return ['Explore an idea', 'Work through a question', 'Plan a next step'];
}

export function queuedPairs(messages: MessageView[]): Set<number> {
  return new Set(messages.filter(message => message.role === 'assistant' && message.status === 'Queued').map(message => message.turnNumber));
}

export function isUnread(lastResponseUtc: string | null | undefined, watermark: string | undefined): boolean {
  if (!lastResponseUtc || !Number.isFinite(Date.parse(lastResponseUtc))) return false;
  return !watermark || Date.parse(lastResponseUtc) > Date.parse(watermark);
}

const READ_KEY = 'guildhall.readWatermarks.v1';
export function readWatermarks(): Record<string, string> {
  try {
    const value: unknown = JSON.parse(localStorage.getItem(READ_KEY) ?? '{}');
    if (!value || typeof value !== 'object' || Array.isArray(value)) return {};
    return Object.fromEntries(Object.entries(value).filter(([id, timestamp]) => typeof id === 'string' && typeof timestamp === 'string' && Number.isFinite(Date.parse(timestamp))));
  } catch { return {}; }
}
export function markConversationRead(id: string, lastResponseUtc?: string | null): void {
  if (!lastResponseUtc || !Number.isFinite(Date.parse(lastResponseUtc))) return;
  try { localStorage.setItem(READ_KEY, JSON.stringify({ ...readWatermarks(), [id]: lastResponseUtc })); } catch { /* Read state stays usable for this view. */ }
}
