import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { HealthTiersBadge } from './HealthTiersBadge';
import * as api from './api';

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api');
  return {
    ...actual,
    requestJson: vi.fn(),
  };
});

const mockConfig: api.Bootstrap = { hostUrl: 'http://127.0.0.1:43120', token: 'a'.repeat(32) };

describe('HealthTiersBadge', () => {
  it('displays 5-tier health popover and triggers tier recovery', async () => {
    const degradedSnapshot: api.FiveTierHealthSnapshot = {
      checkedUtc: '2026-11-17T12:00:00Z',
      overallStatus: 'Degraded',
      tiers: [
        { tierId: 'tauri-ui', displayName: 'Desktop UI', status: 'Healthy', summary: 'Connected', consecutiveFailures: 0, canRecover: false },
        { tierId: 'agent-host', displayName: 'Agent Host', status: 'Healthy', summary: 'Running', consecutiveFailures: 0, canRecover: false },
        { tierId: 'sql-server', displayName: 'SQL Server', status: 'Healthy', summary: 'Ready', consecutiveFailures: 0, canRecover: true },
        { tierId: 'ollama', displayName: 'Ollama', status: 'Unavailable', summary: 'Offline', consecutiveFailures: 1, canRecover: true },
        { tierId: 'mcp-servers', displayName: 'MCP Tool Servers', status: 'Healthy', summary: 'Ready', consecutiveFailures: 0, canRecover: true },
      ],
    };

    const recoveredSnapshot: api.FiveTierHealthSnapshot = {
      ...degradedSnapshot,
      overallStatus: 'Healthy',
      tiers: degradedSnapshot.tiers.map(t => t.tierId === 'ollama' ? { ...t, status: 'Healthy', summary: '1 model(s) available', consecutiveFailures: 0 } : t),
    };

    vi.mocked(api.requestJson).mockImplementation(async (_cfg, path, method) => {
      if (path === '/health/ui-heartbeat' && method === 'POST') return null;
      if (path === '/health/tiers' && method === 'GET') return degradedSnapshot;
      if (path === '/health/tiers/ollama/recover' && method === 'POST') return recoveredSnapshot;
      return null;
    });

    render(<HealthTiersBadge config={mockConfig} hostConnected />);
    const toggle = await screen.findByRole('button', { name: /System health: Degraded/i });
    fireEvent.click(toggle);

    expect(await screen.findByText('Ollama')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));

    await waitFor(() => expect(screen.getByRole('button', { name: /System health: Healthy/i })).toBeInTheDocument());
  });
});
