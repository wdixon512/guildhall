import { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { StreamingMarkdown } from './StreamingMarkdown';
import { splitThinking } from './thinking';
import { PaneResizeHandle } from './PaneResizeHandle';

export function AssistantResponse({ text, streaming }: { text: string; streaming: boolean }) {
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
    if (open) dialog.current?.showModal();
  }, [open]);
  function close() { dialog.current?.close(); setOpen(false); trigger.current?.focus(); }
  return <>
    {parts.marked && <button ref={trigger} className="thinking-toggle" aria-haspopup="dialog" aria-expanded={open} onClick={() => setOpen(true)}>{streaming && parts.pending ? 'Thinking…' : 'View thinking'} <span aria-hidden="true">↗</span></button>}
    {parts.answer ? <StreamingMarkdown text={parts.answer} streaming={streaming} /> : parts.marked && <p className="thinking-status">{streaming ? 'Waiting for the answer…' : 'No answer text was returned.'}</p>}
    {open && createPortal(<dialog ref={dialog} className="thinking-pane" aria-label="Model thinking" style={{ width: Math.min(width, max) }} onCancel={event => { event.preventDefault(); close(); }} onClick={event => { if (event.target === dialog.current) close(); }}>
      <PaneResizeHandle className="thinking-resizer" label="Resize thinking pane" controls="thinking-content" value={Math.min(width, max)} min={Math.min(240, max)} max={max} direction={-1} onResize={setWidth} />
      <header><h2>Model thinking</h2><button autoFocus aria-label="Close thinking pane" onClick={close}>×</button></header>
      <p className="thinking-note">Model-generated reasoning may contain mistakes. {streaming && parts.pending ? 'Still receiving thinking.' : ''}</p>
      <div id="thinking-content" className="thinking-content">{parts.thinking || 'Waiting for thinking text…'}</div>
    </dialog>, document.body)}
  </>;
}
