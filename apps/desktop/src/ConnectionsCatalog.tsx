import { lazy, Suspense, useState } from 'react';
import { HostRequestError, request } from './api';
import type { Bootstrap, ConnectionEntry, ConnectionsCatalogData } from './api';
import { Icon } from './Icon';
import { openExternalLink } from './StreamingMarkdown';

const PrivateInfrastructurePanel = lazy(() =>
  import('./PrivateInfrastructurePanel').then(module => ({ default: module.PrivateInfrastructurePanel }))
);
import antigravityLogo from './assets/connections/antigravity.svg';
import anthropicLogo from './assets/connections/anthropic.svg';
import claudeLogo from './assets/connections/claude.svg';
import cursorLogo from './assets/connections/cursor.svg';
import filesystemLogo from './assets/connections/filesystem.svg';
import geminiLogo from './assets/connections/gemini.svg';
import gitLogo from './assets/connections/git.svg';
import githubLogo from './assets/connections/github.svg';
import gmailLogo from './assets/connections/gmail.svg';
import googleDriveLogo from './assets/connections/google-drive.svg';
import ollamaLogo from './assets/connections/ollama.svg';
import openaiLogo from './assets/connections/openai.svg';
import sqlServerLogo from './assets/connections/sql-server.svg';

const CONNECTION_LOGOS: Record<string, string> = {
  ollama: ollamaLogo,
  'openai-api': openaiLogo,
  'gemini-api': geminiLogo,
  'anthropic-api': anthropicLogo,
  'codex-cli': openaiLogo,
  'agy-cli': antigravityLogo,
  'claude-cli': claudeLogo,
  'cursor-cli': cursorLogo,
  'gemini-cli': geminiLogo,
  'filesystem-mcp': filesystemLogo,
  'git-mcp': gitLogo,
  'github-mcp': githubLogo,
  'google-drive-mcp': googleDriveLogo,
  'gmail-mcp': gmailLogo,
  'sql-server-mcp': sqlServerLogo,
};

const INSTALL_GUIDES: Record<string, { url: string; command?: string }> = {
  ollama: { url: 'https://ollama.com/download' },
  'codex-cli': { url: 'https://github.com/openai/codex', command: 'npm i -g @openai/codex' },
  'agy-cli': { url: 'https://antigravity.google/docs/cli/reference' },
  'claude-cli': { url: 'https://docs.anthropic.com/en/docs/claude-code/overview', command: 'irm https://claude.ai/install.ps1 | iex' },
  'cursor-cli': { url: 'https://cursor.com/cli', command: 'irm https://cursor.com/install.ps1 | iex' },
};

const PILLAR_ORDER = [
  'Local Runtimes',
  'Cloud Model APIs',
  'Local CLI Agent Harnesses',
  'MCP & Service Integrations',
] as const;

type ConnectionsSection = 'all' | (typeof PILLAR_ORDER)[number] | 'private-infra';

const SECTION_LABELS: Record<ConnectionsSection, string> = {
  all: 'All',
  'Local Runtimes': 'Local Runtimes',
  'Cloud Model APIs': 'Cloud APIs',
  'Local CLI Agent Harnesses': 'CLI Harnesses',
  'MCP & Service Integrations': 'MCP & Services',
  'private-infra': 'SQL & Network',
};

function statusClass(status: ConnectionEntry['status']): string {
  switch (status) {
    case 'Connected':
      return 'status-connected';
    case 'Needs sign-in':
      return 'status-signin';
    case 'Not connected':
      return 'status-not-connected';
    case 'Planned':
      return 'status-planned';
    default:
      return 'status-unavailable';
  }
}

export function ConnectionsCatalog({
  config,
  catalog,
  loading,
  onCatalogUpdated,
  onProbeAll,
}: {
  config: Bootstrap | null;
  catalog: ConnectionsCatalogData | null;
  loading: boolean;
  onCatalogUpdated: (updated: ConnectionsCatalogData) => void;
  onProbeAll: () => void;
}) {
  const [keyDrafts, setKeyDrafts] = useState<Record<string, string>>({});
  const [editingId, setEditingId] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [activeSection, setActiveSection] = useState<ConnectionsSection>('all');

  async function saveApiKey(id: string) {
    if (!config) return;
    const apiKey = (keyDrafts[id] ?? '').trim();
    if (apiKey.length < 8) {
      setError('Enter an API key of at least 8 characters.');
      return;
    }

    setBusyId(id);
    setError('');
    const controller = new AbortController();
    try {
      const updated = (await request(config, `/connections/${encodeURIComponent(id)}/api-key`, controller.signal, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ apiKey }),
      })) as ConnectionEntry;
      setKeyDrafts(current => ({ ...current, [id]: '' }));
      setEditingId(null);
      if (catalog) {
        onCatalogUpdated({
          ...catalog,
          entries: catalog.entries.map(entry => (entry.id === id ? updated : entry)),
        });
      }
    } catch (reason) {
      setError(reason instanceof HostRequestError && reason.serverMessage ? reason.serverMessage : 'Could not save API key.');
    } finally {
      setBusyId(null);
    }
  }

  async function deleteApiKey(id: string) {
    if (!config) return;
    setBusyId(id);
    setError('');
    const controller = new AbortController();
    try {
      const updated = (await request(config, `/connections/${encodeURIComponent(id)}/api-key`, controller.signal, {
        method: 'DELETE',
      })) as ConnectionEntry;
      if (catalog) {
        onCatalogUpdated({
          ...catalog,
          entries: catalog.entries.map(entry => (entry.id === id ? updated : entry)),
        });
      }
    } catch {
      setError('Could not remove stored API key.');
    } finally {
      setBusyId(null);
    }
  }

  async function probeOne(id: string) {
    if (!config) return;
    setBusyId(id);
    setError('');
    const controller = new AbortController();
    try {
      const updated = (await request(config, `/connections/${encodeURIComponent(id)}/probe`, controller.signal, {
        method: 'POST',
      })) as ConnectionEntry;
      if (catalog) {
        onCatalogUpdated({
          ...catalog,
          entries: catalog.entries.map(entry => (entry.id === id ? updated : entry)),
        });
      }
    } catch {
      setError('Could not check connection status.');
    } finally {
      setBusyId(null);
    }
  }

  async function launchCliSignIn(id: string, displayName: string) {
    if (!config) return;
    setBusyId(id);
    setError('');
    setNotice('');
    const controller = new AbortController();
    try {
      await request(config, `/connections/${encodeURIComponent(id)}/sign-in`, controller.signal, {
        method: 'POST',
      });
      setNotice(`Opened a terminal window for ${displayName}. Complete sign-in there, then click Test.`);
    } catch (reason) {
      setError(reason instanceof HostRequestError && reason.serverMessage ? reason.serverMessage : `Could not launch ${displayName} sign-in.`);
    } finally {
      setBusyId(null);
    }
  }

  const entries = catalog?.entries ?? [];
  const hasSqlMcp = entries.some(entry => entry.id === 'sql-server-mcp');
  const visiblePillars = activeSection === 'all'
    ? PILLAR_ORDER
    : PILLAR_ORDER.filter(p => p === activeSection);
  const showPrivateInfra = hasSqlMcp && (activeSection === 'all' || activeSection === 'private-infra');

  return (
    <section className="connection-catalog" aria-label="Backing engines and integrations">
      <div className="connection-catalog-header">
        <div>
          <h3>Backing engines &amp; integrations</h3>
          <small>API keys are encrypted in Windows DPAPI; CLI harnesses reuse your local CLI sign-in.</small>
        </div>
        <button type="button" onClick={onProbeAll} disabled={loading || !config}>
          {loading ? 'Checking…' : 'Probe all'}
        </button>
      </div>

      {entries.length > 0 && (
        <div className="connections-section-tabs" role="group" aria-label="Filter connection sections">
          {(['all', ...PILLAR_ORDER, ...(hasSqlMcp ? (['private-infra'] as const) : [])] as ConnectionsSection[]).map(sec => {
            const count = sec === 'all'
              ? entries.filter(e => e.status === 'Connected').length
              : sec === 'private-infra'
                ? null
                : entries.filter(e => e.pillar === sec && e.status === 'Connected').length;
            const total = sec === 'all'
              ? entries.length
              : sec === 'private-infra'
                ? null
                : entries.filter(e => e.pillar === sec).length;
            const countLabel = count !== null && total !== null && total > 0 ? `, ${count} of ${total} connected` : '';
            return (
              <button
                key={sec}
                type="button"
                aria-pressed={activeSection === sec}
                aria-label={`${SECTION_LABELS[sec]}${countLabel}`}
                className={`connections-section-tab ${activeSection === sec ? 'selected' : ''}`}
                onClick={() => setActiveSection(sec)}
              >
                <span>{SECTION_LABELS[sec]}</span>
                {count !== null && total !== null && total > 0 && (
                  <small aria-hidden="true">{count}/{total}</small>
                )}
              </button>
            );
          })}
        </div>
      )}

      {error && <p className="settings-error" role="alert">{error}</p>}
      {notice && <p className="memory-status" role="status">{notice}</p>}

      {!catalog && loading && <p className="memory-status">Loading connections catalog…</p>}
      {!catalog && !loading && <p className="memory-status">Connections catalog is not loaded yet. Click Probe all to load.</p>}

      {visiblePillars.map(pillar => {
        const group = entries
          .filter(entry => entry.pillar === pillar)
          .sort((a, b) => {
            const aConn = a.status === 'Connected' ? 0 : 1;
            const bConn = b.status === 'Connected' ? 0 : 1;
            if (aConn !== bConn) return aConn - bConn;
            return a.displayName.localeCompare(b.displayName);
          });
        if (group.length === 0) return null;

        return (
          <div key={pillar} className="connection-pillar-group">
            <h4>{pillar}</h4>
            <div className="provider-grid">
              {group.map(entry => {
                const isBusy = busyId === entry.id;
                const isEditing = editingId === entry.id;
                const logoSrc = CONNECTION_LOGOS[entry.id];
                return (
                  <article key={entry.id} className={`connection-card ${entry.isPlanned ? 'is-planned' : ''}`}>
                    <div className="connection-card-top">
                      <div className="connection-card-title">
                        {logoSrc && (
                          <span className="connection-logo" aria-hidden="true">
                            <img src={logoSrc} alt="" width={18} height={18} />
                          </span>
                        )}
                        <div>
                          <strong>{entry.displayName}</strong>
                          <small>{entry.statusDetail ?? ''}</small>
                        </div>
                      </div>
                      <span className={`connection-status-pill ${statusClass(entry.status)}`}>{entry.status}</span>
                    </div>

                    {entry.isPlanned ? (
                      <div className="connection-card-actions">
                        <button type="button" disabled title={`${entry.displayName} is planned for a later phase`}>
                          Planned
                        </button>
                      </div>
                    ) : (
                      <div className="connection-card-actions">
                        {entry.canConfigureApiKey && !isEditing && (
                          <button
                            type="button"
                            className="icon-action-button"
                            aria-label={entry.keyHint ? `Update ${entry.displayName} API key` : `Add ${entry.displayName} API key`}
                            title={entry.keyHint ? 'Update API key' : 'Add API key'}
                            disabled={isBusy || !config}
                            onClick={() => setEditingId(entry.id)}
                          >
                            <Icon name="key" size={14} />
                          </button>
                        )}
                        {entry.engineKind === 'cli_harness' && entry.status === 'Needs sign-in' && (
                          <button type="button" disabled={isBusy || !config} onClick={() => void launchCliSignIn(entry.id, entry.displayName)}>
                            Sign in ↗
                          </button>
                        )}
                        {entry.canProbe && (
                          <button
                            type="button"
                            className="icon-action-button"
                            aria-label={`Test ${entry.displayName}`}
                            title={isBusy ? 'Checking…' : 'Test connection'}
                            disabled={isBusy || !config}
                            onClick={() => void probeOne(entry.id)}
                          >
                            <Icon name="play" size={14} />
                          </button>
                        )}
                        {entry.status === 'Unavailable' && INSTALL_GUIDES[entry.id] && (
                          <button
                            type="button"
                            onClick={() => void openExternalLink(INSTALL_GUIDES[entry.id].url)}
                            title={INSTALL_GUIDES[entry.id].command ? `Open install page (${INSTALL_GUIDES[entry.id].command})` : 'Open official download page'}
                          >
                            Install ↗
                          </button>
                        )}
                        {entry.canConfigureApiKey && entry.keyHint && !isEditing && (
                          <button
                            type="button"
                            className="icon-action-button danger-button card-action-spacer"
                            aria-label={`Remove ${entry.displayName} API key`}
                            title="Remove API key"
                            disabled={isBusy || !config}
                            onClick={() => void deleteApiKey(entry.id)}
                          >
                            <Icon name="trash" size={14} />
                          </button>
                        )}
                      </div>
                    )}
                    {entry.status === 'Unavailable' && INSTALL_GUIDES[entry.id]?.command && (
                      <small className="connection-install-cmd"><code>{INSTALL_GUIDES[entry.id].command}</code></small>
                    )}

                    {entry.canConfigureApiKey && isEditing && (
                      <form
                        className="api-key-form"
                        onSubmit={event => {
                          event.preventDefault();
                          void saveApiKey(entry.id);
                        }}
                      >
                        <input
                          type="password"
                          autoComplete="off"
                          aria-label={`${entry.displayName} API key`}
                          placeholder="Paste API key…"
                          value={keyDrafts[entry.id] ?? ''}
                          onChange={event => setKeyDrafts(current => ({ ...current, [entry.id]: event.target.value }))}
                        />
                        <div className="api-key-form-actions">
                          <button type="submit" disabled={isBusy || !(keyDrafts[entry.id] ?? '').trim()}>
                            {isBusy ? 'Saving…' : 'Save & test'}
                          </button>
                          <button type="button" disabled={isBusy} onClick={() => setEditingId(null)}>
                            Cancel
                          </button>
                        </div>
                      </form>
                    )}
                  </article>
                );
              })}
            </div>
          </div>
        );
      })}

      {showPrivateInfra && (
        <Suspense fallback={<p className="memory-status">Loading private infrastructure…</p>}>
          <PrivateInfrastructurePanel config={config} onSqlCatalogChanged={onProbeAll} />
        </Suspense>
      )}
    </section>
  );
}
