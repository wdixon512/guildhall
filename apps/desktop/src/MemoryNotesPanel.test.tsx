import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { MemoryNotesPanel } from './MemoryNotesPanel';
import type { Bootstrap, MemoryNote } from './api';

const config: Bootstrap = {
  hostUrl: 'http://127.0.0.1:43120',
  token: '0123456789abcdef0123456789abcdef',
};

afterEach(() => {
  vi.restoreAllMocks();
});

it('supports scoped memory note CRUD and search', async () => {
  let notes: MemoryNote[] = [
    {
      id: 'n1',
      scope: 'Member',
      agentDefinitionId: 'm1',
      body: 'Use concise bullet points.',
      isEnabled: true,
      sourceConversationId: 'c1',
      createdUtc: '2026-11-16T18:00:00Z',
      updatedUtc: '2026-11-16T18:00:00Z',
      revision: 1,
    },
  ];

  vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
    const url = new URL(String(input));
    const method = init?.method ?? 'GET';
    if (method === 'GET' && url.pathname === '/memory-notes') {
      const q = (url.searchParams.get('query') ?? '').toLowerCase();
      const filtered = q ? notes.filter(x => x.body.toLowerCase().includes(q)) : notes;
      return new Response(JSON.stringify(filtered), { status: 200, headers: { 'Content-Type': 'application/json' } });
    }
    if (method === 'POST' && url.pathname === '/memory-notes') {
      const body = JSON.parse(String(init?.body ?? '{}')) as { body: string };
      const created: MemoryNote = {
        id: 'n2',
        scope: 'Member',
        agentDefinitionId: 'm1',
        body: body.body,
        isEnabled: true,
        sourceConversationId: null,
        createdUtc: '2026-11-16T18:01:00Z',
        updatedUtc: '2026-11-16T18:01:00Z',
        revision: 1,
      };
      notes = [created, ...notes];
      return new Response(JSON.stringify(created), { status: 201, headers: { 'Content-Type': 'application/json' } });
    }
    if (method === 'PATCH' && url.pathname.startsWith('/memory-notes/')) {
      const id = url.pathname.split('/').pop()!;
      const patch = JSON.parse(String(init?.body ?? '{}')) as { body?: string; isEnabled?: boolean };
      notes = notes.map(item => item.id === id ? {
        ...item,
        body: patch.body ?? item.body,
        isEnabled: patch.isEnabled ?? item.isEnabled,
        revision: item.revision + 1,
      } : item);
      const updated = notes.find(item => item.id === id)!;
      return new Response(JSON.stringify(updated), { status: 200, headers: { 'Content-Type': 'application/json' } });
    }
    if (method === 'DELETE' && url.pathname.startsWith('/memory-notes/')) {
      const id = url.pathname.split('/').pop()!;
      notes = notes.filter(item => item.id !== id);
      return new Response(null, { status: 204 });
    }
    return new Response(null, { status: 404 });
  });

  render(
    <MemoryNotesPanel
      config={config}
      scope="Member"
      agentDefinitionId="m1"
      title="Member memory"
      description="Private notes"
    />,
  );

  await waitFor(() => expect(screen.getByText('Use concise bullet points.')).toBeInTheDocument());
  expect(screen.getByText('From session')).toBeInTheDocument();

  fireEvent.change(screen.getByLabelText('New member memory note'), { target: { value: 'Verify SQL grants first.' } });
  fireEvent.click(screen.getByRole('button', { name: 'Add note' }));
  await waitFor(() => expect(screen.getByText('Verify SQL grants first.')).toBeInTheDocument());

  fireEvent.click(screen.getAllByRole('button', { name: 'Disable' })[0]);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Enable' })).toBeInTheDocument());

  fireEvent.click(screen.getAllByRole('button', { name: /^Edit note:/i })[0]);
  fireEvent.change(screen.getByRole('textbox', { name: 'Edit member memory note' }), { target: { value: 'Verify SQL grants always.' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => expect(screen.getByText('Verify SQL grants always.')).toBeInTheDocument());

  fireEvent.click(screen.getAllByRole('button', { name: /^Forget note:/i })[0]);
  await waitFor(() => expect(screen.queryByText('Verify SQL grants always.')).not.toBeInTheDocument());
});
