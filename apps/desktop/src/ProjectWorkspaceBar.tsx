import { useEffect, useState } from 'react';
import { pickWorkspaceFolder, request, type Bootstrap, type Conversation, type RegisteredProject, type WorktreeView } from './api';

export function ProjectWorkspaceBar({
  config,
  activeConversation,
  projects,
  branches = [],
  selectedBranch = '',
  onSelectBranch,
  isNewSession = false,
  isolateNewWorktree = false,
  onToggleIsolateNewWorktree,
  onProjectsChange,
  onSelectProject,
  onWorktreeCreated,
  onOpenDiffReview,
}: {
  config: Bootstrap | null;
  activeConversation: Conversation | null;
  projects: RegisteredProject[];
  branches?: string[];
  selectedBranch?: string;
  onSelectBranch?: (branch: string) => void;
  isNewSession?: boolean;
  isolateNewWorktree?: boolean;
  onToggleIsolateNewWorktree?: (value: boolean) => void;
  onProjectsChange: (next: RegisteredProject[]) => void;
  onSelectProject: (project: RegisteredProject | null) => Promise<void>;
  onWorktreeCreated: (worktree: WorktreeView) => void;
  onOpenDiffReview: () => void;
}) {
  const [manualOpen, setManualOpen] = useState(false);
  const [manualPath, setManualPath] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    setError('');
    setManualOpen(false);
  }, [activeConversation?.id]);

  if (!activeConversation) return null;

  const activeProject = projects.find(p => p.id === activeConversation.projectId) ?? null;

  async function registerAndSelect(rootPath: string) {
    if (!config || !rootPath.trim()) return;
    setBusy(true);
    setError('');
    try {
      const created = await request(config, '/projects', new AbortController().signal, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ rootPath: rootPath.trim() }),
      }) as RegisteredProject;
      const next = [created, ...projects.filter(p => p.id !== created.id)];
      onProjectsChange(next);
      await onSelectProject(created);
      setManualOpen(false);
      setManualPath('');
    } catch {
      setError('Could not register that folder. Check that the path exists on this PC.');
    } finally {
      setBusy(false);
    }
  }

  async function handleBrowse() {
    setError('');
    try {
      const picked = await pickWorkspaceFolder();
      if (picked) await registerAndSelect(picked);
    } catch {
      setManualOpen(true);
    }
  }

  async function handleCreateWorktree() {
    if (!config || !activeConversation) return;
    setBusy(true);
    setError('');
    try {
      const wt = await request(config, `/conversations/${activeConversation.id}/worktree`, new AbortController().signal, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ baseBranch: selectedBranch || null }),
      }) as WorktreeView;
      onWorktreeCreated(wt);
    } catch {
      setError('Could not create Git worktree. Ensure the repo has at least one commit.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="workspace-bar" aria-label="Project workspace">
      <label className="workspace-bar-label" htmlFor="project-select">Project:</label>
      <select
        id="project-select"
        className="workspace-project-select"
        aria-label="Registered project"
        disabled={busy || !config}
        value={activeProject?.id ?? ''}
        onChange={event => {
          const found = projects.find(p => p.id === event.target.value) ?? null;
          void onSelectProject(found);
        }}
      >
        <option value="">
          {activeConversation.workspaceRootPath && !activeProject
            ? `Custom: ${activeConversation.workspaceRootPath}`
            : 'No project selected'}
        </option>
        {projects.map(p => (
          <option key={p.id} value={p.id}>
            {p.name}{p.isGitRepository && p.defaultBranch ? ` (${p.defaultBranch})` : ''}
          </option>
        ))}
      </select>
      <button type="button" disabled={busy || !config} onClick={() => void handleBrowse()}>
        Browse…
      </button>
      {activeProject?.isGitRepository && !activeConversation.activeWorktreeId && branches.length > 0 && onSelectBranch && (
        <select
          className="workspace-project-select"
          aria-label="Base branch"
          disabled={busy || !config}
          value={selectedBranch}
          onChange={e => onSelectBranch(e.target.value)}
        >
          {branches.map(b => (
            <option key={b} value={b}>{b}</option>
          ))}
        </select>
      )}
      {activeProject?.isGitRepository && !activeConversation.activeWorktreeId && (
        isNewSession && onToggleIsolateNewWorktree ? (
          <label className="welcome-worktree-check">
            <input
              type="checkbox"
              checked={isolateNewWorktree}
              onChange={e => onToggleIsolateNewWorktree(e.target.checked)}
            />
            <span>New worktree</span>
          </label>
        ) : (
          <button type="button" disabled={busy || !config} onClick={() => void handleCreateWorktree()}>
            New worktree
          </button>
        )
      )}
      {activeConversation.activeWorktreeId && (
        <>
          <span className="worktree-badge" title={activeConversation.activeWorktreePath ?? ''}>
            Worktree: {activeConversation.activeWorktreeBranch ?? 'active'}
          </span>
          <button type="button" disabled={busy} onClick={onOpenDiffReview}>
            Review Diff
          </button>
        </>
      )}
      {manualOpen && (
        <form
          className="workspace-manual-form"
          onSubmit={e => { e.preventDefault(); void registerAndSelect(manualPath); }}
        >
          <input
            aria-label="Project folder path"
            placeholder="e.g. G:\repos\guildhall"
            value={manualPath}
            onChange={e => setManualPath(e.target.value)}
          />
          <button type="submit" disabled={busy || !manualPath.trim()}>Add</button>
        </form>
      )}
      {error && <span className="workspace-bar-error" role="alert">{error}</span>}
    </div>
  );
}
