import { useEffect, useState } from 'react';
import { request } from './api';
import type { Bootstrap, GuildhallBehavior } from './api';

export function GuildhallBehaviorSettings({ config }: { config: Bootstrap | null }) {
  const [saved, setSaved] = useState<GuildhallBehavior | null>(null);
  const [draft, setDraft] = useState('');
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const dirty = !!saved && draft !== saved.instructions;

  async function reload() {
    if (!config) return;
    setLoading(true); setError(''); setNotice('');
    try {
      const value = await request(config, '/guildhall-behavior', new AbortController().signal) as GuildhallBehavior;
      if (typeof value.instructions !== 'string' || !Number.isInteger(value.revision)) throw new Error();
      setSaved(value); setDraft(value.instructions);
    } catch { setError('Could not load Guildhall behavior. Check the host and database migration, then retry.'); }
    finally { setLoading(false); }
  }
  useEffect(() => { void reload(); }, [config?.hostUrl, config?.token]);

  async function save() {
    if (!config || !saved || !dirty || !draft.trim() || draft.length > 4000) return;
    setSaving(true); setError(''); setNotice('');
    try {
      const value = await request(config, '/guildhall-behavior', new AbortController().signal, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ instructions: draft, expectedRevision: saved.revision }) }) as GuildhallBehavior;
      setSaved(value); setDraft(value.instructions); setNotice('Saved. New replies will use this behavior.');
    } catch (reason) {
      if (reason instanceof Error && reason.message === 'Host request failed') setError('Could not save. The setting may have changed elsewhere; reload before trying again. Your draft is still here.');
      else setError('Could not save Guildhall behavior. Check the host and retry. Your draft is still here.');
    } finally { setSaving(false); }
  }

  return <section className="settings-section behavior-settings" aria-labelledby="guildhall-behavior-heading">
    <div><h3 id="guildhall-behavior-heading">Guildhall behavior</h3><small>Shared guidance for every member’s new replies. Member instructions still define their individual role.</small></div>
    <label htmlFor="guildhall-behavior-editor">Behavior instructions</label>
    <textarea id="guildhall-behavior-editor" aria-describedby="guildhall-behavior-help" maxLength={4000} rows={10} value={draft} disabled={loading || !saved} onChange={event => { setDraft(event.target.value); setNotice(''); }} />
    <small id="guildhall-behavior-help">Saved to this Guildhall database. Each accepted reply keeps the version that was current when it was queued. {draft.length}/4000</small>
    {error && <p className="settings-error" role="alert">{error}</p>}{notice && <p className="settings-success" role="status">{notice}</p>}
    <div className="behavior-actions"><button onClick={() => void reload()} disabled={!config || loading || saving}>{error.includes('changed elsewhere') ? 'Reload latest version' : 'Reload saved version'}</button><button onClick={() => void save()} disabled={!saved || !dirty || !draft.trim() || draft.length > 4000 || loading || saving}>{saving ? 'Saving…' : 'Save behavior'}</button></div>
  </section>;
}
