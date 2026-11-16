import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { PaneResizeHandle } from './PaneResizeHandle';

describe('PaneResizeHandle', () => {
  it('drags in the pane direction and clamps to its bounds', () => {
    const onResize = vi.fn();
    render(<PaneResizeHandle label="Resize council" controls="council-pane" value={280} min={220} max={420} direction={1} className="council-resizer" onResize={onResize} />);
    const separator = screen.getByRole('separator', { name: 'Resize council' });
    fireEvent.pointerDown(separator, { pointerId: 1, pointerType: 'mouse', button: 0, clientX: 100 });
    fireEvent.pointerMove(separator, { pointerId: 1, clientX: 135 });
    expect(onResize).toHaveBeenLastCalledWith(315);
    fireEvent.pointerMove(separator, { pointerId: 1, clientX: 500 });
    expect(onResize).toHaveBeenLastCalledWith(420);
    fireEvent.pointerUp(separator, { pointerId: 1 });
  });

  it('supports touch dragging on a right-side pane', () => {
    const onResize = vi.fn();
    render(<PaneResizeHandle label="Resize Guild Member" controls="guild-member-pane" value={320} min={240} max={520} direction={-1} className="member-resizer" onResize={onResize} />);
    const separator = screen.getByRole('separator', { name: 'Resize Guild Member' });
    fireEvent.pointerDown(separator, { pointerId: 2, pointerType: 'touch', clientX: 200 });
    fireEvent.pointerMove(separator, { pointerId: 2, clientX: 170 });
    expect(onResize).toHaveBeenLastCalledWith(350);
    fireEvent.pointerUp(separator, { pointerId: 2 });
  });

  it('supports keyboard sizing and Home/End bounds for right-side panes', () => {
    const onResize = vi.fn();
    render(<PaneResizeHandle label="Resize Session Tools" controls="session-tools-pane" value={400} min={240} max={600} direction={-1} className="tools-resizer" onResize={onResize} />);
    const separator = screen.getByRole('separator', { name: 'Resize Session Tools' });
    expect(separator).toHaveAttribute('aria-valuenow', '400');
    fireEvent.keyDown(separator, { key: 'ArrowLeft' });
    expect(onResize).toHaveBeenLastCalledWith(410);
    fireEvent.keyDown(separator, { key: 'ArrowRight', shiftKey: true });
    expect(onResize).toHaveBeenLastCalledWith(360);
    fireEvent.keyDown(separator, { key: 'Home' });
    expect(onResize).toHaveBeenLastCalledWith(240);
    fireEvent.keyDown(separator, { key: 'End' });
    expect(onResize).toHaveBeenLastCalledWith(600);
  });
});
