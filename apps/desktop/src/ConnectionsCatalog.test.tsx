import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ConnectionsCatalog } from './ConnectionsCatalog';
import type { Bootstrap, ConnectionsCatalogData } from './api';

const config: Bootstrap = { hostUrl: 'http://127.0.0.1:43120', token: 'a'.repeat(32) };

const sampleCatalog: ConnectionsCatalogData = {
  checkedUtc: '2026-11-17T00:00:00Z',
  entries: [
    {
      id: 'ollama',
      pillar: 'Local Runtimes',
      engineKind: 'local',
      displayName: 'Ollama (Local)',
      authType: 'local_loopback',
      status: 'Connected',
      statusDetail: '1 local model(s) available',
      keyHint: null,
      defaultModelId: 'qwen3:1.7b',
      models: [{ id: 'qwen3:1.7b', displayName: 'qwen3:1.7b', supportsVision: false }],
      canConfigureApiKey: false,
      canProbe: true,
      isPlanned: false,
    },
    {
      id: 'openai-api',
      pillar: 'Cloud Model APIs',
      engineKind: 'cloud_api',
      displayName: 'OpenAI API',
      authType: 'api_key_dpapi',
      status: 'Not connected',
      statusDetail: 'Add an API key (encrypted with Windows DPAPI)',
      keyHint: null,
      defaultModelId: 'gpt-4.1-mini',
      models: [{ id: 'gpt-4.1-mini', displayName: 'GPT-4.1 mini', supportsVision: true }],
      canConfigureApiKey: true,
      canProbe: true,
      isPlanned: false,
    },
    {
      id: 'codex-cli',
      pillar: 'Local CLI Agent Harnesses',
      engineKind: 'cli_harness',
      displayName: 'OpenAI Codex CLI',
      authType: 'cli_oauth',
      status: 'Needs sign-in',
      statusDetail: 'Run codex login',
      keyHint: null,
      defaultModelId: 'codex-default',
      models: [{ id: 'codex-default', displayName: 'OpenAI Codex CLI (Default)', supportsVision: false }],
      canConfigureApiKey: false,
      canProbe: true,
      isPlanned: false,
    },
    {
      id: 'github-mcp',
      pillar: 'MCP & Service Integrations',
      engineKind: 'mcp_planned',
      displayName: 'GitHub',
      authType: 'mcp_oauth',
      status: 'Planned',
      statusDetail: 'MCP & Service Integrations (Phase 3)',
      keyHint: null,
      defaultModelId: null,
      models: [],
      canConfigureApiKey: false,
      canProbe: false,
      isPlanned: true,
    },
  ],
};

describe('ConnectionsCatalog', () => {
  it('renders all 3 pillars and planned entries honestly', () => {
    render(
      <ConnectionsCatalog
        config={config}
        catalog={sampleCatalog}
        loading={false}
        onCatalogUpdated={vi.fn()}
        onProbeAll={vi.fn()}
      />,
    );

    expect(screen.getByText('Local Runtimes')).toBeInTheDocument();
    expect(screen.getByText('Cloud Model APIs')).toBeInTheDocument();
    expect(screen.getByText('Local CLI Agent Harnesses')).toBeInTheDocument();
    expect(screen.getByText('MCP & Service Integrations')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Planned' })).toBeDisabled();
  });

  it('shows Install link button for unavailable CLI harnesses', () => {
    const unavailableCatalog: ConnectionsCatalogData = {
      ...sampleCatalog,
      entries: [
        {
          ...sampleCatalog.entries[2],
          status: 'Unavailable',
          statusDetail: "'codex' was not found on PATH.",
        },
      ],
    };

    render(
      <ConnectionsCatalog
        config={config}
        catalog={unavailableCatalog}
        loading={false}
        onCatalogUpdated={vi.fn()}
        onProbeAll={vi.fn()}
      />,
    );

    expect(screen.getByRole('button', { name: 'Install ↗' })).toBeInTheDocument();
    expect(screen.getByText('npm i -g @openai/codex')).toBeInTheDocument();
  });

  it('launches interactive sign-in for Needs sign-in CLI harnesses', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ launched: true }), { status: 202, headers: { 'Content-Type': 'application/json' } }),
    );

    render(
      <ConnectionsCatalog
        config={config}
        catalog={sampleCatalog}
        loading={false}
        onCatalogUpdated={vi.fn()}
        onProbeAll={vi.fn()}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Sign in ↗' }));
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/Opened a terminal window/i));
    fetchSpy.mockRestore();
  });

  it('saves an API key and updates the catalog entry', async () => {
    const updatedEntry = {
      ...sampleCatalog.entries[1],
      status: 'Connected' as const,
      keyHint: '••••5678',
      statusDetail: 'API key verified against provider endpoint.',
    };
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify(updatedEntry), { status: 200, headers: { 'Content-Type': 'application/json' } }),
    );
    const onCatalogUpdated = vi.fn();

    render(
      <ConnectionsCatalog
        config={config}
        catalog={sampleCatalog}
        loading={false}
        onCatalogUpdated={onCatalogUpdated}
        onProbeAll={vi.fn()}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Add API key' }));
    fireEvent.change(screen.getByLabelText('OpenAI API API key'), { target: { value: 'sk-test-12345678' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save & test' }));

    await waitFor(() => expect(onCatalogUpdated).toHaveBeenCalledTimes(1));
    expect(onCatalogUpdated.mock.calls[0][0].entries[1].status).toBe('Connected');
    fetchSpy.mockRestore();
  });
});
