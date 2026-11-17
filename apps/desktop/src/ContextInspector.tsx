import { useEffect, useState } from 'react';
import { request } from './api';
import type { Bootstrap, ContextInspectorData, VramResidency } from './api';

function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return '0 B';
  if (bytes >= 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
  if (bytes >= 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(0)} MB`;
  return `${(bytes / 1024).toFixed(0)} KB`;
}

export function ContextInspector({
  config,
  conversationId,
  refreshToken = 0,
}: {
  config: Bootstrap | null;
  conversationId: string | null;
  refreshToken?: number;
}) {
  const [data, setData] = useState<ContextInspectorData | null>(null);
  const [vramOnly, setVramOnly] = useState<VramResidency | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [manualTick, setManualTick] = useState(0);

  useEffect(() => {
    setData(null);
    setVramOnly(null);
    if (!config) return;
    const controller = new AbortController();
    setLoading(true);
    setError('');
    const path = conversationId ? `/conversations/${conversationId}/context` : '/vram';
    void (async () => {
      try {
        const result = await request(config, path, controller.signal);
        if (controller.signal.aborted) return;
        if (conversationId) {
          const ctx = result as ContextInspectorData;
          setData(ctx);
          setVramOnly(ctx.vram);
        } else {
          setData(null);
          setVramOnly(result as VramResidency);
        }
      } catch (reason) {
        if (reason instanceof DOMException && reason.name === 'AbortError') return;
        if (!controller.signal.aborted) setError('Could not load context inspector. Check the host connection.');
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    })();
    return () => controller.abort();
  }, [config?.hostUrl, config?.token, conversationId, refreshToken, manualTick]);

  const vram = data?.vram ?? vramOnly;
  const sessionPct = data ? Math.min(100, Math.round((data.budget.estimatedSessionTokens / Math.max(1, data.budget.maxSessionTokens)) * 100)) : 0;
  const turnPct = data ? Math.min(100, Math.round((data.budget.estimatedNextTurnTokens / Math.max(1, data.budget.maxTurnContextTokens)) * 100)) : 0;

  return (
    <section className="context-inspector" aria-label="Context usage inspector">
      <div className="context-inspector-header">
        <div>
          <h3>Context &amp; VRAM</h3>
          <small>Estimates use UTF-8 bytes/4; VRAM comes live from Ollama /api/ps.</small>
        </div>
        <button type="button" onClick={() => setManualTick(value => value + 1)} disabled={!config || loading}>
          {loading ? 'Refreshing…' : 'Refresh'}
        </button>
      </div>

      {error && <p className="settings-error" role="alert">{error}</p>}

      <div className="context-card">
        <div className="context-card-title">
          <strong>VRAM residency</strong>
          <span>{vram ? (vram.useGpu ? 'GPU offload' : 'CPU only') : '—'} · keep_alive {vram?.keepAliveSeconds ?? 0}s</span>
        </div>
        {!vram && loading && <p className="memory-status">Checking Ollama /api/ps…</p>}
        {!vram && !loading && !error && <p className="memory-status">Host connection unavailable.</p>}
        {vram?.status === 'unavailable' && <p className="memory-status">Ollama /api/ps unavailable.</p>}
        {vram?.status === 'idle' && <p className="memory-status">No model currently loaded in VRAM.</p>}
        {vram && vram.models.length > 0 && (
          <ul className="context-vram-list">
            {vram.models.map(model => (
              <li key={model.name}>
                <strong>{model.name}</strong>
                <span>{formatBytes(model.sizeVramBytes)} VRAM / {formatBytes(model.sizeBytes)} total{model.contextLength ? ` · ctx ${model.contextLength}` : ''}</span>
              </li>
            ))}
          </ul>
        )}
      </div>

      {!data && !loading && !error && (
        <p className="memory-status">Select or start a session to inspect its prompt sources and cross-turn budget.</p>
      )}

      {data && (
        <>
          <div className="context-card">
            <div className="context-card-title">
              <strong>Cross-Turn Budget</strong>
              <span>{data.budget.completedTurns} completed turns</span>
            </div>
            <div className="budget-meter">
              <div className="budget-label">
                <span>Next turn context</span>
                <span className={data.budget.isOverTurnLimit ? 'budget-over' : ''}>
                  ~{data.budget.estimatedNextTurnTokens.toLocaleString()} / {data.budget.maxTurnContextTokens.toLocaleString()} tokens
                </span>
              </div>
              <div className="budget-bar" role="progressbar" aria-label="Next turn context" aria-valuenow={turnPct} aria-valuemin={0} aria-valuemax={100}>
                <span style={{ width: `${turnPct}%` }} className={data.budget.isOverTurnLimit ? 'over' : ''} />
              </div>
            </div>
            <div className="budget-meter">
              <div className="budget-label">
                <span>Session cumulative</span>
                <span className={data.budget.isOverSessionBudget ? 'budget-over' : ''}>
                  ~{data.budget.estimatedSessionTokens.toLocaleString()} / {data.budget.maxSessionTokens.toLocaleString()} tokens
                </span>
              </div>
              <div className="budget-bar" role="progressbar" aria-label="Session cumulative" aria-valuenow={sessionPct} aria-valuemin={0} aria-valuemax={100}>
                <span style={{ width: `${sessionPct}%` }} className={data.budget.isOverSessionBudget ? 'over' : ''} />
              </div>
            </div>
          </div>

          <div className="context-card">
            <div className="context-card-title">
              <strong>Included sources</strong>
              <span>{data.memberName}{data.modelId ? ` · ${data.modelId}` : ''}</span>
            </div>
            <ul className="context-source-list">
              <li><span>Guildhall behavior (rev {data.behaviorRevision})</span><span>{data.behaviorChars} chars</span></li>
              <li><span>Global guidelines ({data.globalGuidelinesHash ? data.globalGuidelinesHash.slice(0, 8) : 'none'})</span><span>{data.globalGuidelinesChars} chars</span></li>
              <li><span>Member instructions</span><span>{data.memberInstructionsChars} chars</span></li>
              <li><span>Retained transcript ({data.retainedHistoryTurns} turns)</span><span>{data.retainedHistoryChars} chars</span></li>
            </ul>
          </div>

          <div className="context-card">
            <div className="context-card-title">
              <strong>Active memory notes ({data.memoryNotes.length})</strong>
              <span>Enabled only</span>
            </div>
            {data.memoryNotes.length === 0 ? (
              <p className="memory-status">No enabled memory notes included for this member.</p>
            ) : (
              <ul className="context-notes-list">
                {data.memoryNotes.map(note => (
                  <li key={note.id}>
                    <span className="memory-source">{note.scope === 'AboutYou' ? 'About you' : 'Member'} · ~{note.estimatedTokens}t</span>
                    <p>{note.preview}</p>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </>
      )}
    </section>
  );
}
