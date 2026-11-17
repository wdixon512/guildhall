import { beforeEach, describe, expect, it } from 'vitest';
import { formatAttachmentSize, formatWorkedDuration, isUnread, markConversationRead, queuedPairs, readWatermarks, starterSuggestions, waitingMessages } from './chatPresentation';
import { summarizeThinking } from './thinking';

beforeEach(() => localStorage.clear());

describe('chat presentation helpers', () => {
  it('formats attachment sizes in KB below 1 MB and MB at or above 1 MB', () => {
    expect(formatAttachmentSize(0)).toBe('0 KB');
    expect(formatAttachmentSize(400)).toBe('1 KB');
    expect(formatAttachmentSize(42 * 1024)).toBe('42 KB');
    expect(formatAttachmentSize(1024 * 1024 - 1)).toBe('1024 KB');
    expect(formatAttachmentSize(1024 * 1024)).toBe('1.0 MB');
    expect(formatAttachmentSize(1.5 * 1024 * 1024)).toBe('1.5 MB');
    expect(formatAttachmentSize(-5)).toBe('0 KB');
    expect(formatAttachmentSize(Number.NaN)).toBe('0 KB');
  });

  it('formats only finite non-negative worked durations', () => {
    expect(formatWorkedDuration(0.234)).toBe('Worked for 234ms');
    expect(formatWorkedDuration(1.26)).toBe('Worked for 1.3s');
    expect(formatWorkedDuration(10)).toBe('Worked for 10s');
    expect(formatWorkedDuration(null)).toBeNull();
    expect(formatWorkedDuration(-1)).toBeNull();
    expect(formatWorkedDuration(Number.NaN)).toBeNull();
  });

  it('summarizes the latest active thinking line cleanly', () => {
    expect(summarizeThinking('')).toBe('Thinking');
    expect(summarizeThinking('**Checking options**\nReading `frontend-polish-plan.md`')).toBe('Reading frontend-polish-plan.md');
  });

  it('chooses deterministic starter prompts from member role and instructions', () => {
    expect(starterSuggestions({ role: 'Engineer', instructions: 'Review code and debug problems' })).toContain('Review this code');
    expect(starterSuggestions({ role: 'Jester', instructions: 'Create playful memes and images' })).toContain('Turn this into a playful meme');
    expect(starterSuggestions({ role: 'Steward', instructions: 'General help' })).toEqual(['Explore an idea', 'Work through a question', 'Plan a next step']);
  });

  it('describes observable waiting state without claiming model thoughts', () => {
    expect(waitingMessages('April', 'qwen3:4b')).toEqual([
      "April's reply is in progress",
      'Waiting for qwen3:4b to return the first text',
      'The response will appear here as it arrives',
      'No response text has arrived yet',
      'You can keep browsing while this reply runs',
      'Guildhall is waiting for the next response update',
    ]);
    expect(waitingMessages('', null)[0]).toBe("The guild member's reply is in progress");
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
