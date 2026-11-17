import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { PrivateInfrastructurePanel } from './PrivateInfrastructurePanel';
import type { Bootstrap } from './api';

const config: Bootstrap = { hostUrl: 'http://127.0.0.1:43120', token: 'a'.repeat(32) };

describe('PrivateInfrastructurePanel', () => {
  it('renders SQL Server MCP profiles and private tunnel status', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation((input) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.endsWith('/mcp/sql/profiles')) {
        return Promise.resolve(new Response(JSON.stringify([
          {
            id: '11111111-1111-1111-1111-111111111111',
            name: 'home-analytics',
            server: 'localhost',
            database: 'HomeAnalytics',
            authMode: 'integrated',
            allowedSchemas: ['dbo'],
            allowedObjects: ['dbo.v_Summary'],
            maxRows: 100,
            commandTimeoutSeconds: 10,
            isEnabled: true,
            createdUtc: '2026-11-17T00:00:00Z',
            updatedUtc: '2026-11-17T00:00:00Z',
            revision: 1,
          },
        ]), { status: 200 }));
      }
      if (url.endsWith('/mcp/servers/custom')) {
        return Promise.resolve(new Response(JSON.stringify([]), { status: 200 }));
      }
      if (url.endsWith('/infrastructure/network-status')) {
        return Promise.resolve(new Response(JSON.stringify({
          wireGuardDetected: true,
          tailscaleDetected: false,
          tunnelInterfaces: [{ name: 'wg0', kind: 'WireGuard', status: 'Up', addresses: ['10.66.0.2'] }],
          portChecks: [
            { port: 1433, serviceName: 'SQL Server', posture: 'private_only', listeningAddresses: ['127.0.0.1'], summary: 'SQL Server (port 1433) is bound to loopback/private tunnel addresses only.' },
            { port: 11434, serviceName: 'Ollama', posture: 'private_only', listeningAddresses: ['127.0.0.1'], summary: 'Ollama (port 11434) is bound to loopback/private tunnel addresses only.' },
          ],
          checkedUtc: '2026-11-17T00:00:00Z',
        }), { status: 200 }));
      }
      return Promise.resolve(new Response('{}', { status: 404 }));
    });

    render(<PrivateInfrastructurePanel config={config} />);

    await waitFor(() => {
      expect(screen.getByText('home-analytics')).toBeInTheDocument();
      expect(screen.getByText(/Detected: WireGuard/i)).toBeInTheDocument();
      expect(screen.getByText(/SQL Server \(port 1433\)/i)).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: 'Add SQL profile' }));
    expect(screen.getByPlaceholderText('All user databases (except GuildhallDev)')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Add remote MCP' }));
    expect(screen.getByPlaceholderText('droplet-relay')).toBeInTheDocument();

    fetchSpy.mockRestore();
  });
});
