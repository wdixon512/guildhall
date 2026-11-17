import { lazy, Suspense } from 'react';
import type { Bootstrap, MessageView } from './api';

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
  onClose: () => void;
}

export function WorkspaceSettingsPanel({
  config,
  memoryRefreshKey,
  uiScale,
  onUiScaleChange,
  messages,
  onClose,
}: WorkspaceSettingsPanelProps) {
  const assistantTurns = messages.filter(m => m.role === 'assistant' && m.status === 'Completed');
  const totalIn = assistantTurns.reduce((sum, m) => sum + (m.inputTokens ?? 0), 0);
  const totalOut = assistantTurns.reduce((sum, m) => sum + (m.outputTokens ?? 0), 0);
  const hasMeasured = assistantTurns.some(m => m.inputTokens != null || m.outputTokens != null);

  return (
    <aside className="settings-panel" aria-label="Workspace settings">
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
          <small>Workspace-wide override · planned (per-member policy is active)</small>
        </div>
        <div className="planned-policy" aria-label="Planned workspace agent access overrides">
          <button disabled>Always ask</button>
          <button disabled>Ask for unsafe actions</button>
          <button disabled>Unrestricted · danger</button>
        </div>
        <small>Always ask and Ask for unsafe actions are active per Council Member in their profile drawer; workspace-wide overrides and Unrestricted mode are planned for Phase 4.</small>
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
