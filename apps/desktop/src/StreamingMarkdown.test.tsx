import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { StreamingMarkdown } from './StreamingMarkdown';

afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });

it('reveals only received prefixes, settles exactly, and flushes a stopped reply', () => {
  vi.useFakeTimers();
  vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => setTimeout(() => callback(performance.now()), 16));
  vi.stubGlobal('cancelAnimationFrame', (id: number) => clearTimeout(id));
  const view = render(<StreamingMarkdown text="" streaming />);
  const text = 'This answer arrives in a single large network snapshot.';
  view.rerender(<StreamingMarkdown text={text} streaming />);
  act(() => { vi.advanceTimersByTime(160); });
  const partial = view.container.textContent!;
  expect(partial.length).toBeGreaterThan(0);
  expect(partial.length).toBeLessThan(text.length);
  expect(text.startsWith(partial)).toBe(true);
  act(() => { vi.advanceTimersByTime(300); });
  expect(view.container.textContent).toBe(text);
  view.rerender(<StreamingMarkdown text={`${text} Final words.`} streaming />);
  view.rerender(<StreamingMarkdown text={`${text} Final words.`} streaming={false} />);
  expect(view.container.textContent).toBe(`${text} Final words.`);
  act(() => { vi.advanceTimersByTime(500); });
  expect(view.container.textContent).toBe(`${text} Final words.`);
});

it('renders updates immediately when reduced motion is enabled', () => {
  vi.stubGlobal('matchMedia', () => ({ matches: true }));
  const view = render(<StreamingMarkdown text="Saved response" streaming />);
  view.rerender(<StreamingMarkdown text="Saved response with new text" streaming />);
  expect(view.container.textContent).toBe('Saved response with new text');
});

it('opens safe http/https links externally and blocks unsafe schemes', () => {
  const openSpy = vi.fn();
  vi.stubGlobal('open', openSpy);
  render(<StreamingMarkdown text={'Visit [Docs](https://example.com/docs) or [Bad](javascript:alert(1)).'} streaming={false} />);
  const safeLink = screen.getByRole('link', { name: 'Docs' });
  fireEvent.click(safeLink);
  expect(openSpy).toHaveBeenCalledWith('https://example.com/docs', '_blank', 'noopener,noreferrer');
  expect(screen.queryByRole('link', { name: 'Bad' })).toBeNull();
  expect(screen.getByText('Bad')).toHaveClass('blocked-link');
});

it('provides a top-left copy button on fenced code blocks', async () => {
  const writeText = vi.fn().mockResolvedValue(undefined);
  Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
  render(<StreamingMarkdown text={'```bash\necho hello\n```'} streaming={false} />);
  const copyButton = screen.getByRole('button', { name: 'Copy code block' });
  fireEvent.click(copyButton);
  expect(writeText).toHaveBeenCalledWith('echo hello\n');
});
