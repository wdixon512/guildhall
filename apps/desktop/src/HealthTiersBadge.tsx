import { useEffect, useRef, useState } from 'react';
import { HostRequestError, requestJson } from './api';
import type { Bootstrap, FiveTierHealthSnapshot } from './api';

interface HealthTiersBadgeProps {
  config: Bootstrap | null;
  hostConnected: boolean;
}

export function HealthTiersBadge({ config, hostConnected }: HealthTiersBadgeProps) {
  const [snapshot, setSnapshot] = useState<FiveTierHealthSnapshot | null>(null);
  const [open, setOpen] = useState(false);
  const [recoveringTier, setRecoveringTier] = useState<string | null>(null);
  const [error, setError] = useState('');
  const wrapRef = useRef<HTMLDivElement | null>(null);
  const triggerRef = useRef<HTMLButtonElement | null>(null);

  useEffect(() => {
    if (!config || !hostConnected) return;
    const ac = new AbortController();

    const sync = async () => {
      try {
        await requestJson<null>(config, '/health/ui-heartbeat', 'POST', ac.signal);
        const data = await requestJson<FiveTierHealthSnapshot>(config, '/health/tiers', 'GET', ac.signal);
        setSnapshot(data);
        setError('');
      } catch {
        // Handled by main host status
      }
    };

    void sync();
    const timer = window.setInterval(() => void sync(), 15000);
    return () => {
      ac.abort();
      window.clearInterval(timer);
    };
  }, [config, hostConnected]);

  useEffect(() => {
    if (!open) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setOpen(false);
        triggerRef.current?.focus();
      }
    };
    const onPointerDown = (event: MouseEvent) => {
      if (wrapRef.current && !wrapRef.current.contains(event.target as Node)) {
        setOpen(false);
      }
    };
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('mousedown', onPointerDown);
    return () => {
      window.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('mousedown', onPointerDown);
    };
  }, [open]);

  async function handleRecover(tierId: string) {
    if (!config) return;
    setRecoveringTier(tierId);
    setError('');
    try {
      const updated = await requestJson<FiveTierHealthSnapshot>(
        config,
        `/health/tiers/${encodeURIComponent(tierId)}/recover`,
        'POST',
        new AbortController().signal,
      );
      setSnapshot(updated);
    } catch (reason) {
      setError(reason instanceof HostRequestError && reason.serverMessage ? reason.serverMessage : `Could not recover ${tierId}.`);
    } finally {
      setRecoveringTier(null);
    }
  }

  const overall = !hostConnected ? 'Degraded' : (snapshot?.overallStatus ?? 'Checking…');

  return (
    <div className="health-tiers-wrap" ref={wrapRef}>
      <button
        ref={triggerRef}
        type="button"
        className={`health-tiers-button ${overall === 'Degraded' ? 'degraded' : ''}`}
        aria-expanded={open}
        aria-label={`System health: ${overall}`}
        onClick={() => setOpen(v => !v)}
      >
        <span aria-hidden="true" className={`dot ${overall === 'Degraded' ? 'offline' : ''}`} />
        <span>5-Tier {overall}</span>
      </button>

      {open && (
        <div className="health-tiers-popover" role="region" aria-label="5-tier health status">
          <div className="health-tiers-popover-head">
            <strong>5-Tier Health</strong>
            <button type="button" aria-label="Close health popover" onClick={() => { setOpen(false); triggerRef.current?.focus(); }}>×</button>
          </div>
          {error && <p className="field-error" role="alert">{error}</p>}
          {snapshot ? (
            <ul className="health-tiers-list">
              {snapshot.tiers.map(tier => (
                <li key={tier.tierId} className="health-tier-row">
                  <div className="health-tier-info">
                    <span aria-hidden="true" className={`dot ${tier.status === 'Healthy' ? '' : 'offline'}`} />
                    <div>
                      <strong>{tier.displayName}</strong>
                      <small>{tier.summary}</small>
                    </div>
                  </div>
                  {tier.canRecover && tier.status !== 'Healthy' && (
                    <button
                      type="button"
                      disabled={recoveringTier === tier.tierId}
                      onClick={() => void handleRecover(tier.tierId)}
                    >
                      {recoveringTier === tier.tierId ? 'Probing…' : 'Retry'}
                    </button>
                  )}
                </li>
              ))}
            </ul>
          ) : (
            <p className="muted">Waiting for host health snapshot…</p>
          )}
        </div>
      )}
    </div>
  );
}
