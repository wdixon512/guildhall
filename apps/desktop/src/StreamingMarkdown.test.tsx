import { act, render } from '@testing-library/react';
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
