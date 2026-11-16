import { beforeEach, describe, expect, it } from 'vitest';
import { formatResponseDuration, isUnread, markConversationRead, queuedPairs, readWatermarks, starterSuggestions } from './chatPresentation';

beforeEach(() => localStorage.clear());

describe('chat presentation helpers', () => {
  it('formats only finite non-negative response times', () => {
    expect(formatResponseDuration(0.234)).toBe('Response time: 234 ms');
    expect(formatResponseDuration(1.26)).toBe('Response time: 1.3 s');
    expect(formatResponseDuration(null)).toBeNull();
    expect(formatResponseDuration(-1)).toBeNull();
    expect(formatResponseDuration(Number.NaN)).toBeNull();
  });

  it('chooses deterministic starter prompts from member role and instructions', () => {
    expect(starterSuggestions({ role: 'Engineer', instructions: 'Review code and debug problems' })).toContain('Review this code');
    expect(starterSuggestions({ role: 'Jester', instructions: 'Create playful memes and images' })).toContain('Turn this into a playful meme');
    expect(starterSuggestions({ role: 'Steward', instructions: 'General help' })).toEqual(['Explore an idea', 'Work through a question', 'Plan a next step']);
  });

  it('identifies only user turns paired with queued assistant entries', () => {
    expect([...queuedPairs([
      { id: 'u1', role: 'user', text: 'done', status: 'Completed', turnNumber: 1, attachments: [] },
      { id: 'a1', role: 'assistant', text: '', status: 'Completed', turnNumber: 1, attachments: [] },
      { id: 'u2', role: 'user', text: 'pending', status: 'Completed', turnNumber: 2, attachments: [] },
      { id: 'a2', role: 'assistant', text: '', status: 'Queued', turnNumber: 2, attachments: [] },
    ])]).toEqual([2]);
  });

  it('persists only valid per-conversation response timestamps and compares unread state', () => {
    markConversationRead('one', '2026-11-16T12:00:00Z');
    expect(readWatermarks()).toEqual({ one: '2026-11-16T12:00:00Z' });
    expect(isUnread('2026-11-16T12:00:01Z', readWatermarks().one)).toBe(true);
    expect(isUnread('2026-11-16T12:00:00Z', readWatermarks().one)).toBe(false);
    localStorage.setItem('guildhall.readWatermarks.v1', '{"x":3,"bad":"nope"}');
    expect(readWatermarks()).toEqual({});
  });
});
