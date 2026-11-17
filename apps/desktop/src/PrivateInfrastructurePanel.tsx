import { useEffect, useState } from 'react';
import { HostRequestError, request } from './api';
import type {
  Bootstrap,
  CustomMcpServerView,
  PrivateNetworkStatus,
  SqlMcpProbeResponse,
  SqlMcpProfileView,
} from './api';
import { Icon } from './Icon';

export function PrivateInfrastructurePanel({
  config,
  onSqlCatalogChanged,
}: {
  config: Bootstrap | null;
  onSqlCatalogChanged?: () => void;
}) {
  const [profiles, setProfiles] = useState<SqlMcpProfileView[]>([]);
  const [customServers, setCustomServers] = useState<CustomMcpServerView[]>([]);
  const [networkStatus, setNetworkStatus] = useState<PrivateNetworkStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [sqlError, setSqlError] = useState('');
  const [sqlNotice, setSqlNotice] = useState('');
  const [networkError, setNetworkError] = useState('');
  const [networkNotice, setNetworkNotice] = useState('');
  const [busyId, setBusyId] = useState<string | null>(null);
  const [confirmDeleteSqlId, setConfirmDeleteSqlId] = useState<string | null>(null);
  const [confirmDeleteMcpId, setConfirmDeleteMcpId] = useState<string | null>(null);

  // SQL Profile Form
  const [showSqlForm, setShowSqlForm] = useState(false);
  const [sqlName, setSqlName] = useState('');
  const [sqlServer, setSqlServer] = useState('localhost');
  const [sqlDatabase, setSqlDatabase] = useState('');
  const [sqlAuthMode, setSqlAuthMode] = useState<'integrated' | 'sql_login'>('integrated');
  const [sqlUsername, setSqlUsername] = useState('');
  const [sqlPassword, setSqlPassword] = useState('');
  const [sqlSchemas, setSqlSchemas] = useState('dbo');
  const [sqlObjects, setSqlObjects] = useState('');
  const [sqlMaxRows, setSqlMaxRows] = useState('100');

  // Remote Streamable HTTP MCP Form
  const [showMcpForm, setShowMcpForm] = useState(false);
  const [mcpId, setMcpId] = useState('');
  const [mcpName, setMcpName] = useState('');
  const [mcpUrl, setMcpUrl] = useState('');
  const [mcpToken, setMcpToken] = useState('');
  const [allowTunnelHttp, setAllowTunnelHttp] = useState(true);

  async function refreshAll(signal: AbortSignal = new AbortController().signal) {
    if (!config) {
      setLoading(false);
      return;
    }
    try {
      const [p, s, net] = await Promise.all([
        request(config, '/mcp/sql/profiles', signal) as Promise<SqlMcpProfileView[]>,
        request(config, '/mcp/servers/custom', signal) as Promise<CustomMcpServerView[]>,
        request(config, '/infrastructure/network-status', signal) as Promise<PrivateNetworkStatus>,
      ]);
      setProfiles(Array.isArray(p) ? p : []);
      setCustomServers(Array.isArray(s) ? s : []);
      setNetworkStatus(
        net && Array.isArray(net.tunnelInterfaces) && Array.isArray(net.portChecks)
          ? net
          : null,
      );
    } catch (reason) {
      if (reason instanceof DOMException && reason.name === 'AbortError') return;
      setNetworkError('Could not load private infrastructure status.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    const ac = new AbortController();
    void refreshAll(ac.signal);
    return () => ac.abort();
  }, [config]);

  function toggleSqlForm() {
    setShowSqlForm(v => {
      if (v) {
        setSqlPassword('');
        setSqlError('');
      }
      return !v;
    });
  }

  function toggleMcpForm() {
    setShowMcpForm(v => {
      if (v) {
        setMcpToken('');
        setNetworkError('');
      }
      return !v;
    });
  }

  async function handleCreateSqlProfile(event: React.FormEvent) {
    event.preventDefault();
    if (!config) return;
    setBusyId('sql-create');
    setSqlError('');
    setSqlNotice('');
    try {
      const allowedSchemas = sqlSchemas.split(',').map(s => s.trim()).filter(Boolean);
      const allowedObjects = sqlObjects.split(',').map(s => s.trim()).filter(Boolean);
      await request(config, '/mcp/sql/profiles', new AbortController().signal, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: sqlName.trim(),
          server: sqlServer.trim(),
          database: sqlDatabase.trim(),
          authMode: sqlAuthMode,
          username: sqlAuthMode === 'sql_login' ? sqlUsername.trim() : null,
          password: sqlAuthMode === 'sql_login' ? sqlPassword : null,
          allowedSchemas: allowedSchemas.length > 0 ? allowedSchemas : ['dbo'],
          allowedObjects,
          maxRows: Math.min(500, Math.max(1, Number.parseInt(sqlMaxRows, 10) || 100)),
          commandTimeoutSeconds: 10,
        }),
      });
      setSqlName('');
      setSqlDatabase('');
      setSqlUsername('');
      setSqlPassword('');
      setSqlObjects('');
      setShowSqlForm(false);
      setSqlNotice('Read-only SQL Server MCP profile saved.');
      await refreshAll();
      onSqlCatalogChanged?.();
    } catch (reason) {
      setSqlError(reason instanceof HostRequestError && reason.serverMessage ? reason.serverMessage : 'Could not create SQL MCP profile.');
    } finally {
      setBusyId(null);
    }
  }

  async function handleProbeSqlProfile(id: string) {
    if (!config) return;
    setBusyId(`sql-probe-${id}`);
    setSqlError('');
    setSqlNotice('');
    try {
      const res = (await request(config, `/mcp/sql/profiles/${encodeURIComponent(id)}/probe`, new AbortController().signal, {
        method: 'POST',
      })) as SqlMcpProbeResponse;
      if (res.healthy) {
        setSqlNotice(`${res.name}: ${res.detail ?? 'Connected read-only'}`);
      } else {
        setSqlError(`${res.name}: ${res.detail ?? 'Connection failed'}`);
      }
      onSqlCatalogChanged?.();
    } catch {
      setSqlError('Could not probe SQL MCP profile.');
    } finally {
      setBusyId(null);
    }
  }

  async function handleDeleteSqlProfile(id: string) {
    if (!config) return;
    setBusyId(`sql-del-${id}`);
    setSqlError('');
    setSqlNotice('');
    try {
      await request(config, `/mcp/sql/profiles/${encodeURIComponent(id)}`, new AbortController().signal, { method: 'DELETE' });
      setConfirmDeleteSqlId(null);
      setSqlNotice('SQL MCP profile removed.');
      await refreshAll();
      onSqlCatalogChanged?.();
    } catch {
      setSqlError('Could not remove SQL MCP profile.');
    } finally {
      setBusyId(null);
    }
  }

  async function handleCreateRemoteMcp(event: React.FormEvent) {
    event.preventDefault();
    if (!config) return;
    const trimmedToken = mcpToken.trim();
    if (trimmedToken.length > 0 && trimmedToken.length < 8) {
      setNetworkError('Bearer token must be at least 8 characters (or leave blank if unauthenticated).');
      return;
    }

    setBusyId('mcp-create');
    setNetworkError('');
    setNetworkNotice('');
    const id = mcpId.trim().toLowerCase();
    let serverCreated = false;
    try {
      await request(config, '/mcp/servers', new AbortController().signal, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          id,
          displayName: mcpName.trim(),
          transport: 'streamable_http',
          endpointUrl: mcpUrl.trim(),
          allowPrivateTunnelHttp: allowTunnelHttp,
          securityTier: 'Tier3',
        }),
      });
      serverCreated = true;

      if (trimmedToken.length >= 8) {
        await request(config, `/mcp/servers/${encodeURIComponent(id)}/token`, new AbortController().signal, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ token: trimmedToken }),
        });
      }

      setMcpId('');
      setMcpName('');
      setMcpUrl('');
      setMcpToken('');
      setShowMcpForm(false);
      await refreshAll();

      try {
        const probe = (await request(config, `/mcp/servers/${encodeURIComponent(id)}/probe`, new AbortController().signal, {
          method: 'POST',
        })) as { status?: string; lastError?: string | null };
        if (probe.status === 'Connected') {
          setNetworkNotice(`Remote Streamable HTTP MCP '${id}' registered and connected.`);
        } else {
          setNetworkError(`Registered '${id}', but probe reported ${probe.status ?? 'Unavailable'}: ${probe.lastError ?? 'check endpoint URL/token'}.`);
        }
      } catch {
        setNetworkNotice(`Remote Streamable HTTP MCP '${id}' registered (click Test to discover tools).`);
      }
    } catch (reason) {
      if (serverCreated) {
        await refreshAll();
        setNetworkError('Remote MCP server was registered, but saving the bearer token failed.');
      } else {
        setNetworkError(reason instanceof HostRequestError && reason.serverMessage ? reason.serverMessage : 'Could not register remote MCP server.');
      }
    } finally {
      setBusyId(null);
    }
  }

  async function handleProbeRemoteMcp(id: string) {
    if (!config) return;
    setBusyId(`mcp-probe-${id}`);
    setNetworkError('');
    setNetworkNotice('');
    try {
      const res = (await request(config, `/mcp/servers/${encodeURIComponent(id)}/probe`, new AbortController().signal, {
        method: 'POST',
      })) as { status?: string; lastError?: string | null };
      if (res.status === 'Connected') {
        setNetworkNotice(`MCP server '${id}' connected and tools discovered.`);
      } else {
        setNetworkError(`MCP server '${id}' probe failed (${res.status ?? 'Unavailable'}): ${res.lastError ?? 'no response'}`);
      }
      await refreshAll();
    } catch {
      setNetworkError(`Could not probe MCP server '${id}'.`);
    } finally {
      setBusyId(null);
    }
  }

  async function handleDeleteRemoteMcp(id: string) {
    if (!config) return;
    setBusyId(`mcp-del-${id}`);
    setNetworkError('');
    setNetworkNotice('');
    try {
      await request(config, `/mcp/servers/${encodeURIComponent(id)}`, new AbortController().signal, { method: 'DELETE' });
      await request(config, `/mcp/servers/${encodeURIComponent(id)}/token`, new AbortController().signal, { method: 'DELETE' });
      setConfirmDeleteMcpId(null);
      setNetworkNotice(`Removed MCP server '${id}'.`);
      await refreshAll();
    } catch {
      setNetworkError('Could not remove remote MCP server.');
    } finally {
      setBusyId(null);
    }
  }

  const allPortsPrivate = networkStatus?.portChecks.every(
    pc => pc.posture === 'private_only' || pc.posture === 'not_listening',
  ) ?? true;

  return (
    <section className="infra-section" aria-label="Private infrastructure and SQL MCP">
      <div className="connection-pillar-group">
        <div className="infra-header-row">
          <h4>Read-Only SQL Server MCP Profiles</h4>
          <button
            type="button"
            aria-expanded={showSqlForm}
            disabled={busyId === 'sql-create' || !config}
            onClick={toggleSqlForm}
          >
            {showSqlForm ? 'Cancel' : 'Add SQL profile'}
          </button>
        </div>

        {sqlError && <p className="settings-error" role="alert">{sqlError}</p>}
        {sqlNotice && <p className="memory-status" role="status">{sqlNotice}</p>}

        {showSqlForm && (
          <form className="infra-form" onSubmit={e => void handleCreateSqlProfile(e)}>
            <div className="infra-form-grid">
              <label>
                Profile name
                <input required value={sqlName} onChange={e => setSqlName(e.target.value)} placeholder="home-analytics" />
              </label>
              <label>
                SQL Server host
                <input required value={sqlServer} onChange={e => setSqlServer(e.target.value)} placeholder="localhost or 10.66.0.2" />
              </label>
              <label>
                Database (optional — blank for all user DBs)
                <input value={sqlDatabase} onChange={e => setSqlDatabase(e.target.value)} placeholder="All user databases (except GuildhallDev)" />
              </label>
              <label>
                Auth mode
                <select value={sqlAuthMode} onChange={e => setSqlAuthMode(e.target.value as 'integrated' | 'sql_login')}>
                  <option value="integrated">Windows Integrated</option>
                  <option value="sql_login">SQL Login (DPAPI)</option>
                </select>
              </label>
              {sqlAuthMode === 'sql_login' && (
                <>
                  <label>
                    SQL username
                    <input required value={sqlUsername} onChange={e => setSqlUsername(e.target.value)} autoComplete="off" />
                  </label>
                  <label>
                    SQL password
                    <input required type="password" value={sqlPassword} onChange={e => setSqlPassword(e.target.value)} autoComplete="off" />
                  </label>
                </>
              )}
              <label>
                Allowed schemas (comma-separated)
                <input value={sqlSchemas} onChange={e => setSqlSchemas(e.target.value)} placeholder="dbo, reporting" />
              </label>
              <label>
                Allowed views/tables (optional)
                <input value={sqlObjects} onChange={e => setSqlObjects(e.target.value)} placeholder="dbo.v_SalesSummary" />
              </label>
              <label>
                Max rows (1–500)
                <input type="number" min={1} max={500} value={sqlMaxRows} onChange={e => setSqlMaxRows(e.target.value)} />
              </label>
            </div>
            <div className="api-key-form-actions">
              <button type="submit" disabled={busyId === 'sql-create'}>
                {busyId === 'sql-create' ? 'Saving…' : 'Save read-only profile'}
              </button>
            </div>
          </form>
        )}

        {loading ? (
          <p className="memory-status">Loading private infrastructure status…</p>
        ) : profiles.length === 0 ? (
          <p className="memory-status">No read-only SQL Server profiles configured. GuildhallDev is always protected.</p>
        ) : (
          <div className="provider-grid">
            {profiles.map(p => (
              <article key={p.id} className="connection-card">
                <div className="connection-card-top">
                  <div>
                    <strong>{p.name}</strong>
                    <small>{p.server} · {p.database || 'All user DBs (except GuildhallDev)'} ({p.authMode === 'sql_login' ? 'DPAPI login' : 'Windows Auth'})</small>
                  </div>
                  <span className="connection-status-pill status-connected">Read-Only</span>
                </div>
                <small>
                  Schemas: {p.allowedSchemas.join(', ')} · Objects: {p.allowedObjects.length > 0 ? p.allowedObjects.join(', ') : 'All in allowed schemas'} · Max {p.maxRows} rows · {p.commandTimeoutSeconds}s timeout
                </small>
                <div className="connection-card-actions">
                  <button
                    type="button"
                    className="icon-action-button"
                    aria-label={`Test SQL profile ${p.name}`}
                    title={busyId === `sql-probe-${p.id}` ? 'Testing…' : 'Test connection'}
                    disabled={busyId === `sql-probe-${p.id}`}
                    onClick={() => void handleProbeSqlProfile(p.id)}
                  >
                    <Icon name="play" size={14} />
                  </button>
                  <span className="card-action-spacer" />
                  {confirmDeleteSqlId === p.id ? (
                    <>
                      <button
                        type="button"
                        className="icon-action-button danger-button"
                        aria-label={`Confirm remove SQL profile ${p.name}`}
                        title={busyId === `sql-del-${p.id}` ? 'Removing…' : 'Confirm remove'}
                        disabled={busyId === `sql-del-${p.id}`}
                        onClick={() => void handleDeleteSqlProfile(p.id)}
                      >
                        <Icon name="check" size={14} />
                      </button>
                      <button
                        type="button"
                        className="icon-action-button"
                        aria-label="Cancel remove"
                        title="Cancel"
                        onClick={() => {
                          setConfirmDeleteSqlId(null);
                          requestAnimationFrame(() => document.getElementById(`sql-del-${p.id}`)?.focus());
                        }}
                      >
                        <Icon name="close" size={14} />
                      </button>
                    </>
                  ) : (
                    <button
                      id={`sql-del-${p.id}`}
                      type="button"
                      className="icon-action-button danger-button"
                      aria-label={`Remove SQL profile ${p.name}`}
                      title="Remove SQL profile"
                      onClick={() => setConfirmDeleteSqlId(p.id)}
                    >
                      <Icon name="trash" size={14} />
                    </button>
                  )}
                </div>
              </article>
            ))}
          </div>
        )}
      </div>

      <div className="connection-pillar-group">
        <div className="infra-header-row">
          <h4>Private Networking &amp; Remote Streamable HTTP MCP</h4>
          <div className="connection-card-actions">
            <button
              type="button"
              disabled={busyId === 'refresh-net' || !config}
              onClick={() => {
                setBusyId('refresh-net');
                void refreshAll().finally(() => setBusyId(null));
              }}
            >
              {busyId === 'refresh-net' ? 'Refreshing…' : 'Refresh network'}
            </button>
            <button
              type="button"
              aria-expanded={showMcpForm}
              disabled={busyId === 'mcp-create' || !config}
              onClick={toggleMcpForm}
            >
              {showMcpForm ? 'Cancel' : 'Add remote MCP'}
            </button>
          </div>
        </div>

        {networkError && <p className="settings-error" role="alert">{networkError}</p>}
        {networkNotice && <p className="memory-status" role="status">{networkNotice}</p>}

        {networkStatus && (
          <div className="provider-grid">
            <article className="connection-card">
              <div className="connection-card-top">
                <div>
                  <strong>Private Tunnels (WireGuard / Tailscale)</strong>
                  <small>
                    {networkStatus.wireGuardDetected || networkStatus.tailscaleDetected
                      ? `Detected: ${[networkStatus.wireGuardDetected ? 'WireGuard' : null, networkStatus.tailscaleDetected ? 'Tailscale' : null].filter(Boolean).join(', ')}`
                      : 'No active WireGuard or Tailscale tunnel interface detected'}
                  </small>
                </div>
                <span className={`connection-status-pill ${networkStatus.wireGuardDetected || networkStatus.tailscaleDetected ? 'status-connected' : 'status-not-connected'}`}>
                  {networkStatus.wireGuardDetected || networkStatus.tailscaleDetected ? 'Active' : 'Not detected'}
                </span>
              </div>
              {networkStatus.tunnelInterfaces.map(t => (
                <small key={t.name}>{t.kind} ({t.name}): {t.addresses.join(', ') || t.status}</small>
              ))}
            </article>

            <article className="connection-card">
              <div className="connection-card-top">
                <div>
                  <strong>Port Exposure Posture (1433 &amp; 11434)</strong>
                  <small>SQL Server &amp; Ollama must never be exposed publicly</small>
                </div>
                <span className={`connection-status-pill ${allPortsPrivate ? 'status-connected' : 'status-signin'}`}>
                  {allPortsPrivate ? 'Private only' : 'Check firewall'}
                </span>
              </div>
              {networkStatus.portChecks.map(pc => (
                <small key={pc.port}>{pc.summary}</small>
              ))}
            </article>
          </div>
        )}

        {showMcpForm && (
          <form className="infra-form" onSubmit={e => void handleCreateRemoteMcp(e)}>
            <div className="infra-form-grid">
              <label>
                Server ID
                <input required value={mcpId} onChange={e => setMcpId(e.target.value)} placeholder="droplet-relay" />
              </label>
              <label>
                Display name
                <input required value={mcpName} onChange={e => setMcpName(e.target.value)} placeholder="DigitalOcean MCP Relay" />
              </label>
              <label>
                Streamable HTTP Endpoint URL
                <input required value={mcpUrl} onChange={e => setMcpUrl(e.target.value)} placeholder="http://10.66.0.1:8080/mcp or https://..." />
              </label>
              <label>
                Bearer token (optional, DPAPI)
                <input type="password" value={mcpToken} onChange={e => setMcpToken(e.target.value)} autoComplete="off" placeholder="Min 8 chars" />
              </label>
            </div>
            <label className="infra-checkbox">
              <input type="checkbox" checked={allowTunnelHttp} onChange={e => setAllowTunnelHttp(e.target.checked)} />
              <span>Allow private-tunnel HTTP (WireGuard / Tailscale RFC1918 &amp; 100.64.0.0/10 only)</span>
            </label>
            <div className="api-key-form-actions">
              <button type="submit" disabled={busyId === 'mcp-create'}>
                {busyId === 'mcp-create' ? 'Registering…' : 'Register remote MCP'}
              </button>
            </div>
          </form>
        )}

        {customServers.length > 0 && (
          <div className="provider-grid">
            {customServers.map(s => (
              <article key={s.id} className="connection-card">
                <div className="connection-card-top">
                  <div>
                    <strong>{s.displayName}</strong>
                    <small>{s.transport} · {s.endpointUrl ?? 'stdio'}</small>
                  </div>
                  <span className={`connection-status-pill ${s.healthStatus === 'Connected' ? 'status-connected' : s.healthStatus === 'Disconnected' ? 'status-not-connected' : 'status-unavailable'}`}>
                    {s.healthStatus ?? s.securityTier}
                  </span>
                </div>
                <small>
                  {s.securityTier}{s.hasToken ? ' · Token DPAPI' : ''} · {s.toolCount ?? 0} tool{(s.toolCount ?? 0) === 1 ? '' : 's'} discovered
                </small>
                <div className="connection-card-actions">
                  <button
                    type="button"
                    className="icon-action-button"
                    aria-label={`Test remote MCP ${s.displayName}`}
                    title={busyId === `mcp-probe-${s.id}` ? 'Testing…' : 'Test connection'}
                    disabled={busyId === `mcp-probe-${s.id}`}
                    onClick={() => void handleProbeRemoteMcp(s.id)}
                  >
                    <Icon name="play" size={14} />
                  </button>
                  <span className="card-action-spacer" />
                  {confirmDeleteMcpId === s.id ? (
                    <>
                      <button
                        type="button"
                        className="icon-action-button danger-button"
                        aria-label={`Confirm remove remote MCP ${s.displayName}`}
                        title={busyId === `mcp-del-${s.id}` ? 'Removing…' : 'Confirm remove'}
                        disabled={busyId === `mcp-del-${s.id}`}
                        onClick={() => void handleDeleteRemoteMcp(s.id)}
                      >
                        <Icon name="check" size={14} />
                      </button>
                      <button
                        type="button"
                        className="icon-action-button"
                        aria-label="Cancel remove"
                        title="Cancel"
                        onClick={() => {
                          setConfirmDeleteMcpId(null);
                          requestAnimationFrame(() => document.getElementById(`mcp-del-${s.id}`)?.focus());
                        }}
                      >
                        <Icon name="close" size={14} />
                      </button>
                    </>
                  ) : (
                    <button
                      id={`mcp-del-${s.id}`}
                      type="button"
                      className="icon-action-button danger-button"
                      aria-label={`Remove remote MCP ${s.displayName}`}
                      title="Remove remote MCP"
                      onClick={() => setConfirmDeleteMcpId(s.id)}
                    >
                      <Icon name="trash" size={14} />
                    </button>
                  )}
                </div>
              </article>
            ))}
          </div>
        )}
      </div>
    </section>
  );
}
