import { lazy, Suspense } from 'react';
import type { Bootstrap, ConversationDetails } from './api';
import { PaneResizeHandle } from './PaneResizeHandle';

const ContextInspector = lazy(() =>
  import('./ContextInspector').then(module => ({ default: module.ContextInspector })),
);
const WorktreeDiffDrawer = lazy(() =>
  import('./WorktreeDiffDrawer').then(module => ({ default: module.WorktreeDiffDrawer })),
);

export const SESSION_TABS = [
  { id: 'git', label: 'Git changes', status: 'No project' },
  { id: 'terminal', label: 'Terminal', status: 'Planned' },
  { id: 'plan', label: 'Plan', status: 'Not linked' },
  { id: 'context', label: 'Context usage', status: 'Live' },
  { id: 'browser', label: 'Browser', status: 'QC harness' },
  { id: 'side-chat', label: 'Side chat', status: 'Planned' },
  { id: 'automations', label: 'Scheduled tasks', status: 'Live' },
] as const;

export type SessionTabId = typeof SESSION_TABS[number]['id'];

const SESSION_PLACEHOLDER_TEXT: Record<Exclude<SessionTabId, 'context'>, { title: string; detail: string }> = {
  git: { title: 'Choose a project to see its Git changes', detail: 'Project selection will connect a local repository to this workspace. No repository is attached to this chat yet.' },
  terminal: { title: 'Terminal is planned', detail: 'A project-scoped terminal will run locally after project access and command controls are in place.' },
  plan: { title: 'No active plan is linked', detail: 'When Guildhall has a task plan for this session, it will appear here.' },
  browser: { title: 'Browser controls are planned', detail: 'Playwright browser interaction will appear here when the local QC harness is connected.' },
  'side-chat': { title: 'Side chat is planned', detail: 'A separate question thread will let you ask about this session without interrupting its main conversation.' },
  automations: { title: 'Scheduled tasks are disabled', detail: 'Local scheduled automations are planned for a later phase. They will run on this computer, with the selected agent access policy, when enabled.' },
};

interface SessionToolsPaneProps {
  closing: boolean;
  previewTab: SessionTabId;
  onSelectTab: (tab: SessionTabId) => void;
  onClose: () => void;
  toolsPaneWidth: number;
  rightPaneResizeMax: number;
  onResize: (width: number) => void;
  config: Bootstrap | null;
  conversation: ConversationDetails | null;
  memoryRefreshKey: number;
  running: boolean;
  worktreeNotice: string;
  onWorktreeResolved: (message: string) => void;
  renderAutomations?: () => React.ReactNode;
  automationsStatus?: string;
}

export function SessionToolsPane({
  closing,
  previewTab,
  onSelectTab,
  onClose,
  toolsPaneWidth,
  rightPaneResizeMax,
  onResize,
  config,
  conversation,
  memoryRefreshKey,
  running,
  worktreeNotice,
  onWorktreeResolved,
  renderAutomations,
  automationsStatus,
}: SessionToolsPaneProps) {
  const activePlaceholder = previewTab === 'context' ? null : SESSION_PLACEHOLDER_TEXT[previewTab];

  return (
    <aside id="session-tools-pane" className={`preview-pane ${closing ? 'panel-closing' : ''}`} aria-label="Session tools">
      <PaneResizeHandle
        className="right-pane-resizer tools-resizer"
        label="Resize Session Tools pane"
        controls="session-tools-pane"
        value={Math.min(toolsPaneWidth, rightPaneResizeMax)}
        min={240}
        max={rightPaneResizeMax}
        direction={-1}
        onResize={onResize}
      />
      <div className="pane-header">
        <span>◫</span><h2>Session tools</h2><span className="preview-label">LOCAL WORKSPACE</span>
        <button aria-label="Collapse session tools" onClick={onClose}>×</button>
      </div>
      <div className="session-tabs" role="tablist" aria-label="Session tools">
        {SESSION_TABS.map(tab => {
          const status = tab.id === 'git'
            ? (conversation?.conversation.activeWorktreeId
              ? 'Worktree'
              : conversation?.conversation.projectId
                ? 'Project'
                : 'No project')
            : tab.id === 'automations' && automationsStatus
              ? automationsStatus
              : tab.status;
          const selected = previewTab === tab.id;
          return (
            <button
              key={tab.id}
              id={`session-tab-${tab.id}`}
              role="tab"
              aria-selected={selected}
              aria-controls={`session-tabpanel-${tab.id}`}
              className={selected ? 'selected' : ''}
              onClick={() => onSelectTab(tab.id)}
            >
              {tab.label}<small>{status}</small>
            </button>
          );
        })}
      </div>
      <div
        id={`session-tabpanel-${previewTab}`}
        role="tabpanel"
        aria-labelledby={`session-tab-${previewTab}`}
        style={{ display: 'flex', flexDirection: 'column', flex: 1, minHeight: 0 }}
      >
        {previewTab === 'context' ? (
          <Suspense fallback={<section className="session-placeholder" aria-busy="true"><small>Loading context inspector…</small></section>}>
            <ContextInspector config={config} conversationId={conversation?.conversation.id ?? null} refreshToken={(conversation?.conversation.revision ?? 0) + memoryRefreshKey + (running ? 1 : 0)} />
          </Suspense>
        ) : previewTab === 'git' && conversation?.conversation.activeWorktreeId ? (
          <Suspense fallback={<section className="session-placeholder" aria-busy="true"><small>Loading diff review…</small></section>}>
            <WorktreeDiffDrawer
              config={config}
              conversationId={conversation.conversation.id}
              branchName={conversation.conversation.activeWorktreeBranch}
              onWorktreeResolved={onWorktreeResolved}
            />
          </Suspense>
        ) : previewTab === 'automations' && renderAutomations ? (
          renderAutomations()
        ) : activePlaceholder ? (
          <section key={previewTab} className="session-placeholder">
            <span className="session-placeholder-icon">
              {previewTab === 'git' ? '⌘' : previewTab === 'terminal' ? '>_' : previewTab === 'plan' ? '☷' : previewTab === 'browser' ? '◎' : previewTab === 'automations' ? '◷' : '◌'}
            </span>
            <span className="eyebrow">{activePlaceholder.title}</span>
            {previewTab === 'git' && worktreeNotice && <p role="status">{worktreeNotice}</p>}
            <p>
              {previewTab === 'git' && conversation?.conversation.projectId
                ? 'Project attached. Click Isolate Worktree in the workspace bar to edit on an isolated Git branch and review diffs here.'
                : activePlaceholder.detail}
            </p>
            {previewTab !== 'git' && <small>Preview only · This panel does not run commands or connect external services.</small>}
          </section>
        ) : null}
      </div>
    </aside>
  );
}
