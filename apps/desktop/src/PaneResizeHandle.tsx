import { useRef } from 'react';
import type { KeyboardEvent, PointerEvent } from 'react';

type ResizeDirection = 1 | -1;

type PaneResizeHandleProps = {
  label: string;
  controls: string;
  value: number;
  min: number;
  max: number;
  direction: ResizeDirection;
  className: string;
  onResize: (value: number) => void;
};

type DragStart = { x: number; value: number };

export function PaneResizeHandle({ label, controls, value, min, max, direction, className, onResize }: PaneResizeHandleProps) {
  const dragStart = useRef<DragStart | null>(null);
  const clamp = (next: number) => Math.max(min, Math.min(max, next));

  function startDrag(event: PointerEvent<HTMLDivElement>) {
    if (event.pointerType === 'mouse' && event.button !== 0) return;
    event.preventDefault();
    dragStart.current = { x: event.clientX, value };
    try { event.currentTarget.setPointerCapture(event.pointerId); } catch { /* Pointer capture is optional in test DOMs. */ }
  }

  function moveDrag(event: PointerEvent<HTMLDivElement>) {
    if (!dragStart.current) return;
    onResize(clamp(dragStart.current.value + (event.clientX - dragStart.current.x) * direction));
  }

  function stopDrag(event: PointerEvent<HTMLDivElement>) {
    dragStart.current = null;
    try { event.currentTarget.releasePointerCapture(event.pointerId); } catch { /* Capture may already be released. */ }
  }

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const step = event.shiftKey ? 40 : 10;
    const increaseKey = direction === 1 ? 'ArrowRight' : 'ArrowLeft';
    const decreaseKey = direction === 1 ? 'ArrowLeft' : 'ArrowRight';
    let next: number;
    if (event.key === increaseKey) next = clamp(value + step);
    else if (event.key === decreaseKey) next = clamp(value - step);
    else if (event.key === 'Home') next = min;
    else if (event.key === 'End') next = max;
    else return;
    event.preventDefault();
    if (next !== value) onResize(next);
  }

  return <div
    className={`pane-resizer ${className}`}
    role="separator"
    aria-orientation="vertical"
    aria-label={label}
    aria-controls={controls}
    aria-valuemin={min}
    aria-valuemax={max}
    aria-valuenow={Math.round(value)}
    tabIndex={0}
    onPointerDown={startDrag}
    onPointerMove={moveDrag}
    onPointerUp={stopDrag}
    onPointerCancel={stopDrag}
    onLostPointerCapture={() => { dragStart.current = null; }}
    onKeyDown={handleKeyDown}
  ><span aria-hidden="true" /></div>;
}
