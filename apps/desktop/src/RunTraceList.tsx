import { useState } from 'react';
import type { RunEventView } from './api';

function tierLabel(tier: string): string {
  switch (tier) {
    case 'Tier0': return 'Tier 0';
    case 'Tier1': return 'Tier 1 Read-only';
    case 'Tier2': return 'Tier 2 Write';
    case 'Tier3': return 'Tier 3 External';
    default: return tier;
  }
}

export function RunTraceList({
  runId,
  events,
  busy = false,
  onResolveDecision,
  onRollback,
}: {
  runId?: string;
  events: RunEventView[];
  busy?: boolean;
  onResolveDecision?: (runId: string, eventId: string, approved: boolean) => void;
  onRollback?: (runId: string, eventId: string) => void;
}) {
  const [expandedIds, setExpandedIds] = useState<Record<string, boolean>>({});
  const [confirmRollbackId, setConfirmRollbackId] = useState<string | null>(null);

  if (!events.length) return null;

  return (
    <div className="run-trace-list" aria-label="Tool trace">
      {events.map(ev => {
        const expanded = !!expandedIds[ev.id];
        const isPending = ev.status === 'PendingApproval';
        return (
          <div key={ev.id} className={`run-trace-item status-${ev.status.toLowerCase()}`}>
            <div className="run-trace-header">
              <button
                type="button"
                className="run-trace-toggle"
                aria-expanded={expanded}
                onClick={() => setExpandedIds(curr => ({ ...curr, [ev.id]: !expanded }))}
              >
                <span className="run-trace-chevron" aria-hidden="true">{expanded ? '▾' : '▸'}</span>
                <span className="run-trace-name">{ev.serverId} · {ev.toolName}</span>
                <span className="run-trace-tier">{tierLabel(ev.securityTier)}</span>
                <span className="run-trace-status">
                  {ev.status === 'PendingApproval' ? 'Approval required' : ev.status}
                  {ev.durationMs != null ? ` · ${ev.durationMs} ms` : ''}
                  {ev.rollbackStatus === 'RolledBack' ? ' · Rolled back' : ''}
                </span>
              </button>
              {ev.canRollback && runId && onRollback && (
                confirmRollbackId === ev.id ? (
                  <span className="run-approval-actions">
                    <button
                      type="button"
                      className="danger-button run-rollback-btn"
                      aria-label={`Confirm rollback ${ev.toolName}`}
                      disabled={busy}
                      onClick={() => { setConfirmRollbackId(null); onRollback(runId, ev.id); }}
                    >
                      Confirm
                    </button>
                    <button
                      type="button"
                      className="run-rollback-btn"
                      disabled={busy}
                      onClick={() => setConfirmRollbackId(null)}
                    >
                      Cancel
                    </button>
                  </span>
                ) : (
                  <button
                    type="button"
                    className="run-rollback-btn"
                    aria-label={`Rollback ${ev.toolName}`}
                    disabled={busy}
                    onClick={() => setConfirmRollbackId(ev.id)}
                    title="Restore workspace state before this tool ran"
                  >
                    Rollback
                  </button>
                )
              )}
            </div>
            {isPending && runId && onResolveDecision && (
              <div className="run-approval-card" role="region" aria-label={`Approval required for ${ev.toolName}`}>
                <p>
                  Approve <strong>{ev.toolName}</strong> ({tierLabel(ev.securityTier)})?
                </p>
                <pre className="run-trace-code">{ev.argumentsRedactedJson}</pre>
                <div className="run-approval-actions">
                  <button
                    type="button"
                    disabled={busy}
                    onClick={() => onResolveDecision(runId, ev.id, true)}
                  >
                    Approve
                  </button>
                  <button
                    type="button"
                    className="danger-button"
                    disabled={busy}
                    onClick={() => onResolveDecision(runId, ev.id, false)}
                  >
                    Deny
                  </button>
                </div>
              </div>
            )}
            {expanded && (
              <div className="run-trace-details">
                <div>
                  <small>Arguments (redacted)</small>
                  <pre className="run-trace-code">{ev.argumentsRedactedJson}</pre>
                </div>
                {ev.resultRedactedJson && (
                  <div>
                    <small>Result (redacted)</small>
                    <pre className="run-trace-code">{ev.resultRedactedJson}</pre>
                  </div>
                )}
                {ev.errorCode && <small className="run-trace-error">Error: {ev.errorCode}</small>}
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}
