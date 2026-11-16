import { fireEvent, render as renderView, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import { bootstrap, request } from './api';

function render(element: React.ReactNode) {
  const view = renderView(element);
  fireEvent.click(screen.getByRole('button', { name: 'Connections' }));
  return view;
}

vi.mock('./api', async importOriginal => ({
  ...await importOriginal<typeof import('./api')>(), bootstrap: vi.fn(), request: vi.fn(),
}));
const defaultWindowWidth = window.innerWidth;
afterEach(() => Object.defineProperty(window, 'innerWidth', { configurable: true, value: defaultWindowWidth }));

function responses(database: unknown, models: unknown) {
  vi.mocked(request).mockImplementation(async (_, path) => path === '/health' ? { status: 'ok', apiVersion: 1 } : path === '/ready' ? database : models);
}
beforeEach(() => {
  vi.mocked(bootstrap).mockReset().mockResolvedValue({ hostUrl: 'http://127.0.0.1:43120', token: 'test-token' });
  vi.mocked(request).mockReset();
});

describe('connection status', () => {
  it('requires explicit confirmation before deleting a member and all of its sessions', async () => {
    const originalWidth = window.innerWidth;
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 420 });
    const profile = { id: 'member-one', name: 'Maren', role: 'Steward', portraitId: 0, accent: '#71465f', instructions: 'Rules', revision: 3, isArchived: false };
    const deleted: string[] = [];
    vi.mocked(request).mockImplementation(async (_, path, __, init) => {
      if (path === '/health') return { status: 'ok', apiVersion: 1 };
      if (path === '/ready') return { status: 'ready' };
      if (path === '/models') return { providerId: 'ollama', status: 'available', models: [{ id: 'one', displayName: 'one' }] };
      if (path === '/agents') return [profile];
      if (path.startsWith('/conversations?')) return [];
      if (path === `/agents/${profile.id}?expectedRevision=3` && init?.method === 'DELETE') { deleted.push(path); return null; }
      throw new Error(`Unexpected API request: ${path}`);
    });
    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: 'Connections' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Expand chats sidebar' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Member actions for Maren' }));
    const archiveAction = screen.getByRole('menuitem', { name: 'Archive member' });
    fireEvent.keyDown(archiveAction, { key: 'ArrowDown' });
    const deleteAction = screen.getByRole('menuitem', { name: 'Delete member and sessions' });
    expect(deleteAction).toHaveTextContent('Delete…');
    expect(deleteAction).toHaveFocus();
    fireEvent.keyDown(deleteAction, { key: 'Home' });
    expect(archiveAction).toHaveFocus();
    fireEvent.keyDown(archiveAction, { key: 'End' });
    expect(deleteAction).toHaveFocus();
    fireEvent.click(deleteAction);
    const dialog = screen.getByRole('alertdialog');
    expect(dialog).toHaveTextContent('all of their saved sessions, messages, and attachments');
    expect(deleted).toEqual([]);
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect(deleted).toEqual([]);
    const actionsButton = screen.getByRole('button', { name: 'Member actions for Maren' });
    fireEvent.click(actionsButton);
    fireEvent.click(screen.getByRole('menuitem', { name: 'Delete member and sessions' }));
    fireEvent.keyDown(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Cancel' }), { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull());
    expect(actionsButton).toHaveFocus();
    expect(screen.getByRole('button', { name: 'Collapse chats sidebar' })).toBeInTheDocument();
    fireEvent.click(actionsButton);
    fireEvent.click(screen.getByRole('menuitem', { name: 'Delete member and sessions' }));
    fireEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Delete member and sessions' }));
    await waitFor(() => expect(deleted).toEqual([`/agents/${profile.id}?expectedRevision=3`]));
    expect(screen.queryByRole('button', { name: 'Member actions for Maren' })).toBeNull();
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: originalWidth });
  });

  it('archives and restores members without hiding their saved sessions or enabling new sessions', async () => {
    let profile = { id: 'member-one', name: 'Maren', role: 'Steward', portraitId: 0, accent: '#71465f', instructions: 'Rules', revision: 1, isArchived: false };
    const session = { id: 'saved', title: 'Retained session', agentDefinitionId: profile.id, revision: 1, selectedModelId: 'one' };
    const mutations: boolean[] = [];
    vi.mocked(request).mockImplementation(async (_, path, __, init) => {
      if (path === '/health') return { status: 'ok', apiVersion: 1 };
      if (path === '/ready') return { status: 'ready' };
      if (path === '/models') return { providerId: 'ollama', status: 'available', models: [{ id: 'one', displayName: 'one' }] };
      if (path === '/agents') return [profile];
      if (path.startsWith('/conversations?')) return [session];
      if (path.endsWith('/lifecycle')) {
        const body = JSON.parse(init!.body as string); mutations.push(body.isArchived);
        profile = { ...profile, isArchived: body.isArchived, revision: profile.revision + 1 }; return profile;
      }
      return { conversation: session, agent: profile, messages: [{ id: 'answer', role: 'assistant', text: 'Preserved answer', status: 'Completed', turnNumber: 1 }] };
    });
    render(<App />);
    const member = await screen.findByRole('button', { name: 'Select Maren' });
    fireEvent.contextMenu(member);
    fireEvent.click(screen.getByRole('menuitem', { name: 'Archive member' }));
    expect(screen.getByRole('alertdialog')).toHaveTextContent('All saved sessions and messages are kept');
    fireEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Cancel' }));
    expect(mutations).toEqual([]);
    const actions = screen.getByRole('button', { name: 'Member actions for Maren' });
    fireEvent.click(actions);
    fireEvent.keyDown(screen.getByRole('menuitem', { name: 'Archive member' }), { key: 'Escape' });
    expect(actions).toHaveFocus();
    fireEvent.click(actions);
    fireEvent.click(screen.getByRole('menuitem', { name: 'Archive member' }));
    fireEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Archive member' }));
    await waitFor(() => expect(mutations).toEqual([true]));
    expect(screen.getByRole('button', { name: /Archived members \(1\)/ })).toHaveAttribute('aria-expanded', 'true');
    expect(screen.queryByRole('button', { name: 'New session with Maren' })).toBeNull();
    expect(screen.getByRole('textbox', { name: 'Message' })).toBeDisabled();
    expect(screen.getByRole('button', { name: /^Send$/ })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Select Maren' }));
    expect(screen.getByRole('button', { name: /^New session$/ })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Open Retained session' }));
    await screen.findByText('Preserved answer');
    fireEvent.click(screen.getByRole('button', { name: 'Member actions for Maren' }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Restore member' }));
    await waitFor(() => expect(mutations).toEqual([true, false]));
    expect(screen.getByRole('button', { name: 'New session with Maren' })).toBeInTheDocument();
    expect(screen.getByText('Preserved answer')).toBeInTheDocument();
  });

  it('keeps member drafts while navigating and serializes edits made during a save', async () => {
    let maren = { id: 'member-one', name: 'Maren', role: 'Steward', portraitId: 0, accent: '#71465f', instructions: 'Maren rules', revision: 1, isArchived: false };
    let tarin = { id: 'member-two', name: 'Tarin', role: 'Developer', portraitId: 1, accent: '#536078', instructions: 'Tarin rules', revision: 1, isArchived: false };
    const profilePatches: { id: string; body: Record<string, unknown> }[] = [];
    let releaseFirstSave: ((profile: typeof maren) => void) | undefined;
    vi.mocked(request).mockImplementation(async (_, path, __, init) => {
      if (path === '/health') return { status: 'ok', apiVersion: 1 };
      if (path === '/ready') return { status: 'ready' };
      if (path === '/models') return { providerId: 'ollama', status: 'available', models: [{ id: 'qwen3:1.7b', displayName: 'qwen3:1.7b' }] };
      if (path === '/agents') return [maren, tarin];
      if (path.startsWith('/conversations?')) return [];
      if (path === `/agents/${maren.id}` && init?.method === 'PATCH') {
        const body = JSON.parse(init.body as string) as Record<string, unknown>;
        profilePatches.push({ id: maren.id, body });
        const updated = { ...maren, ...body, revision: maren.revision + 1 };
        if (profilePatches.length === 1) {
          return await new Promise<typeof maren>(resolve => { releaseFirstSave = resolve; });
        }
        maren = updated;
        return updated;
      }
      throw new Error(`Unexpected API request: ${path}`);
    });

    render(<App />);
    await screen.findByRole('button', { name: 'Select Maren' });
    fireEvent.click(screen.getByRole('button', { name: 'Customize guild member' }));
    const instructions = screen.getByRole('textbox', { name: 'Instructions / rules' });
    fireEvent.change(instructions, { target: { value: 'Maren first draft' } });
    expect(screen.getByRole('button', { name: 'Select Tarin' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'New session with Maren' })).toBeEnabled();

    fireEvent.click(screen.getByRole('button', { name: 'Select Tarin' }));
    expect(instructions).toHaveValue('Tarin rules');
    fireEvent.click(screen.getByRole('button', { name: 'Select Maren' }));
    expect(instructions).toHaveValue('Maren first draft');

    await waitFor(() => expect(profilePatches).toHaveLength(1), { timeout: 1500 });
    expect(screen.getByRole('button', { name: 'Select Tarin' })).toBeEnabled();
    fireEvent.change(instructions, { target: { value: 'Maren newer draft' } });
    fireEvent.click(screen.getByRole('button', { name: 'Select Tarin' }));
    fireEvent.click(screen.getByRole('button', { name: 'Select Maren' }));
    expect(instructions).toHaveValue('Maren newer draft');

    releaseFirstSave?.({ ...maren, instructions: 'Maren first draft', revision: 2 });
    await waitFor(() => expect(profilePatches).toHaveLength(2), { timeout: 1500 });
    expect(profilePatches[1].body).toMatchObject({ instructions: 'Maren newer draft', expectedRevision: 2 });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Select Tarin' })).toBeEnabled());
    expect(instructions).toHaveValue('Maren newer draft');
  });

  it('keeps a failed member draft and retries it without disabling council navigation', async () => {
    let profile = { id: 'member-one', name: 'Maren', role: 'Steward', portraitId: 0, accent: '#71465f', instructions: 'Maren rules', revision: 1, isArchived: false };
    let tarin = { id: 'member-two', name: 'Tarin', role: 'Developer', portraitId: 1, accent: '#536078', instructions: 'Tarin rules', revision: 1, isArchived: false };
    let attempts = 0;
    vi.mocked(request).mockImplementation(async (_, path, __, init) => {
      if (path === '/health') return { status: 'ok', apiVersion: 1 };
      if (path === '/ready') return { status: 'ready' };
      if (path === '/models') return { providerId: 'ollama', status: 'available', models: [{ id: 'qwen3:1.7b', displayName: 'qwen3:1.7b' }] };
      if (path === '/agents') return [profile, tarin];
      if (path.startsWith('/conversations?')) return [];
      if (path === `/agents/${tarin.id}` && init?.method === 'PATCH') {
        tarin = { ...tarin, ...JSON.parse(init.body as string), revision: tarin.revision + 1 };
        return tarin;
      }
      if (path === `/agents/${profile.id}` && init?.method === 'PATCH') {
        attempts++;
        if (attempts < 3) throw new Error('revision conflict');
        profile = { ...profile, ...JSON.parse(init.body as string), revision: profile.revision + 1 };
        return profile;
      }
      throw new Error(`Unexpected API request: ${path}`);
    });

    render(<App />);
    await screen.findByRole('button', { name: 'Select Maren' });
    fireEvent.click(screen.getByRole('button', { name: 'Customize guild member' }));
    const instructions = screen.getByRole('textbox', { name: 'Instructions / rules' });
    fireEvent.change(instructions, { target: { value: 'Keep this draft after a save conflict' } });
    expect(screen.getByRole('button', { name: 'Select Tarin' })).toBeEnabled();
    await screen.findByRole('alert');
    expect(instructions).toHaveValue('Keep this draft after a save conflict');
    expect(attempts).toBe(1);

    fireEvent.click(screen.getByRole('button', { name: 'Select Tarin' }));
    const tarinInstructions = screen.getByRole('textbox', { name: 'Instructions / rules' });
    fireEvent.change(tarinInstructions, { target: { value: 'Tarin can keep editing too' } });
    fireEvent.click(screen.getByRole('button', { name: 'Select Maren' }));
    expect(instructions).toHaveValue('Keep this draft after a save conflict');
    expect(screen.getByRole('button', { name: 'Retry save' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Retry save' }));
    await waitFor(() => expect(attempts).toBe(2), { timeout: 1500 });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Retry save' })).toBeInTheDocument());
    fireEvent.change(instructions, { target: { value: 'Temporary edit' } });
    fireEvent.change(instructions, { target: { value: 'Keep this draft after a save conflict' } });
    await waitFor(() => expect(attempts).toBe(3), { timeout: 1500 });
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Retry save' })).toBeNull());
    expect(instructions).toHaveValue('Keep this draft after a save conflict');
    expect(screen.getByRole('button', { name: 'Select Tarin' })).toBeEnabled();
  });

  it('accepts queued and immediate follow-ups and model changes during generation', async () => {
    let chat = { id: 'busy-chat', title: 'Busy session', selectedModelId: 'qwen3:4b', revision: 1 };
    const messages: { id: string; runId?: string; role: string; text: string; status: string; turnNumber: number; attachments: never[] }[] = [{ id: 'active-reply', runId: 'active-run', role: 'assistant', text: '', status: 'Running', turnNumber: 1, attachments: [] }];
    const sends: string[] = [];
    const promotions: string[] = [];
    vi.mocked(request).mockImplementation(async (_, path, __, init) => {
      if (path === '/health') return { status: 'ok', apiVersion: 1 };
      if (path === '/ready') return { status: 'ready' };
      if (path === '/models') return { providerId: 'ollama', status: 'available', models: ['qwen3:4b', 'qwen3-vl:2b'].map(id => ({ id, displayName: id })) };
      if (path.startsWith('/conversations?')) return [chat];
      if (init?.method === 'PATCH') { chat = { ...chat, selectedModelId: JSON.parse(init.body as string).modelId, revision: chat.revision + 1 }; return chat; }
      if (path.endsWith('/messages') && init?.method === 'POST') {
        const body = JSON.parse(init.body as string); sends.push(body.deliveryMode);
        messages.push({ id: `user-${sends.length}`, role: 'user', text: body.text, status: 'Completed', turnNumber: sends.length + 1, attachments: [] });
        messages.push({ id: `queued-${sends.length}`, runId: `queued-run-${sends.length}`, role: 'assistant', text: '', status: 'Queued', turnNumber: sends.length + 1, attachments: [] });
        chat = { ...chat, revision: chat.revision + 1 };
        return { runId: `queued-run-${sends.length}` };
      }
      if (path.endsWith('/promote')) { promotions.push(path); return { runId: 'queued-run-1', conversationRevision: chat.revision + 1 }; }
      if (path === '/runs/queued-run-1/cancel' && init?.method === 'POST') { const queued = messages.find(message => message.runId === 'queued-run-1'); if (queued) queued.status = 'Cancelled'; return null; }
      return { conversation: chat, messages: [...messages] };
    });
    render(<App />);
    fireEvent.click(await screen.findByRole('button', { name: 'Open Busy session' }));
    const composer = screen.getByRole('textbox', { name: 'Message' });
    await screen.findByRole('button', { name: 'Stop response' });
    await waitFor(() => expect(composer).toBeEnabled());
    const model = screen.getByRole('combobox', { name: 'Model for this conversation' });
    expect(model).toBeEnabled();
    fireEvent.change(composer, { target: { value: 'A queued follow-up' } });
    fireEvent.keyDown(composer, { key: 'Enter' });
    await waitFor(() => expect(sends).toEqual(['queue']));
    const queuedPanel = screen.getByLabelText('Queued messages');
    expect(within(queuedPanel).getByText('A queued follow-up')).toBeInTheDocument();
    expect(screen.queryByText('Queued — waiting for a turn.')).toBeNull();
    fireEvent.click(within(queuedPanel).getByRole('button', { name: 'Edit queued message 2' }));
    await waitFor(() => expect(composer).toHaveValue('A queued follow-up'));
    fireEvent.change(composer, { target: { value: 'Edited queued follow-up' } });
    fireEvent.click(screen.getByRole('button', { name: 'Replace queued message' }));
    await waitFor(() => expect(sends).toEqual(['queue', 'queue']));
    expect(await within(screen.getByLabelText('Queued messages')).findByText('Edited queued follow-up')).toBeInTheDocument();
    fireEvent.click(within(screen.getByLabelText('Queued messages')).getByRole('button', { name: 'Send queued message 3 now' }));
    await waitFor(() => expect(promotions).toHaveLength(1));
    await waitFor(() => expect(model).toBeEnabled());
    fireEvent.change(model, { target: { value: 'qwen3-vl:2b' } });
    await waitFor(() => expect(model).toHaveValue('qwen3-vl:2b'));
    fireEvent.change(screen.getByRole('combobox', { name: 'Follow-up delivery' }), { target: { value: 'send_now' } });
    fireEvent.change(composer, { target: { value: 'Do this now instead' } });
    fireEvent.click(screen.getByRole('button', { name: 'Send now' }));
    await waitFor(() => expect(sends).toEqual(['queue', 'queue', 'send_now']));
  });

  it('keeps portraits on member headings and uses a neutral session icon while keeping the model current', async () => {
    const chat = { id: 'model-label', title: 'Model label check', selectedModelId: 'qwen3:4b', revision: 1 };
    vi.mocked(request).mockImplementation(async (_, path, __, init) => {
      if (path === '/health') return { status: 'ok', apiVersion: 1 };
      if (path === '/ready') return { status: 'ready' };
      if (path === '/models') return { providerId: 'ollama', status: 'available', models: ['qwen3:4b', 'qwen3-vl:2b'].map(id => ({ id, displayName: id })) };
      if (path.startsWith('/conversations?')) return [chat];
      if (init?.method === 'PATCH') return { ...chat, selectedModelId: JSON.parse(init.body as string).modelId, revision: 2 };
      return { conversation: chat, messages: [] };
    });
    render(<App />);
    const card = await screen.findByRole('button', { name: 'Open Model label check' });
    expect(within(card).queryByRole('img', { name: /portrait/ })).toBeNull();
    expect(screen.getByRole('button', { name: 'Customize guild member' }).querySelector('[role="img"]')).not.toBeNull();
    expect(within(card).getByText('qwen3:4b')).toBeInTheDocument();
    fireEvent.click(card);
    await screen.findByRole('heading', { name: 'Model label check' });
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'qwen3-vl:2b' } });
    await waitFor(() => expect(within(card).getByText('qwen3-vl:2b')).toBeInTheDocument());
  });

  it('pastes a snipping-tool image and accompanying text, supports removal, and rejects excessive files', async () => {
    vi.mocked(request).mockImplementation(async (_, path) => {
      if (path === '/agents') return [{ id: 'member-one', name: 'Maren', role: 'Steward', portraitId: 0, accent: '#71465f', instructions: 'Rules', revision: 1, isArchived: false }];
      if (path === '/health') return { status: 'ok', apiVersion: 1 };
      if (path === '/ready') return { status: 'ready' };
      if (path === '/models') return { providerId: 'ollama', status: 'available', models: [{ id: 'one', displayName: 'one' }] };
      return [];
    });
    render(<App />);
    const composer = screen.getByRole('textbox', { name: 'Message' });
    await screen.findByRole('option', { name: 'one' });
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'one' } });
    await waitFor(() => expect(composer).toBeEnabled());
    const screenshot = new File(['screenshot bytes'], 'image.png', { type: 'image/png' });
    const clipboardData = { items: [{ kind: 'file', getAsFile: () => screenshot }], files: [screenshot], getData: () => 'Explain this image' };
    fireEvent.paste(composer, { clipboardData });
    expect(screen.getByRole('button', { name: 'Remove image.png' })).toBeInTheDocument();
    expect(composer).toHaveValue('Explain this image');
    fireEvent.click(screen.getByRole('button', { name: 'Remove image.png' }));
    expect(screen.queryByRole('button', { name: 'Remove image.png' })).toBeNull();
    const tooMany = { ...clipboardData, items: Array.from({ length: 6 }, () => ({ kind: 'file', getAsFile: () => screenshot })), getData: () => '' };
    fireEvent.paste(composer, { clipboardData: tooMany });
    expect(screen.getByRole('alert')).toHaveTextContent('Attach up to five files');
    const textEvent = new Event('paste', { bubbles: true, cancelable: true });
    Object.defineProperty(textEvent, 'clipboardData', { value: { items: [], files: [], getData: () => 'plain text' } });
    fireEvent(composer, textEvent);
    expect(textEvent.defaultPrevented).toBe(false);
  });

  it('explains a model image capability failure instead of an empty saved response', async () => {
    const chat = { id: 'image-failed', title: 'Image question', selectedModelId: 'one', revision: 1 };
    vi.mocked(request).mockImplementation(async (_, path) => {
      if (path === '/health') return { status: 'ok', apiVersion: 1 };
      if (path === '/ready') return { status: 'ready' };
      if (path === '/models') return { providerId: 'ollama', status: 'available', models: [{ id: 'one', displayName: 'one' }] };
      if (path.startsWith('/conversations?')) return [chat];
      return { conversation: chat, messages: [{ id: 'failed-reply', role: 'assistant', text: '', status: 'Failed', errorCode: 'model_does_not_support_images', turnNumber: 1 }] };
    });
    render(<App />);
    fireEvent.click(await screen.findByRole('button', { name: 'Open Image question' }));
    expect(await screen.findByText(/This model cannot read images/)).toBeInTheDocument();
    expect(screen.queryByText('No response text saved.')).not.toBeInTheDocument();
  });

  it('shows appearance, access policy, and honest usage placeholders in workspace settings', async () => {
    responses({ status: 'ready' }, { providerId: 'ollama', status: 'available', models: [] });
    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: 'Workspace settings' }));
    expect(await screen.findByRole('heading', { name: 'Workspace settings' })).toBeInTheDocument();
    expect(screen.getByRole('slider', { name: 'Display scale' })).toHaveValue('100');
    expect(screen.getByRole('button', { name: 'Always ask' })).toBeDisabled();
    expect(screen.getByText(/no provider usage data is connected/i)).toBeInTheDocument();
  });

  it('preselects qwen3:1.7b and starts a conversation from the empty chat screen', async () => {
    let createdModel = '';
    let sentText = '';
    const summary = { id: 'splash-chat', title: 'New conversation', selectedModelId: 'qwen3:1.7b', revision: 1 };
    vi.mocked(request).mockImplementation(async (_, path, __, init) => {
      if (path === '/agents') return [{ id: 'member-one', name: 'Maren', role: 'Steward', portraitId: 0, accent: '#71465f', instructions: 'Rules', revision: 1, isArchived: false }];
      if (path === '/health') return { status: 'ok', apiVersion: 1 };
      if (path === '/ready') return { status: 'ready' };
      if (path === '/models') return { providerId: 'ollama', status: 'available', models: [{ id: 'qwen3:1.7b', displayName: 'qwen3:1.7b' }] };
      if (path.startsWith('/conversations?')) return [];
      if (path === '/conversations' && init?.method === 'POST') {
        createdModel = JSON.parse(init.body as string).modelId;
        return summary;
      }
      if (path === '/conversations/splash-chat/messages' && init?.method === 'POST') {
        sentText = JSON.parse(init.body as string).text;
        return { runId: 'run-one' };
      }
      if (path === '/conversations/splash-chat') return {
        conversation: summary,
        messages: [{ id: 'message-one', role: 'user', text: sentText, status: 'Completed', turnNumber: 1 }],
      };
      throw new Error(`Unexpected API request: ${path}`);
    });

    render(<App />);
    const model = await screen.findByRole('combobox');
    await waitFor(() => expect(model).toHaveValue('qwen3:1.7b'));
    const composer = screen.getByRole('textbox', { name: 'Message' });
    await waitFor(() => expect(composer).toBeEnabled());
    fireEvent.change(composer, { target: { value: 'Hello from the splash screen.' } });
    fireEvent.keyDown(composer, { key: 'Enter', code: 'Enter', charCode: 13 });

    expect(await screen.findByText('Hello from the splash screen.')).toBeInTheDocument();
    expect(createdModel).toBe('qwen3:1.7b');
    expect(sentText).toBe('Hello from the splash screen.');
  });

  it('scrolls a selected conversation to its latest message', async () => {
    const chats = ['first', 'second'].map(id => ({ id, title: id, selectedModelId: 'one', revision: 1 }));
    vi.mocked(request).mockImplementation(async (_, path) => {
      if (path === '/health') return { status: 'ok', apiVersion: 1 };
      if (path === '/ready') return { status: 'ready' };
      if (path === '/models') return { providerId: 'ollama', status: 'available', models: [{ id: 'one', displayName: 'one' }] };
      if (path.startsWith('/conversations?')) return chats;
      const selected = chats.find(chat => path === `/conversations/${chat.id}`)!;
      return {
        conversation: selected,
        messages: [{ id: `${selected.id}-message`, role: 'assistant', text: 'Latest reply', status: 'Completed', turnNumber: 1 }],
      };
    });
    render(<App />);
    const transcript = document.querySelector('.messages')!;
    Object.defineProperties(transcript, {
      scrollHeight: { configurable: true, value: 1400 },
      clientHeight: { configurable: true, value: 500 },
    });

    fireEvent.click(await screen.findByRole('button', { name: 'Open first' }));
    await screen.findByRole('heading', { name: 'first' });
    await waitFor(() => expect(transcript.scrollTop).toBe(1400));

    transcript.scrollTop = 0;
    fireEvent.click(screen.getByRole('button', { name: 'Open second' }));
    await screen.findByRole('heading', { name: 'second' });
    await waitFor(() => expect(transcript.scrollTop).toBe(1400));
  });

  it('allows leaving a running conversation to browse another chat', async () => {
    const chats = ['running', 'other'].map(id => ({ id, title: id, selectedModelId: 'one', revision: 1 }));
    vi.mocked(request).mockImplementation(async (_, path) => {
      if (path === '/health') return { status: 'ok', apiVersion: 1 };
      if (path === '/ready') return { status: 'ready' };
      if (path === '/models') return { providerId: 'ollama', status: 'available', models: [{ id: 'one', displayName: 'one' }] };
      if (path.startsWith('/conversations?')) return chats;
      const active = path.endsWith('/running');
      return { conversation: chats[active ? 0 : 1], messages: active ? [{ id: 'reply', role: 'assistant', text: '', status: 'Running', turnNumber: 1 }] : [] };
    });
    render(<App />);
    const first = await screen.findByRole('button', { name: 'Open running' });
    fireEvent.click(first);
    await screen.findByText(/reply is in progress\./);
    expect(screen.getByRole('button', { name: 'Delete running' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Archive running' })).toBeDisabled();
    const other = screen.getByRole('button', { name: 'Open other' });
    expect(other).toBeEnabled();
    fireEvent.click(other);
    await waitFor(() => expect(screen.queryByText(/reply is in progress\./)).toBeNull());
    expect(screen.getByRole('heading', { name: 'other' })).toBeInTheDocument();
  });

  it('archives, restores, and permanently deletes a conversation with confirmation', async () => {
    let archived = false;
    let deleted = false;
    const item = () => ({ id: 'chat-one', title: 'A saved chat', selectedModelId: 'one', revision: archived ? 2 : 1, isArchived: archived, hasActiveRun: false });
    vi.mocked(request).mockImplementation(async (_, path, __, init) => {
      if (path === '/health') return { status: 'ok', apiVersion: 1 };
      if (path === '/ready') return { status: 'ready' };
      if (path === '/models') return { providerId: 'ollama', status: 'available', models: [{ id: 'one', displayName: 'one' }] };
      if (path.startsWith('/conversations?')) return deleted || (path.includes('archived=true') !== archived) ? [] : [item()];
      if (path === '/conversations/chat-one' && init?.method === 'PATCH') {
        archived = JSON.parse(init.body as string).isArchived;
        return item();
      }
      if (path === '/conversations/chat-one' && init?.method === 'DELETE') { deleted = true; return null; }
      if (path === '/conversations/chat-one') return { conversation: item(), messages: [] };
      throw new Error(`Unexpected API request: ${path}`);
    });

    render(<App />);
    fireEvent.click(await screen.findByRole('button', { name: 'Archive A saved chat' }));
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Open A saved chat' })).toBeNull());
    fireEvent.click(screen.getByRole('button', { name: 'Archived' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Restore A saved chat' }));
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Open A saved chat' })).toBeNull());
    fireEvent.click(within(screen.getByRole('group', { name: 'Conversation list filter' })).getByRole('button', { name: 'Chats' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Delete A saved chat' }));
    expect(screen.getByRole('alertdialog')).toHaveTextContent(/permanently removed from Guildhall/);
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('alertdialog')).toBeNull();
    expect(screen.getByRole('button', { name: 'Open A saved chat' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Delete A saved chat' }));
    fireEvent.click(screen.getByRole('button', { name: 'Delete conversation' }));
    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull());
    expect(deleted).toBe(true);
  });
  it('creates a Qwen conversation and saves explicit switches for future sends', async () => {
    let savedModel = 'qwen3:4b';
    const metadata = () => ({ id: 'chat-one', title: 'New conversation', selectedModelId: savedModel, revision: 1 });
    vi.mocked(request).mockImplementation(async (_, path, __, init) => {
      if (path === '/health') return { status: 'ok', apiVersion: 1 };
      if (path === '/agents') return [{ id: 'member-one', name: 'Maren', role: 'Steward', portraitId: 0, accent: '#71465f', instructions: 'Rules', revision: 1, isArchived: false }];
      if (path === '/ready') return { status: 'ready' };
      if (path === '/models') return { providerId: 'ollama', status: 'available', models: ['qwen3:4b', 'smollm2:135m'].map(id => ({ id, displayName: id })) };
      if (path === '/conversations' && init?.method === 'POST') {
        expect(JSON.parse(init.body as string).modelId).toBe('qwen3:4b'); return metadata();
      }
      if (path === '/conversations/chat-one' && init?.method === 'PATCH') {
        savedModel = JSON.parse(init.body as string).modelId; return metadata();
      }
      return { conversation: metadata(), messages: [] };
    });
    render(<App />);
    const selector = await screen.findByRole('combobox');
    await waitFor(() => expect(selector).toBeEnabled());
    expect(screen.getByRole('button', { name: 'New session' })).toBeDisabled();
    fireEvent.change(selector, { target: { value: 'qwen3:4b' } });
    fireEvent.click(screen.getByRole('button', { name: 'New session' }));
    await waitFor(() => expect(request).toHaveBeenCalledWith(expect.anything(), '/conversations/chat-one', expect.anything()));
    await waitFor(() => expect(selector).toBeEnabled());
    fireEvent.change(selector, { target: { value: 'smollm2:135m' } });
    await waitFor(() => expect(selector).toHaveValue('smollm2:135m'));
    expect(savedModel).toBe('smollm2:135m');
  });
  it('shows independent readiness and safely renders local model names', async () => {
    responses({ status: 'ready' }, { providerId: 'ollama', status: 'available', models: [{ id: 'one', displayName: '<script>model</script>' }] });
    render(<App />);
    expect(await screen.findByText('Connected')).toBeInTheDocument();
    expect(await screen.findByText('Ready')).toBeInTheDocument();
    expect(await screen.findByText('<script>model</script>', { selector: 'li' })).toBeInTheDocument();
    expect(document.querySelector('script')).toBeNull();
  });
  it.each(['database_not_configured', 'database_unavailable', 'migration_required'])('keeps host connected when SQL reports %s', async reason => {
    responses({ status: 'unavailable', reason }, { providerId: 'ollama', status: 'no_models', models: [] });
    render(<App />);
    expect(await screen.findByText('No local models installed.')).toBeInTheDocument();
    expect(screen.getByText('Connected')).toBeInTheDocument();
    expect(screen.queryByText('Ready')).toBeNull();
  });
  it('reports unavailable Ollama without losing SQL readiness', async () => {
    responses({ status: 'ready' }, { providerId: 'ollama', status: 'unavailable', models: [] });
    render(<App />);
    expect(await screen.findByText(/Ollama unavailable/)).toBeInTheDocument();
    expect(screen.getByText('Ready')).toBeInTheDocument();
  });
  it('renders safe Markdown and copies message text', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    const chat = { id: 'copy-chat', title: 'Formatting check', selectedModelId: 'one', revision: 2 };
    vi.mocked(request).mockImplementation(async (_, path) => {
      if (path === '/health') return { status: 'ok', apiVersion: 1 };
      if (path === '/ready') return { status: 'ready' };
      if (path === '/models') return { providerId: 'ollama', status: 'available', models: [{ id: 'one', displayName: 'one' }] };
      if (path.startsWith('/conversations?')) return [chat];
      return { conversation: chat, messages: [
        { id: 'user-message', role: 'user', text: 'Please explain **this**.', status: 'Completed', turnNumber: 1 },
        { id: 'assistant-message', role: 'assistant', text: 'Use `safe` formatting.', status: 'Completed', turnNumber: 1, responseDurationSeconds: 54 },
      ] };
    });
    render(<App />);
    fireEvent.click(await screen.findByRole('button', { name: 'Open Formatting check' }));
    expect(await screen.findByText('this', { selector: 'strong' })).toBeInTheDocument();
    expect(screen.getByText('safe', { selector: 'code' })).toBeInTheDocument();
    const responseTime = screen.getByText('Response time: 54 s');
    expect(responseTime.nextElementSibling).toHaveClass('message-surface');
    fireEvent.click(screen.getByRole('button', { name: 'Copy assistant response' }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith('Use `safe` formatting.'));
  });
  it('uploads a local text attachment with the message and exposes session tool placeholders', async () => {
    const chat = { id: 'file-chat', title: 'New conversation', selectedModelId: 'qwen3:1.7b', revision: 1 };
    let submitted: FormData | null = null;
    vi.mocked(request).mockImplementation(async (_, path, __, init) => {
      if (path === '/agents') return [{ id: 'member-one', name: 'Maren', role: 'Steward', portraitId: 0, accent: '#71465f', instructions: 'Rules', revision: 1, isArchived: false }];
      if (path === '/health') return { status: 'ok', apiVersion: 1 };
      if (path === '/ready') return { status: 'ready' };
      if (path === '/models') return { providerId: 'ollama', status: 'available', models: [{ id: 'qwen3:1.7b', displayName: 'qwen3:1.7b' }] };
      if (path.startsWith('/conversations?')) return [];
      if (path === '/conversations' && init?.method === 'POST') return chat;
      if (path === '/conversations/file-chat/messages') { submitted = init?.body as FormData; return { runId: 'run-file' }; }
      if (path === '/conversations/file-chat') return { conversation: { ...chat, title: 'notes.txt' }, messages: [{ id: 'user-file', role: 'user', text: '', status: 'Completed', turnNumber: 1, attachments: [{ id: 'attachment-one', fileName: 'notes.txt', contentType: 'text/plain', length: 10 }] }] };
      throw new Error(`Unexpected API request: ${path}`);
    });
    render(<App />);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Add attachments' })).toBeEnabled());
    const file = new File(['local note'], 'notes.txt', { type: 'text/plain' });
    fireEvent.change(screen.getByLabelText('Choose attachments'), { target: { files: [file] } });
    expect(screen.getByText(/notes.txt/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Send' }));
    await waitFor(() => expect(submitted).toBeInstanceOf(FormData));
    expect(submitted).toBeInstanceOf(FormData);
    const submittedFile = (submitted as unknown as FormData).getAll('attachments')[0] as File;
    expect(submittedFile.name).toBe('notes.txt');
    expect(await submittedFile.text()).toBe('local note');
    expect(await screen.findByRole('button', { name: /notes.txt/ })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Session tools/ }));
    fireEvent.click(screen.getByRole('button', { name: /Context usage/ }));
    expect(screen.getByText(/context usage is planned/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Scheduled tasks — planned' })).toBeDisabled();
  });
  it('recovers after host failure using refresh', async () => {
    vi.mocked(request).mockRejectedValue(new Error('Disconnected'));
    render(<App />);
    expect(await screen.findByText(/Disconnected —/)).toBeInTheDocument();
    responses({ status: 'ready' }, { providerId: 'ollama', status: 'no_models', models: [] });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Refresh' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));
    expect(await screen.findByText('Ready')).toBeInTheDocument();
  });
  it('bounds malformed responses to their status panel', async () => {
    responses({ unexpected: true }, { providerId: 'foreign', status: 'available', models: [] });
    render(<App />);
    expect(await screen.findByText('Could not read database status.')).toBeInTheDocument();
    expect(await screen.findByText('Could not read model status.')).toBeInTheDocument();
    expect(screen.getByText('Connected')).toBeInTheDocument();
  });
  it('cancels requests on unmount', async () => {
    let signal: AbortSignal | undefined;
    vi.mocked(request).mockImplementation(async (_, __, supplied) => { signal = supplied; return new Promise(() => {}); });
    const view = render(<App />);
    await waitFor(() => expect(signal).toBeDefined());
    view.unmount();
    expect(signal?.aborted).toBe(true);
  });
});

