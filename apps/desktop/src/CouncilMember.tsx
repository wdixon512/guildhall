import { useEffect, useRef, useState } from 'react';
import { Icon } from './Icon';
import { PaneResizeHandle } from './PaneResizeHandle';

export const accents = [
  { name: 'Plum', value: '#71465f' }, { name: 'Slate', value: '#536078' },
  { name: 'Forest', value: '#3d6654' }, { name: 'Bronze', value: '#7f6042' }, { name: 'Sea glass', value: '#54717a' },
];
export type MemberIdentity = { name: string; role: string; portrait: number; customPortrait: string; accent: string; instructions: string };
export const expressions = ['neutral', 'pondering', 'idea', 'reading', 'surprised', 'laughing'] as const;
export type PortraitExpression = typeof expressions[number];
const characters = ['maren', 'scholar', 'artisan', 'researcher'];
const defaults: MemberIdentity = { name: 'Maren', role: 'Steward', portrait: 0, customPortrait: '', accent: accents[0].value, instructions: 'You are a helpful local assistant.' };
export function useMemberIdentity(persist = true) {
  const [identity, setIdentity] = useState<MemberIdentity>(() => {
    try {
      const saved = JSON.parse(localStorage.getItem('guildhall.memberAppearance') ?? 'null');
      if (!saved) return defaults;
      return {
        name: typeof saved.name === 'string' ? saved.name.slice(0, 40) : defaults.name,
        role: typeof saved.role === 'string' ? saved.role.slice(0, 40) : defaults.role,
        portrait: [0, 1, 2, 3].includes(saved.portrait) ? saved.portrait : 0,
        customPortrait: typeof saved.customPortrait === 'string' && saved.customPortrait.length < 1000000 && /^data:image\/(jpeg|png|webp);base64,/.test(saved.customPortrait) ? saved.customPortrait : '',
        accent: accents.some(color => color.value === saved.accent) ? saved.accent : defaults.accent,
        instructions: typeof saved.instructions === 'string' ? saved.instructions.slice(0, 8192) : defaults.instructions,
      };
    } catch { return defaults; }
  });
  const [storageError, setStorageError] = useState('');
  useEffect(() => {
    if (!persist) return;
    try { localStorage.setItem('guildhall.memberAppearance', JSON.stringify(identity)); setStorageError(''); }
    catch { setStorageError('Appearance could not be saved. Changes will last for this session.'); }
  }, [identity, persist]);
  return { identity, setIdentity, storageError };
}

export function Portrait({ identity, large = false, expression = 'neutral' }: { identity: MemberIdentity; large?: boolean; expression?: PortraitExpression }) {
  const index = expressions.indexOf(expression);
  const style = identity.customPortrait ? { backgroundImage: `url(${identity.customPortrait})`, backgroundSize: 'cover', backgroundPosition: 'center' } : expression === 'neutral' ? { backgroundPosition: `${identity.portrait % 2 * 100}% ${Math.floor(identity.portrait / 2) * 100}%` } : { backgroundImage: `url(/assets/${characters[identity.portrait]}-expressions.webp)`, backgroundSize: '300% 200%', backgroundPosition: `${index % 3 * 50}% ${Math.floor(index / 3) * 100}%` };
  return <span key={`${identity.portrait}-${expression}-${!!identity.customPortrait}`} className={`portrait ${large ? 'portrait-large' : ''}`} role="img" aria-label={`${identity.name || 'Maren'} portrait${expression !== 'neutral' && !identity.customPortrait ? `, ${expression}` : ''}`} style={style} />;
}

export function CouncilMember({ identity, onChange, onClose, storageError, closing = false, activity = 'neutral', saving = false, onRetrySave, retryAvailable = false, resizePane }: { identity: MemberIdentity; onChange: (value: MemberIdentity) => void; onClose: () => void; storageError: string; closing?: boolean; activity?: PortraitExpression; saving?: boolean; onRetrySave?: () => void; retryAvailable?: boolean; resizePane?: { value: number; min: number; max: number; onResize: (value: number) => void } }) {
  const picker = useRef<HTMLInputElement>(null);
  const panel = useRef<HTMLElement>(null);
  const [overlay, setOverlay] = useState(() => window.innerWidth < 1280);
  useEffect(() => {
    if (!window.matchMedia) return;
    const media = window.matchMedia('(max-width: 1279px)');
    const change = () => setOverlay(media.matches);
    media.addEventListener('change', change);
    return () => media.removeEventListener('change', change);
  }, []);
  useEffect(() => {
    if (!overlay) return;
    const previous = document.activeElement as HTMLElement | null;
    panel.current?.querySelector<HTMLButtonElement>('button')?.focus();
    return () => { if (previous?.isConnected) previous.focus(); };
  }, [overlay]);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [previewExpression, setPreviewExpression] = useState<PortraitExpression | 'auto'>('auto');
  const uploadVersion = useRef(0);
  const latestIdentity = useRef(identity);
  useEffect(() => { latestIdentity.current = identity; }, [identity]);
  useEffect(() => () => { uploadVersion.current++; }, []);
  async function upload(file?: File) {
    if (!file) return;
    if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type) || file.size > 4 * 1024 * 1024) {
      setError('Choose a PNG, JPEG or WebP image up to 4 MB.'); return;
    }
    const version = ++uploadVersion.current;
    setLoading(true); setError('');
    const url = URL.createObjectURL(file);
    try {
      const image = new Image(); image.src = url; await image.decode();
      const canvas = document.createElement('canvas'); canvas.width = canvas.height = 512;
      const context = canvas.getContext('2d'); if (!context) throw new Error('No image context');
      const edge = Math.min(image.naturalWidth, image.naturalHeight);
      context.drawImage(image, (image.naturalWidth - edge) / 2, (image.naturalHeight - edge) / 2, edge, edge, 0, 0, 512, 512);
      const portrait = canvas.toDataURL('image/jpeg', 0.78);
      if (portrait.length > 350000) throw new Error('portrait_too_large');
      if (version === uploadVersion.current) onChange({ ...latestIdentity.current, customPortrait: portrait });
    } catch (cause) { if (version === uploadVersion.current) setError(cause instanceof Error && cause.message === 'portrait_too_large' ? 'The prepared portrait is too large to save. Choose a simpler or smaller image.' : 'This image could not be opened. Try another image.'); }
    finally { URL.revokeObjectURL(url); if (version === uploadVersion.current) setLoading(false); }
  }
  return <aside id="guild-member-pane" ref={panel} className={`member-panel ${closing ? 'panel-closing' : ''}`} role={overlay ? 'dialog' : undefined} aria-modal={overlay ? true : undefined} aria-label="Guild member" onKeyDown={event => {
    if (event.key !== 'Tab' || !overlay) return;
    const controls = panel.current?.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(.sr-only), select, textarea:not(:disabled), [role="separator"][tabindex="0"]');
    if (!controls?.length) return;
    const first = controls[0], last = controls[controls.length - 1];
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
  }}>
    {resizePane && <PaneResizeHandle className="right-pane-resizer member-resizer" label="Resize Guild Member pane" controls="guild-member-pane" value={resizePane.value} min={resizePane.min} max={resizePane.max} direction={-1} onResize={resizePane.onResize} />}
    <div className="member-heading"><h2>Guild member</h2><button className="icon-button" aria-label="Close guild member" onClick={onClose}><Icon name="close" /></button></div>
    <Portrait identity={identity} large expression={previewExpression === 'auto' ? activity : previewExpression} />
    <div className="member-fields"><label>Name<input maxLength={40} value={identity.name} onChange={event => onChange({ ...identity, name: event.target.value })} /></label><label>Role<input maxLength={40} value={identity.role} onChange={event => onChange({ ...identity, role: event.target.value })} /></label></div>
    <section className="member-section"><h3>Choose a portrait</h3><div className="portrait-options">{['Maren', 'The scholar', 'The artisan', 'The researcher'].map((label, index) => <button key={label} disabled={loading} className={!identity.customPortrait && identity.portrait === index ? 'selected' : ''} aria-label={`Use ${label} portrait`} aria-pressed={!identity.customPortrait && identity.portrait === index} onClick={() => { uploadVersion.current++; onChange({ ...identity, portrait: index, customPortrait: '' }); }}><Portrait identity={{ ...identity, portrait: index, customPortrait: '' }} /></button>)}</div>
      <input ref={picker} type="file" accept="image/png,image/jpeg,image/webp" className="sr-only" aria-label="Choose portrait image" onChange={event => { void upload(event.target.files?.[0]); event.target.value = ''; }} />
      <button className="upload-portrait" disabled={loading} onClick={() => picker.current?.click()}><Icon name="attach" size={18} />{loading ? 'Preparing portrait…' : 'Upload portrait…'}</button>
      {error && <p role="alert" className="profile-error">{error}</p>}
    </section>
    <section className="member-section"><h3>Accent color</h3><div className="accent-options">{accents.map(color => <button key={color.name} style={{ background: color.value }} aria-label={`${color.name} accent`} aria-pressed={identity.accent === color.value} onClick={() => onChange({ ...identity, accent: color.value })} />)}</div></section>
    <section className="member-section member-instructions"><label htmlFor="member-instructions">Instructions / rules</label><textarea id="member-instructions" maxLength={8192} value={identity.instructions} onChange={event => onChange({ ...identity, instructions: event.target.value })} rows={7} /><small>Markdown text included for this member’s future replies. Changes apply to new messages.</small></section>
    <section className="member-section expression-preview"><label>Expressions<select aria-label="Preview expression" value={previewExpression} disabled={!!identity.customPortrait} onChange={event => setPreviewExpression(event.target.value as PortraitExpression | 'auto')}><option value="auto">Follow activity</option>{expressions.map(expression => <option key={expression} value={expression}>{expression.charAt(0).toUpperCase() + expression.slice(1)}</option>)}</select></label><small>{identity.customPortrait ? 'Uploaded portraits stay static.' : 'Preview a pose here. Live replies use pondering, then idea as text arrives. Research and other expressions are reserved for future activity signals.'}</small></section>
    <p className="member-note">This identity and its instructions belong to this guild member and are shared by its sessions.</p>{saving && <p className="member-note" role="status">Saving guild member… Other members are briefly locked while this change saves.</p>}{storageError && <p role="alert" className="profile-error">{storageError}{retryAvailable && onRetrySave && <> <button className="profile-retry" onClick={onRetrySave}>Retry save</button></>}</p>}
  </aside>;
}
