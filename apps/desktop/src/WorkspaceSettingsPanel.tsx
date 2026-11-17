import { lazy, Suspense, useEffect, useRef, useState } from 'react';
import { HostRequestError, pickWorkspaceFolder, request, type Bootstrap, type MessageView, type RegisteredProject, type WorkspaceAccessPolicy, type WorktreeSettings } from './api';

const POLICY_LABELS: Record<string, string> = {
  always_ask: 'Always ask',
  ask_unsafe: 'Ask for unsafe actions',
  unrestricted: 'Unrestricted · danger',
};

const GuildhallBehaviorSettings = lazy(() =>
  import('./GuildhallBehaviorSettings').then(module => ({ default: module.GuildhallBehaviorSettings }))
);
const MemoryNotesPanel = lazy(() =>
  import('./MemoryNotesPanel').then(module => ({ default: module.MemoryNotesPanel }))
);

export interface WorkspaceSettingsPanelProps {
  config: Bootstrap | null;
  memoryRefreshKey: number;
  uiScale: number;
  onUiScaleChange: (scale: number) => void;
  messages: MessageView[];
  activeProject?: RegisteredProject | null;
  onClose: () => void;
}

export function WorkspaceSettingsPanel({
  config,
  memoryRefreshKey,
  uiScale,
  onUiScaleChange,
  messages,
  activeProject,
  onClose,
}: WorkspaceSettingsPanelProps) {
  const assistantTurns = messages.filter(m => m.role === 'assistant' && m.status === 'Completed');
  const totalIn = assistantTurns.reduce((sum, m) => sum + (m.inputTokens ?? 0), 0);
  const totalOut = assistantTurns.reduce((sum, m) => sum + (m.outputTokens ?? 0), 0);
  const hasMeasured = assistantTurns.some(m => m.inputTokens != null || m.outputTokens != null);

  const [accessPolicy, setAccessPolicy] = useState<WorkspaceAccessPolicy>({ policyOverride: null, revision: 1 });
  const [policyBusy, setPolicyBusy] = useState(false);
  const [policyError, setPolicyError] = useState('');
  const [confirmUnrestricted, setConfirmUnrestricted] = useState(false);
  const unrestrictedButtonRef = useRef<HTMLButtonElement | null>(null);
  const [worktreeSettings, setWorktreeSettings] = useState<WorktreeSettings>({ defaultWorktreeRootPath: null, revision: 1 });
  const [worktreeRootDraft, setWorktreeRootDraft] = useState('');
  const [worktreeBusy, setWorktreeBusy] = useState(false);
  const [worktreeError, setWorktreeError] = useState('');
  const [worktreeStatus, setWorktreeStatus] = useState('');
  const panelRef = useRef<HTMLElement | null>(null);

  useEffect(() => {
    const onPointerDown = (event: PointerEvent) => {
      const target = event.target;
      if (!(target instanceof Element)) return;
      if (panelRef.current?.contains(target)) return;
      if (target.closest('button[aria-label="Workspace settings"]')) return;
      if (target.closest('dialog')) return;
      onClose();
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !document.querySelector('dialog[open]')) {
        event.preventDefault();
        onClose();
      }
    };
    document.addEventListener('pointerdown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('pointerdown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [onClose]);

  useEffect(() => {
    if (!config) return;
    const ac = new AbortController();
    void request(config, '/settings/access-policy', ac.signal)
      .then(data => setAccessPolicy(data as WorkspaceAccessPolicy))
      .catch(reason => {
        if (reason instanceof DOMException && reason.name === 'AbortError') return;
        setPolicyError('Could not load workspace access policy.');
      });
    void request(config, '/settings/worktree-root', ac.signal)
      .then(data => {
        const loaded = data as WorktreeSettings;
        setWorktreeSettings(loaded);
        setWorktreeRootDraft(loaded.defaultWorktreeRootPath ?? '');
      })
      .catch(reason => {
        if (reason instanceof DOMException && reason.name === 'AbortError') return;
        setWorktreeError('Could not load default worktree folder.');
      });
    return () => ac.abort();
  }, [config]);

  async function saveWorkspacePolicy(nextOverride: string | null) {
    if (!config) return;
    setPolicyBusy(true);
    setPolicyError('');
    setConfirmUnrestricted(false);
    try {
      const updated = await request(config, '/settings/access-policy', new AbortController().signal, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ policyOverride: nextOverride, expectedRevision: accessPolicy.revision }),
      }) as WorkspaceAccessPolicy;
      setAccessPolicy(updated);
      setWorktreeSettings(curr => ({ ...curr, revision: updated.revision }));
    } catch {
      setPolicyError('Could not update workspace access policy.');
    } finally {
      setPolicyBusy(false);
    }
  }

  async function saveWorktreeRoot(nextPath: string | null) {
    if (!config) return;
    setWorktreeBusy(true);
    setWorktreeError('');
    setWorktreeStatus('');
    try {
      const updated = await request(config, '/settings/worktree-root', new AbortController().signal, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          defaultWorktreeRootPath: nextPath && nextPath.trim() ? nextPath.trim() : null,
          expectedRevision: worktreeSettings.revision,
        }),
      }) as WorktreeSettings;
      setWorktreeSettings(updated);
      setWorktreeRootDraft(updated.defaultWorktreeRootPath ?? '');
      setAccessPolicy(curr => ({ ...curr, revision: updated.revision }));
      setWorktreeStatus(updated.defaultWorktreeRootPath ? 'Saved for future worktrees.' : 'Reset to default location.');
    } catch (reason) {
      setWorktreeError(
        reason instanceof HostRequestError && reason.serverMessage
          ? reason.serverMessage
          : 'Could not update default worktree folder.',
      );
    } finally {
      setWorktreeBusy(false);
    }
  }

  async function browseWorktreeRoot() {
    setWorktreeError('');
    setWorktreeStatus('');
    try {
      const picked = await pickWorkspaceFolder();
      if (!picked) return;
      setWorktreeRootDraft(picked);
      await saveWorktreeRoot(picked);
    } catch {
      setWorktreeError('Could not open the Windows folder picker; enter a folder path directly.');
    }
  }

  return (
    <aside ref={panelRef} className="settings-panel" aria-label="Workspace settings">
      <div className="toolbar">
        <div>
          <h2>Workspace settings</h2>
          <small>Preferences are stored on this computer.</small>
        </div>
        <button aria-label="Close workspace settings" onClick={onClose}>×</button>
      </div>
      <Suspense fallback={<section className="settings-section" aria-busy="true"><small>Loading behavior settings…</small></section>}>
        <GuildhallBehaviorSettings config={config} />
      </Suspense>
      <Suspense fallback={<section className="settings-section" aria-busy="true"><small>Loading shared memory…</small></section>}>
        <section className="settings-section">
          <MemoryNotesPanel
            config={config}
            scope="AboutYou"
            refreshKey={memoryRefreshKey}
            title="About you"
            description="Shared facts and preferences available to every guild member."
          />
        </section>
      </Suspense>
      {activeProject && (
        <Suspense fallback={<section className="settings-section" aria-busy="true"><small>Loading project memory…</small></section>}>
          <section className="settings-section">
            <MemoryNotesPanel
              config={config}
              scope="Project"
              projectId={activeProject.id}
              refreshKey={memoryRefreshKey}
              title={`Project memory · ${activeProject.name}`}
              description="Notes scoped to this local project repository across all Council Members."
            />
          </section>
        </Suspense>
      )}
      <section className="settings-section">
        <div>
          <h3>Appearance</h3>
          <small>Display scale: {Math.round(uiScale * 100)}%</small>
        </div>
        <input
          aria-label="Display scale"
          type="range"
          min="80"
          max="150"
          step="5"
          value={Math.round(uiScale * 100)}
          onChange={event => onUiScaleChange(Number(event.target.value) / 100)}
        />
        <div className="settings-choice-row">
          <span>Theme</span>
          <button aria-pressed="true">Dark</button>
          <button disabled title="Additional themes are planned">Light · planned</button>
          <button disabled title="Additional themes are planned">System · planned</button>
        </div>
        <small>Keyboard: Ctrl/Cmd + or − to scale; Ctrl/Cmd 0 to reset.</small>
      </section>
      <section className="settings-section">
        <div>
          <h3>Agent access</h3>
          <small>
            {accessPolicy.policyOverride
              ? `Workspace override active: ${POLICY_LABELS[accessPolicy.policyOverride] ?? accessPolicy.policyOverride}`
              : 'Using each Council Member’s policy'}
          </small>
        </div>
        <div className="planned-policy" role="group" aria-label="Workspace agent access override">
          <button
            type="button"
            disabled={policyBusy || !config}
            aria-pressed={!accessPolicy.policyOverride}
            onClick={() => void saveWorkspacePolicy(null)}
          >
            Per-member
          </button>
          <button
            type="button"
            disabled={policyBusy || !config}
            aria-pressed={accessPolicy.policyOverride === 'always_ask'}
            onClick={() => void saveWorkspacePolicy('always_ask')}
          >
            Always ask
          </button>
          <button
            type="button"
            disabled={policyBusy || !config}
            aria-pressed={accessPolicy.policyOverride === 'ask_unsafe'}
            onClick={() => void saveWorkspacePolicy('ask_unsafe')}
          >
            Ask for unsafe actions
          </button>
          <button
            ref={unrestrictedButtonRef}
            type="button"
            className="danger-button"
            disabled={policyBusy || !config}
            aria-pressed={accessPolicy.policyOverride === 'unrestricted'}
            onClick={() => setConfirmUnrestricted(true)}
          >
            Unrestricted · danger
          </button>
        </div>
        {confirmUnrestricted && (
          <div className="unrestricted-confirm" role="region" aria-label="Confirm unrestricted access">
            <p><strong>Danger:</strong> Unrestricted mode auto-approves workspace edits and commands inside the active project/worktree without prompting. Path and secret exfiltration guards remain enforced.</p>
            <div className="settings-choice-row">
              <button type="button" className="danger-button" disabled={policyBusy} onClick={() => void saveWorkspacePolicy('unrestricted')}>Enable Unrestricted</button>
              <button type="button" disabled={policyBusy} onClick={() => { setConfirmUnrestricted(false); unrestrictedButtonRef.current?.focus(); }}>Cancel</button>
            </div>
          </div>
        )}
        {policyError && <small role="alert" className="profile-error">{policyError}</small>}
        <small>Workspace-wide override governs both native MCP tools and wrapped CLI harnesses.</small>
      </section>
      <section className="settings-section">
        <div>
          <h3>Default worktree folder</h3>
          <small>
            {worktreeSettings.defaultWorktreeRootPath
              ? 'New session worktrees will be created under this folder.'
              : 'Using default: <project-parent>/guildhall-worktrees'}
          </small>
        </div>
        <div className="worktree-settings-row">
          <input
            type="text"
            aria-label="Default worktree folder"
            placeholder="Default (<project-parent>/guildhall-worktrees)"
            value={worktreeRootDraft}
            disabled={worktreeBusy || !config}
            onChange={event => {
              setWorktreeRootDraft(event.target.value);
              setWorktreeError('');
              setWorktreeStatus('');
            }}
          />
          <button type="button" disabled={worktreeBusy || !config} onClick={() => void browseWorktreeRoot()}>
            Browse…
          </button>
        </div>
        <div className="settings-choice-row">
          <button
            type="button"
            disabled={
              worktreeBusy ||
              !config ||
              worktreeRootDraft.trim() === (worktreeSettings.defaultWorktreeRootPath ?? '')
            }
            onClick={() => void saveWorktreeRoot(worktreeRootDraft)}
          >
            {worktreeBusy ? 'Saving…' : 'Save'}
          </button>
          {(worktreeSettings.defaultWorktreeRootPath || worktreeRootDraft.trim()) && (
            <button
              type="button"
              disabled={worktreeBusy || !config}
              onClick={() => {
                if (worktreeSettings.defaultWorktreeRootPath) {
                  void saveWorktreeRoot(null);
                } else {
                  setWorktreeRootDraft('');
                  setWorktreeError('');
                  setWorktreeStatus('');
                }
              }}
            >
              Reset
            </button>
          )}
          {worktreeStatus && <small className="memory-status">{worktreeStatus}</small>}
        </div>
        {worktreeError && <small role="alert" className="profile-error">{worktreeError}</small>}
      </section>
      <section className="settings-section">
        <div>
          <h3>Usage &amp; spending</h3>
          <small>{hasMeasured ? 'Active session measured usage' : 'No provider usage data is connected.'}</small>
        </div>
        {!hasMeasured ? (
          <p>No measured token usage recorded yet in this session. Guildhall reports measured provider tokens and will never fabricate spending totals.</p>
        ) : (
          <p>
            Measured tokens in this session: {totalIn.toLocaleString()} input · {totalOut.toLocaleString()} output ({assistantTurns.length} completed turn{assistantTurns.length === 1 ? '' : 's'}). Subscription CLI and local Ollama turns are not billed per token.
          </p>
        )}
      </section>
    </aside>
  );
}
