import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { ContextInspector } from './ContextInspector';
import type { Bootstrap, ContextInspectorData } from './api';

const config: Bootstrap = {
  hostUrl: 'http://127.0.0.1:43120',
  token: '0123456789abcdef0123456789abcdef',
};

afterEach(() => {
  vi.restoreAllMocks();
});

it('displays live VRAM residency, cross-turn budget, and scoped memory sources', async () => {
  const payload: ContextInspectorData = {
    conversationId: 'c1',
    agentDefinitionId: 'm1',
    memberName: 'Maren',
    modelId: 'qwen3:1.7b',
    behaviorRevision: 2,
    behaviorChars: 420,
    globalGuidelinesHash: 'abcd1234ef56',
    globalGuidelinesChars: 310,
    memberInstructionsChars: 95,
    memoryNotes: [
      { id: 'n1', scope: 'AboutYou', revision: 1, estimatedTokens: 12, preview: 'Prefer concise bullets.' },
      { id: 'n2', scope: 'Member', revision: 3, estimatedTokens: 18, preview: 'Check SQL grants.' },
    ],
    retainedHistoryTurns: 2,
    retainedHistoryChars: 400,
    budget: {
      estimatedSessionTokens: 320,
      maxSessionTokens: 32768,
      estimatedNextTurnTokens: 540,
      maxTurnContextTokens: 4096,
      completedTurns: 2,
      isOverTurnLimit: false,
      isOverSessionBudget: false,
    },
    vram: {
      providerId: 'ollama',
      status: 'loaded',
      useGpu: true,
      keepAliveSeconds: 900,
      models: [{ name: 'qwen3:1.7b', sizeBytes: 1_500_000_000, sizeVramBytes: 1_200_000_000, contextLength: 4096 }],
    },
  };

  vi.spyOn(globalThis, 'fetch').mockResolvedValue(
    new Response(JSON.stringify(payload), { status: 200, headers: { 'Content-Type': 'application/json' } }),
  );

  render(<ContextInspector config={config} conversationId="c1" />);

  await waitFor(() => expect(screen.getByText('qwen3:1.7b')).toBeInTheDocument());
  expect(screen.getByText(/GPU offload/)).toBeInTheDocument();
  expect(screen.getByText(/keep_alive 900s/)).toBeInTheDocument();
  expect(screen.getByText(/~540 \/ 4,096 tokens/)).toBeInTheDocument();
  expect(screen.getByText(/~320 \/ 32,768 tokens/)).toBeInTheDocument();
  expect(screen.getByText('Prefer concise bullets.')).toBeInTheDocument();
  expect(screen.getByText('Check SQL grants.')).toBeInTheDocument();
});
