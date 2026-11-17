import { useEffect, useState } from 'react';
import { request } from './api';
import type { Bootstrap, MemoryNote, MemoryNoteScope } from './api';
import { Icon } from './Icon';
import { TextEditorModal } from './TextEditorModal';

export function MemoryNotesPanel({
  config,
  scope,
  agentDefinitionId,
  projectId,
  title,
  description,
  refreshKey = 0,
}: {
  config: Bootstrap | null;
  scope: MemoryNoteScope;
  agentDefinitionId?: string;
  projectId?: string;
  title: string;
  description: string;
  refreshKey?: number;
}) {
  const [notes, setNotes] = useState<MemoryNote[]>([]);
  const [query, setQuery] = useState('');
  const [draft, setDraft] = useState('');
  const [loading, setLoading] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [editingNote, setEditingNote] = useState<MemoryNote | null>(null);
  const [expandDraft, setExpandDraft] = useState(false);
  const [modalError, setModalError] = useState('');
  const [error, setError] = useState('');

  const canLoad = !!config && (scope === 'AboutYou' || (scope === 'Member' && !!agentDefinitionId) || (scope === 'Project' && !!projectId));

  async function loadNotes(search = query, signal?: AbortSignal) {
    if (!canLoad || !config) return;
    setLoading(true);
    setError('');
    try {
      const params = new URLSearchParams({ scope });
      if (scope === 'Member' && agentDefinitionId) params.set('agentDefinitionId', agentDefinitionId);
      if (scope === 'Project' && projectId) params.set('projectId', projectId);
      if (search.trim()) params.set('query', search.trim());
      const items = await request(config, `/memory-notes?${params.toString()}`, signal ?? new AbortController().signal) as MemoryNote[];
      if (!signal?.aborted) setNotes(Array.isArray(items) ? items : []);
    } catch (reason) {
      if (reason instanceof DOMException && reason.name === 'AbortError') return;
      if (!signal?.aborted) setError('Could not load memory notes. Check the host and database migration.');
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  }

  useEffect(() => {
    setEditingNote(null);
    setModalError('');
    if (!canLoad) {
      setNotes([]);
      return;
    }
    const controller = new AbortController();
    void loadNotes(query, controller.signal);
    return () => controller.abort();
  }, [config?.hostUrl, config?.token, scope, agentDefinitionId, projectId, query, refreshKey]);

  async function addNote() {
    if (!config || !canLoad || creating) return;
    const trimmed = draft.trim();
    if (!trimmed || trimmed.length > 2000) return;
    setCreating(true);
    setError('');
    try {
      const created = await request(config, '/memory-notes', new AbortController().signal, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          scope,
          agentDefinitionId: scope === 'Member' ? agentDefinitionId : null,
          projectId: scope === 'Project' ? projectId : null,
          body: trimmed,
          isEnabled: true,
        }),
      }) as MemoryNote;
      setDraft('');
      setNotes(current => [created, ...current]);
    } catch {
      setError('Could not save memory note. Your draft is still here.');
    } finally {
      setCreating(false);
    }
  }

  async function toggleEnabled(note: MemoryNote) {
    if (!config || busyId) return;
    setBusyId(note.id);
    setError('');
    try {
      const updated = await request(config, `/memory-notes/${note.id}`, new AbortController().signal, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ isEnabled: !note.isEnabled, expectedRevision: note.revision }),
      }) as MemoryNote;
      setNotes(current => current.map(item => (item.id === updated.id ? updated : item)));
    } catch {
      setError('Could not update note state. Reload and try again.');
    } finally {
      setBusyId(null);
    }
  }

  async function saveEdit(note: MemoryNote, nextBody: string) {
    if (!config || busyId) return;
    const trimmed = nextBody.trim();
    if (!trimmed || trimmed.length > 2000) return;
    setBusyId(note.id);
    setModalError('');
    setError('');
    try {
      const updated = await request(config, `/memory-notes/${note.id}`, new AbortController().signal, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ body: trimmed, expectedRevision: note.revision }),
      }) as MemoryNote;
      setNotes(current => current.map(item => (item.id === updated.id ? updated : item)));
      setEditingNote(null);
    } catch {
      setModalError('Could not save note edit. Reload and try again.');
    } finally {
      setBusyId(null);
    }
  }

  async function forgetNote(note: MemoryNote) {
    if (!config || busyId) return;
    setBusyId(note.id);
    setError('');
    try {
      await request(config, `/memory-notes/${note.id}?expectedRevision=${note.revision}`, new AbortController().signal, {
        method: 'DELETE',
      });
      setNotes(current => current.filter(item => item.id !== note.id));
    } catch {
      setError('Could not forget note. Reload and try again.');
    } finally {
      setBusyId(null);
    }
  }

  return (
    <section className="memory-panel" aria-label={title}>
      <div className="memory-panel-header">
        <h3>{title}</h3>
        <small>{description}</small>
      </div>
      <div className="memory-add">
        <textarea
          aria-label={`New ${title.toLowerCase()} note`}
          placeholder={
            scope === 'AboutYou'
              ? 'Add a shared preference or fact for all members…'
              : scope === 'Project'
                ? 'Add a note for this project…'
                : 'Add a private note for this member…'
          }
          maxLength={2000}
          rows={3}
          value={draft}
          disabled={!canLoad || creating}
          onChange={event => setDraft(event.target.value)}
        />
        <div className="memory-add-footer">
          <small>{draft.trim().length}/2000</small>
          <div className="memory-add-buttons">
            <button type="button" className="icon-action-button" aria-label="Expand draft" title="Expand draft" onClick={() => setExpandDraft(true)} disabled={!canLoad || creating}>
              <Icon name="expand" size={14} />
            </button>
            <button type="button" onClick={() => void addNote()} disabled={!canLoad || creating || !draft.trim() || draft.trim().length > 2000}>
              {creating ? 'Saving…' : 'Add note'}
            </button>
          </div>
        </div>
      </div>
      <input
        type="search"
        className="memory-search"
        aria-label={`Search ${title.toLowerCase()}`}
        placeholder="Search notes…"
        value={query}
        disabled={!canLoad}
        onChange={event => setQuery(event.target.value)}
      />
      {error && <p className="settings-error" role="alert">{error}</p>}
      {loading && <p className="memory-status">Loading notes…</p>}
      {!loading && !notes.length && <p className="memory-status">{query.trim() ? 'No matching notes.' : 'No notes saved yet.'}</p>}
      <ul className="memory-list">
        {notes.map(note => (
          <li key={note.id} className={`memory-item ${note.isEnabled ? '' : 'disabled'}`}>
            <p className="memory-body memory-body-clamped" title={note.body}>{note.body}</p>
            <div className="memory-meta">
              <span>{note.isEnabled ? 'Enabled' : 'Disabled'} · rev {note.revision}</span>
              {note.sourceConversationId && <span className="memory-source">From session</span>}
            </div>
            <div className="memory-item-actions">
              <button
                type="button"
                className="icon-action-button"
                aria-label={note.isEnabled ? 'Disable' : 'Enable'}
                title={note.isEnabled ? 'Disable note' : 'Enable note'}
                onClick={() => void toggleEnabled(note)}
                disabled={!!busyId}
              >
                <Icon name={note.isEnabled ? 'eye' : 'eyeOff'} size={14} />
              </button>
              <button type="button" className="icon-action-button" aria-label={`Edit note: ${note.body.slice(0, 40)}`} title="Edit note" onClick={() => { setModalError(''); setEditingNote(note); }} disabled={!!busyId}>
                <Icon name="edit" size={14} />
              </button>
              <button type="button" className="icon-action-button memory-forget" aria-label={`Forget note: ${note.body.slice(0, 40)}`} title="Forget note" onClick={() => void forgetNote(note)} disabled={!!busyId}>
                <Icon name="trash" size={14} />
              </button>
            </div>
          </li>
        ))}
      </ul>
      <TextEditorModal
        open={!!editingNote}
        title={`Edit ${title.toLowerCase()} note`}
        subtitle={editingNote?.sourceConversationId ? 'Saved from a chat session · changes apply to future replies.' : 'Changes apply to future replies.'}
        value={editingNote?.body ?? ''}
        maxLength={2000}
        saving={!!busyId}
        error={modalError}
        onSave={nextBody => editingNote ? saveEdit(editingNote, nextBody) : undefined}
        onClose={() => { setEditingNote(null); setModalError(''); }}
      />
      <TextEditorModal
        open={expandDraft}
        title={`New ${title.toLowerCase()} note`}
        subtitle={
          scope === 'AboutYou'
            ? 'Shared with every guild member.'
            : scope === 'Project'
              ? 'Shared across Council Members for this project.'
              : 'Private to this guild member.'
        }
        value={draft}
        maxLength={2000}
        saveLabel="Apply"
        onSave={nextDraft => { setDraft(nextDraft); setExpandDraft(false); }}
        onClose={() => setExpandDraft(false)}
      />
    </section>
  );
}
