import type { AgentProfile, MessageView } from './api';

export function formatAttachmentSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return '0 KB';
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export function formatWorkedDuration(seconds?: number | null): string | null {
  if (seconds == null || !Number.isFinite(seconds) || seconds < 0) return null;
  if (seconds < 1) return `Worked for ${Math.round(seconds * 1000)}ms`;
  const formatted = seconds < 10 ? seconds.toFixed(1).replace(/\.0$/, '') : String(Math.round(seconds));
  return `Worked for ${formatted}s`;
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

export function waitingMessages(name: string, model?: string | null): string[] {
  const member = name.trim() || 'The guild member';
  const selectedModel = model?.trim();
  return [
    `${member}'s reply is in progress`,
    selectedModel ? `Waiting for ${selectedModel} to return the first text` : 'Waiting for the model to return the first text',
    'The response will appear here as it arrives',
    'No response text has arrived yet',
    'You can keep browsing while this reply runs',
    'Guildhall is waiting for the next response update',
  ];
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

export interface EffortVariant {
  effort: string;
  label: string;
  modelId: string;
}

export interface GroupedModelOption {
  baseKey: string;
  displayName: string;
  defaultModelId: string;
  variants: EffortVariant[];
}

const EFFORT_ORDER: Record<string, number> = { low: 0, medium: 1, high: 2, xhigh: 3 };
const EFFORT_LABELS: Record<string, string> = { low: 'Low', medium: 'Medium', high: 'High', xhigh: 'XHigh' };
const EFFORT_SUFFIX_RE = /^(.+?)-(low|medium|high|xhigh)$/i;
const EFFORT_PAREN_RE = /\s*\((Low|Medium|High|XHigh)\)\s*$/i;

export function groupModelsByEffort(models: { id: string; displayName: string }[]): GroupedModelOption[] {
  const groups = new Map<string, GroupedModelOption>();

  for (const model of models) {
    const idMatch = EFFORT_SUFFIX_RE.exec(model.id);
    const nameMatch = EFFORT_PAREN_RE.exec(model.displayName);
    if (idMatch && nameMatch) {
      const baseKey = idMatch[1];
      const effort = idMatch[2].toLowerCase();
      const baseName = model.displayName.replace(EFFORT_PAREN_RE, '').trim();
      const existing = groups.get(baseKey);
      const variant: EffortVariant = { effort, label: EFFORT_LABELS[effort] ?? effort, modelId: model.id };
      if (existing) {
        if (!existing.variants.some(v => v.effort === effort))
          existing.variants.push(variant);
      } else {
        groups.set(baseKey, { baseKey, displayName: baseName, defaultModelId: model.id, variants: [variant] });
      }
    } else {
      groups.set(model.id, {
        baseKey: model.id,
        displayName: model.displayName,
        defaultModelId: model.id,
        variants: [],
      });
    }
  }

  for (const group of groups.values()) {
    if (group.variants.length > 0) {
      group.variants.sort((a, b) => (EFFORT_ORDER[a.effort] ?? 99) - (EFFORT_ORDER[b.effort] ?? 99));
      const medium = group.variants.find(v => v.effort === 'medium');
      group.defaultModelId = (medium ?? group.variants[0]).modelId;
    }
  }

  return Array.from(groups.values());
}
