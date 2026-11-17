import { useEffect, useState } from 'react';
import { request, type Bootstrap, type WorktreeDiff, type WorktreeMergeResult } from './api';

export function WorktreeDiffDrawer({
  config,
  conversationId,
  branchName,
  onWorktreeResolved,
}: {
  config: Bootstrap | null;
  conversationId: string;
  branchName?: string | null;
  onClose?: () => void;
  onWorktreeResolved: (message: string) => void;
}) {
  const [diff, setDiff] = useState<WorktreeDiff | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [commitMessage, setCommitMessage] = useState('');
  const [confirmDiscard, setConfirmDiscard] = useState(false);

  async function loadDiff(signal?: AbortSignal) {
    if (!config) return;
    setLoading(true);
    setError('');
    try {
      const data = await request(config, `/conversations/${conversationId}/worktree/diff`, signal ?? new AbortController().signal) as WorktreeDiff;
      setDiff(data);
    } catch {
      setError('Could not load worktree diff.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    const ac = new AbortController();
    void loadDiff(ac.signal);
    return () => ac.abort();
  }, [config, conversationId]);

  async function handleMerge() {
    if (!config || busy) return;
    setBusy(true);
    setError('');
    try {
      const res = await request(config, `/conversations/${conversationId}/worktree/merge`, new AbortController().signal, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ commitMessage: commitMessage.trim() || null }),
      }) as WorktreeMergeResult;
      onWorktreeResolved(res.message);
    } catch {
      setError('Merge failed or conflicted. Resolve conflicts or discard the worktree.');
    } finally {
      setBusy(false);
    }
  }

  async function handleDiscard() {
    if (!config || busy) return;
    setBusy(true);
    setError('');
    try {
      await request(config, `/conversations/${conversationId}/worktree/discard`, new AbortController().signal, {
        method: 'POST',
      });
      onWorktreeResolved('Worktree discarded.');
    } catch {
      setError('Could not discard worktree.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="diff-drawer" aria-label="Worktree diff review">
      <div className="toolbar">
        <div>
          <h3>Worktree diff review</h3>
          <small>{branchName ?? diff?.branchName ?? 'Isolated branch'}</small>
        </div>
      </div>
      <div className="diff-drawer-actions">
        <button type="button" disabled={loading || busy} onClick={() => void loadDiff()}>Refresh</button>
        <button type="button" disabled={loading || busy || !diff?.hasChanges} onClick={() => void handleMerge()}>
          Approve &amp; Merge
        </button>
        {confirmDiscard ? (
          <>
            <button type="button" className="danger-button" disabled={loading || busy} onClick={() => void handleDiscard()}>
              Confirm Discard
            </button>
            <button type="button" disabled={loading || busy} onClick={() => setConfirmDiscard(false)}>
              Cancel
            </button>
          </>
        ) : (
          <button type="button" className="danger-button" disabled={loading || busy} onClick={() => setConfirmDiscard(true)}>
            Discard Worktree
          </button>
        )}
      </div>
      <label className="diff-commit-label">
        Merge commit message (optional)
        <input
          value={commitMessage}
          onChange={e => setCommitMessage(e.target.value)}
          placeholder="Guildhall: apply approved worktree changes"
          disabled={busy}
        />
      </label>
      {error && <p role="alert" className="profile-error">{error}</p>}
      {loading && <p><small>Computing diff…</small></p>}
      {!loading && diff && (
        <>
          <div className="diff-files-summary">
            <small>
              {diff.changedFiles.length === 0
                ? 'No modified files in this worktree yet.'
                : `${diff.changedFiles.length} changed file${diff.changedFiles.length === 1 ? '' : 's'}: ${diff.changedFiles.join(', ')}`}
            </small>
          </div>
          {diff.diffText ? (
            <pre className="diff-code-block" aria-label="Unified diff">
              {diff.diffText.split('\n').map((line, idx) => {
                const cls = line.startsWith('+') && !line.startsWith('+++')
                  ? 'diff-line-add'
                  : line.startsWith('-') && !line.startsWith('---')
                    ? 'diff-line-del'
                    : line.startsWith('@@')
                      ? 'diff-line-hunk'
                      : '';
                return <div key={idx} className={cls}>{line || ' '}</div>;
              })}
            </pre>
          ) : (
            <p><small>No line diff to display.</small></p>
          )}
        </>
      )}
    </section>
  );
}
