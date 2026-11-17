import { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { AssistantResponse } from './AssistantResponse';
import { StreamingMarkdown } from './StreamingMarkdown';

export function TextEditorModal({
  open,
  title,
  subtitle,
  value,
  maxLength = 8192,
  saving = false,
  error = '',
  supportsPreview = true,
  readOnly = false,
  liveStreaming = false,
  statusBadge,
  saveLabel = 'Save',
  onSave,
  onClose,
}: {
  open: boolean;
  title: string;
  subtitle?: string;
  value: string;
  maxLength?: number;
  saving?: boolean;
  error?: string;
  supportsPreview?: boolean;
  readOnly?: boolean;
  liveStreaming?: boolean;
  statusBadge?: string;
  saveLabel?: string;
  onSave?: (nextValue: string) => void | Promise<void>;
  onClose: () => void;
}) {
  const [draft, setDraft] = useState(value);
  const [mode, setMode] = useState<'edit' | 'preview'>('edit');
  const dialogRef = useRef<HTMLFormElement>(null);
  const streamRef = useRef<HTMLDivElement>(null);
  const returnFocus = useRef<HTMLElement | null>(null);
  const wasOpen = useRef(false);
  const latestValue = useRef(value);
  latestValue.current = value;

  useEffect(() => {
    if (open && !wasOpen.current) {
      setDraft(latestValue.current);
      setMode('edit');
      returnFocus.current = document.activeElement as HTMLElement | null;
    }
    wasOpen.current = open;
    if (!open) return;
    return () => {
      const target = returnFocus.current;
      returnFocus.current = null;
      if (target?.isConnected) window.setTimeout(() => target.focus(), 0);
    };
  }, [open]);

  useEffect(() => {
    if (!open || !readOnly || !liveStreaming || !streamRef.current) return;
    streamRef.current.scrollTop = streamRef.current.scrollHeight;
  }, [open, readOnly, liveStreaming, value]);

  if (!open) return null;

  const trimmedLength = draft.trim().length;
  const invalid = !readOnly && (trimmedLength === 0 || draft.length > maxLength);

  return createPortal(
    <div className="dialog-backdrop">
      <form
        ref={dialogRef}
        className="confirm-dialog text-editor-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="text-editor-modal-title"
        onKeyDown={event => {
          if (event.key === 'Escape' && !saving) {
            event.preventDefault();
            onClose();
            return;
          }
          if (event.key !== 'Tab') return;
          const controls = dialogRef.current?.querySelectorAll<HTMLElement>(
            'button:not(:disabled), textarea:not(:disabled), input:not(:disabled), [tabindex="0"]',
          );
          if (!controls?.length) return;
          const first = controls[0];
          const last = controls[controls.length - 1];
          if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last.focus();
          } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first.focus();
          }
        }}
        onSubmit={event => {
          event.preventDefault();
          if (!readOnly && !saving && !invalid && onSave) void onSave(draft);
        }}
      >
        <div className="text-editor-modal-header">
          <div>
            <h2 id="text-editor-modal-title">{title}</h2>
            {subtitle && <small>{subtitle}</small>}
          </div>
          {!readOnly && supportsPreview && (
            <div className="member-instructions-tabs" role="group" aria-label="Modal editor view">
              <button type="button" className={mode === 'edit' ? 'selected' : ''} aria-pressed={mode === 'edit'} onClick={() => setMode('edit')}>Source</button>
              <button type="button" className={mode === 'preview' ? 'selected' : ''} aria-pressed={mode === 'preview'} onClick={() => setMode('preview')}>Preview</button>
            </div>
          )}
          {readOnly && statusBadge && (
            <span className={`status-pill ${statusBadge.toLowerCase()}`} role="status" aria-live="polite">{statusBadge}</span>
          )}
        </div>
        {readOnly ? (
          <div ref={streamRef} className="text-editor-modal-preview markdown-body text-editor-modal-stream" role="region" tabIndex={0} aria-label={`${title} output`}>
            {value.trim() ? <AssistantResponse text={value} streaming={liveStreaming} /> : <p className="member-instructions-empty">Waiting for output…</p>}
          </div>
        ) : mode === 'edit' ? (
          <textarea
            autoFocus
            aria-label={title}
            maxLength={maxLength}
            rows={14}
            value={draft}
            disabled={saving}
            onChange={event => setDraft(event.target.value)}
          />
        ) : (
          <div className="text-editor-modal-preview markdown-body" role="region" tabIndex={0} aria-label={`${title} preview`}>
            {draft.trim() ? <StreamingMarkdown text={draft} streaming={false} /> : <p className="member-instructions-empty">Nothing to preview yet.</p>}
          </div>
        )}
        <div className="text-editor-modal-footer">
          {!readOnly && <small>{draft.length}/{maxLength}</small>}
          {error && <p className="chat-error" role="alert">{error}</p>}
          <div className="text-editor-modal-actions">
            <button type="button" autoFocus={readOnly} disabled={saving} onClick={onClose}>
              {readOnly ? 'Close' : 'Cancel'}
            </button>
            {!readOnly && (
              <button type="submit" disabled={saving || invalid}>
                {saving ? 'Saving…' : saveLabel}
              </button>
            )}
          </div>
        </div>
      </form>
    </div>,
    document.body,
  );
}
