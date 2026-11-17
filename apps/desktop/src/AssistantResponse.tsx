import { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { StreamingMarkdown } from './StreamingMarkdown';
import { splitThinking, summarizeThinking } from './thinking';
import { formatWorkedDuration } from './chatPresentation';
import { PaneResizeHandle } from './PaneResizeHandle';

export function AssistantResponse({ text, streaming, durationSeconds }: { text: string; streaming: boolean; durationSeconds?: number | null }) {
  const parts = splitThinking(text, streaming);
  const [open, setOpen] = useState(false);
  const [width, setWidth] = useState(440);
  const [viewport, setViewport] = useState(window.innerWidth);
  const dialog = useRef<HTMLDialogElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const max = Math.max(240, Math.min(800, viewport - 24));
  useEffect(() => {
    const resize = () => setViewport(window.innerWidth);
    window.addEventListener('resize', resize);
    return () => window.removeEventListener('resize', resize);
  }, []);
  useEffect(() => {
    if (open && dialog.current && !dialog.current.open) dialog.current.showModal();
  }, [open]);
  function close() { dialog.current?.close(); setOpen(false); trigger.current?.focus(); }
  const isThinkingLive = streaming && parts.pending;
  const workedLabel = !streaming ? formatWorkedDuration(durationSeconds) : null;
  return <>
    {isThinkingLive ? (
      <button type="button" ref={trigger} className="thinking-toggle is-thinking" aria-haspopup="dialog" aria-expanded={open} title="View live thinking" onClick={() => setOpen(true)}>
        <span className="thinking-summary-wrap"><span className="thinking-summary">{summarizeThinking(parts.thinking)}</span><span className="waiting-dots" aria-hidden="true"><span>.</span><span>.</span><span>.</span></span></span>
        <span className="thinking-chevron" aria-hidden="true">›</span>
      </button>
    ) : parts.marked ? (
      <button type="button" ref={trigger} className="thinking-toggle" aria-haspopup="dialog" aria-expanded={open} title={workedLabel ? `${workedLabel} — view full thinking` : 'View full thinking'} onClick={() => setOpen(true)}>
        <span className="thinking-summary">{workedLabel ?? 'View thinking'}</span>
        <span className="thinking-chevron" aria-hidden="true">›</span>
      </button>
    ) : workedLabel ? (
      <span className="thinking-toggle static" title="Includes time waiting in the queue">
        <span className="thinking-summary">{workedLabel}</span>
      </span>
    ) : null}
    {parts.answer ? <StreamingMarkdown text={parts.answer} streaming={streaming} /> : !streaming && parts.marked && <p className="thinking-status">No answer text was returned.</p>}
    {open && createPortal(<dialog ref={dialog} className="thinking-pane" aria-label="Model thinking" style={{ width: Math.min(width, max) }} onCancel={event => { event.preventDefault(); close(); }} onKeyDown={event => { if (event.key === 'Escape') event.stopPropagation(); }} onClick={event => { if (event.target === dialog.current) close(); }}>
      <PaneResizeHandle className="thinking-resizer" label="Resize thinking pane" controls="thinking-content" value={Math.min(width, max)} min={Math.min(240, max)} max={max} direction={-1} onResize={setWidth} />
      <header><h2>Model thinking</h2><button type="button" autoFocus aria-label="Close thinking pane" onClick={close}>×</button></header>
      <p className="thinking-note">Model-generated reasoning may contain mistakes. {streaming && parts.pending ? 'Still receiving thinking.' : ''}</p>
      <div id="thinking-content" className="thinking-content markdown-body">
        {parts.thinking ? <StreamingMarkdown text={parts.thinking} streaming={streaming && parts.pending} /> : <p className="thinking-empty">Waiting for thinking text…</p>}
      </div>
    </dialog>, document.body)}
  </>;
}
