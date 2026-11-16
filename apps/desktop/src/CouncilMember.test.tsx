import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, expect, it } from 'vitest';
import { CouncilMember, useMemberIdentity } from './CouncilMember';

function Editor() {
  const { identity, setIdentity, storageError } = useMemberIdentity();
  return <CouncilMember identity={identity} onChange={setIdentity} storageError={storageError} onClose={() => {}} />;
}
beforeEach(() => localStorage.clear());

it('keeps the chosen name, role, portrait and accent when the editor reopens', () => {
  const view = render(<Editor />);
  fireEvent.change(screen.getByLabelText('Name', { exact: true }), { target: { value: 'Elara' } });
  fireEvent.change(screen.getByLabelText('Role', { exact: true }), { target: { value: 'Scholar' } });
  fireEvent.click(screen.getByRole('button', { name: 'Use The researcher portrait' }));
  fireEvent.click(screen.getByRole('button', { name: 'Forest accent' }));
  view.unmount();
  render(<Editor />);
  expect(screen.getByLabelText('Name', { exact: true })).toHaveValue('Elara');
  expect(screen.getByLabelText('Role', { exact: true })).toHaveValue('Scholar');
  expect(screen.getByRole('button', { name: 'Use The researcher portrait' })).toHaveAttribute('aria-pressed', 'true');
  expect(screen.getByRole('button', { name: 'Forest accent' })).toHaveAttribute('aria-pressed', 'true');
});

it('rejects unsupported portrait files and recovers from malformed saved preferences', async () => {
  localStorage.setItem('guildhall.memberAppearance', '{broken');
  render(<Editor />);
  expect(screen.getByLabelText('Name', { exact: true })).toHaveValue('Maren');
  const file = new File(['<svg />'], 'unsafe.svg', { type: 'image/svg+xml' });
  fireEvent.change(screen.getByLabelText('Choose portrait image'), { target: { files: [file] } });
  await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Choose a PNG, JPEG or WebP image'));
});
