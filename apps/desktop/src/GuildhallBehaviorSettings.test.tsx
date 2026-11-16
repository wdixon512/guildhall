import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { GuildhallBehaviorSettings } from './GuildhallBehaviorSettings';
import { request } from './api';

vi.mock('./api', () => ({ request: vi.fn() }));
const config = { hostUrl: 'http://127.0.0.1:43120', token: 'test-token' };
const initial = { instructions: 'Be thoughtful.', revision: 3 };

beforeEach(() => vi.mocked(request).mockReset());

describe('Guildhall behavior settings', () => {
  it('loads and saves explicitly, then reports that future replies use the saved policy', async () => {
    vi.mocked(request).mockResolvedValueOnce(initial).mockResolvedValueOnce({ instructions: 'Be evidence-led.', revision: 4 });
    render(<GuildhallBehaviorSettings config={config} />);
    const editor = await screen.findByRole('textbox', { name: 'Behavior instructions' });
    expect(editor).toHaveValue(initial.instructions);
    expect(screen.getByRole('button', { name: 'Save behavior' })).toBeDisabled();
    fireEvent.change(editor, { target: { value: 'Be evidence-led.' } });
    const save = screen.getByRole('button', { name: 'Save behavior' });
    expect(save).toBeEnabled();
    fireEvent.click(save);
    await screen.findByRole('status');
    expect(screen.getByRole('status')).toHaveTextContent('New replies will use this behavior');
    expect(request).toHaveBeenLastCalledWith(config, '/guildhall-behavior', expect.any(AbortSignal), expect.objectContaining({ method: 'PUT', body: JSON.stringify({ instructions: 'Be evidence-led.', expectedRevision: 3 }) }));
    expect(screen.getByRole('button', { name: 'Save behavior' })).toBeDisabled();
  });

  it('retains the draft and offers reload after a revision conflict', async () => {
    vi.mocked(request).mockResolvedValueOnce(initial).mockRejectedValueOnce(new Error('Host request failed')).mockResolvedValueOnce({ instructions: 'Latest value.', revision: 5 });
    render(<GuildhallBehaviorSettings config={config} />);
    const editor = await screen.findByRole('textbox', { name: 'Behavior instructions' });
    fireEvent.change(editor, { target: { value: 'My draft.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save behavior' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Your draft is still here');
    expect(editor).toHaveValue('My draft.');
    fireEvent.click(screen.getByRole('button', { name: 'Reload latest version' }));
    await waitFor(() => expect(editor).toHaveValue('Latest value.'));
  });

  it('disables saving empty instructions and limits editor length', async () => {
    vi.mocked(request).mockResolvedValueOnce(initial);
    render(<GuildhallBehaviorSettings config={config} />);
    const editor = await screen.findByRole('textbox', { name: 'Behavior instructions' });
    fireEvent.change(editor, { target: { value: '   ' } });
    expect(screen.getByRole('button', { name: 'Save behavior' })).toBeDisabled();
    expect(editor).toHaveAttribute('maxLength', '4000');
  });
});
