import { Suspense, lazy, useEffect, useRef, useState } from 'react';
import { request, type Bootstrap, type CustomMcpServerView } from './api';
import { Icon } from './Icon';
import { PaneResizeHandle } from './PaneResizeHandle';
import { StreamingMarkdown } from './StreamingMarkdown';
import { TextEditorModal } from './TextEditorModal';

const MemoryNotesPanel = lazy(() =>
  import('./MemoryNotesPanel').then(module => ({ default: module.MemoryNotesPanel })),
);

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

export function stripMarkdownFrontmatter(markdown: string): string {
  const normalized = markdown.replace(/\r\n/g, '\n');
  if (!normalized.startsWith('---\n')) return markdown.trim();
  const closingIndex = normalized.indexOf('\n---\n', 4);
  if (closingIndex >= 0) return normalized.slice(closingIndex + 5).trim();
  if (normalized.endsWith('\n---')) return '';
  return markdown.trim();
}

export function toInstructionSlug(name: string): string {
  const slug = name.trim().toLowerCase().replace(/^the\s+/, '').replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
  return slug || 'guild-member';
}

const MCP_TOOL_OPTIONS = [
  { id: 'filesystem-mcp', label: 'Local Filesystem (Tier 1/2)' },
  { id: 'git-mcp', label: 'Local Git (Tier 1)' },
  { id: 'workspace-exec-mcp', label: 'Workspace Commands (Tier 2)' },
  { id: 'github-mcp', label: 'GitHub (Tier 1/3)' },
  { id: 'google-drive-mcp', label: 'Google Drive (Tier 1)' },
  { id: 'gmail-mcp', label: 'Gmail (Tier 1/3)' },
  { id: 'sql-server-mcp', label: 'SQL Server Read-Only (Tier 1)' },
] as const;

export function CouncilMember({ identity, onChange, onClose, storageError, closing = false, activity = 'neutral', saving = false, onRetrySave, retryAvailable = false, resizePane, config, profileId, memoryRefreshKey = 0, enabledToolIds = ['filesystem-mcp', 'git-mcp'], permissionPolicy = 'ask_unsafe', onUpdateToolPolicy }: { identity: MemberIdentity; onChange: (value: MemberIdentity) => void; onClose: () => void; storageError: string; closing?: boolean; activity?: PortraitExpression; saving?: boolean; onRetrySave?: () => void; retryAvailable?: boolean; resizePane?: { value: number; min: number; max: number; onResize: (value: number) => void }; config?: Bootstrap | null; profileId?: string; memoryRefreshKey?: number; enabledToolIds?: string[]; permissionPolicy?: string; onUpdateToolPolicy?: (enabledToolIds: string[], permissionPolicy: string) => void }) {
  const picker = useRef<HTMLInputElement>(null);
  const markdownPicker = useRef<HTMLInputElement>(null);
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
  const [instructionsMode, setInstructionsMode] = useState<'edit' | 'preview'>('edit');
  const [instructionsModalOpen, setInstructionsModalOpen] = useState(false);
  const [markdownError, setMarkdownError] = useState('');
  const [previewExpression, setPreviewExpression] = useState<PortraitExpression | 'auto'>('auto');
  const [confirmUnrestricted, setConfirmUnrestricted] = useState(false);
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
  async function importMarkdown(file?: File) {
    if (!file) return;
    if (file.size > 64 * 1024) {
      setMarkdownError('Choose a Markdown file up to 64 KB.');
      return;
    }
    try {
      const raw = await file.text();
      const body = stripMarkdownFrontmatter(raw);
      if (body.length > 8192) {
        setMarkdownError(`Imported Markdown must be 8,192 characters or less (found ${body.length}).`);
        return;
      }
      setMarkdownError('');
      onChange({ ...latestIdentity.current, instructions: body });
    } catch {
      setMarkdownError('This Markdown file could not be read.');
    }
  }
  function exportMarkdown() {
    setMarkdownError('');
    const blob = new Blob([identity.instructions], { type: 'text/markdown;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `${toInstructionSlug(identity.name)}-instructions.md`;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
  }
  const [customMcpOptions, setCustomMcpOptions] = useState<Array<{ id: string; label: string }>>([]);
  useEffect(() => {
    if (!config || !profileId || !onUpdateToolPolicy) return;
    const ac = new AbortController();
    void (request(config, '/mcp/servers/custom', ac.signal) as Promise<CustomMcpServerView[]>)
      .then(list => {
        if (!Array.isArray(list)) return;
        setCustomMcpOptions(
          list
            .filter(s => s.isEnabled)
            .map(s => ({ id: s.id, label: `${s.displayName} (${s.securityTier} · Remote MCP)` })),
        );
      })
      .catch(() => { /* best effort; built-in options remain available */ });
    return () => ac.abort();
  }, [config, profileId, onUpdateToolPolicy]);

  const allToolOptions = [
    ...MCP_TOOL_OPTIONS,
    ...customMcpOptions.filter(c => !MCP_TOOL_OPTIONS.some(b => b.id === c.id)),
  ];

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
    <section className="member-section member-instructions">
      <div className="member-instructions-header">
        {instructionsMode === 'edit' ? (
          <label htmlFor="member-instructions">Instructions / rules</label>
        ) : (
          <span className="member-instructions-label">Instructions / rules</span>
        )}
        <div className="member-instructions-tabs" role="group" aria-label="Instructions view">
          <button type="button" className={instructionsMode === 'edit' ? 'selected' : ''} aria-pressed={instructionsMode === 'edit'} onClick={() => setInstructionsMode('edit')}>Source</button>
          <button type="button" className={instructionsMode === 'preview' ? 'selected' : ''} aria-pressed={instructionsMode === 'preview'} onClick={() => setInstructionsMode('preview')}>Preview</button>
        </div>
      </div>
      {instructionsMode === 'edit' ? (
        <textarea id="member-instructions" maxLength={8192} value={identity.instructions} onChange={event => { setMarkdownError(''); onChange({ ...identity, instructions: event.target.value }); }} rows={7} />
      ) : (
        <div className="member-instructions-preview markdown-body" role="region" aria-label="Instructions preview">
          {identity.instructions.trim() ? <StreamingMarkdown text={identity.instructions} streaming={false} /> : <p className="member-instructions-empty">No instructions yet.</p>}
        </div>
      )}
      <div className="member-instructions-actions">
        <input ref={markdownPicker} type="file" accept=".md,.markdown,text/markdown,text/plain" className="sr-only" aria-label="Import instructions Markdown" onChange={event => { void importMarkdown(event.target.files?.[0]); event.target.value = ''; }} />
        <button type="button" onClick={() => setInstructionsModalOpen(true)}>Expand</button>
        <button type="button" onClick={() => markdownPicker.current?.click()}>Import .md…</button>
        <button type="button" onClick={exportMarkdown} disabled={!identity.instructions}>Export .md</button>
      </div>
      <TextEditorModal
        open={instructionsModalOpen}
        title={`${identity.name || 'Guild member'} — Instructions / rules`}
        subtitle="Markdown text included for this member’s future replies."
        value={identity.instructions}
        maxLength={8192}
        saveLabel="Apply"
        onSave={next => { setMarkdownError(''); onChange({ ...latestIdentity.current, instructions: next }); setInstructionsModalOpen(false); }}
        onClose={() => setInstructionsModalOpen(false)}
      />
      {markdownError && <p role="alert" className="profile-error">{markdownError}</p>}
      <small>Markdown text included for this member’s future replies. Changes apply to new messages. {identity.instructions.length}/8192</small>
    </section>
    <section className="member-section expression-preview"><label>Expressions<select aria-label="Preview expression" value={previewExpression} disabled={!!identity.customPortrait} onChange={event => setPreviewExpression(event.target.value as PortraitExpression | 'auto')}><option value="auto">Follow activity</option>{expressions.map(expression => <option key={expression} value={expression}>{expression.charAt(0).toUpperCase() + expression.slice(1)}</option>)}</select></label><small>{identity.customPortrait ? 'Uploaded portraits stay static.' : 'Preview a pose here. Live replies use pondering, then idea as text arrives. Research and other expressions are reserved for future activity signals.'}</small></section>
    {profileId && onUpdateToolPolicy && (
      <section className="member-section member-tools-policy" aria-label="MCP tools and permissions">
        <h3>Allowed MCP tools</h3>
        <div className="member-tool-checks">
          {allToolOptions.map(opt => {
            const checked = enabledToolIds.includes(opt.id);
            return (
              <label key={opt.id} className="member-tool-check">
                <input
                  type="checkbox"
                  checked={checked}
                  disabled={saving}
                  onChange={() => {
                    const next = checked
                      ? enabledToolIds.filter(id => id !== opt.id)
                      : [...enabledToolIds, opt.id];
                    onUpdateToolPolicy(next, permissionPolicy);
                  }}
                />
                <span>{opt.label}</span>
              </label>
            );
          })}
        </div>
        <label>
          Tool permission policy
          <select
            aria-label="Tool permission policy"
            value={confirmUnrestricted ? 'unrestricted' : permissionPolicy}
            disabled={saving}
            onChange={event => {
              const next = event.target.value;
              if (next === 'unrestricted' && permissionPolicy !== 'unrestricted') {
                setConfirmUnrestricted(true);
                return;
              }
              setConfirmUnrestricted(false);
              onUpdateToolPolicy(enabledToolIds, next);
            }}
          >
            <option value="ask_unsafe">Ask for unsafe actions (Tier 1 auto, Tier 2/3 ask)</option>
            <option value="always_ask">Always ask (every tool call)</option>
            <option value="unrestricted">Unrestricted · danger (auto-run inside workspace)</option>
          </select>
        </label>
        {confirmUnrestricted && (
          <div className="unrestricted-confirm" role="region" aria-label="Confirm unrestricted member policy">
            <p><strong>Danger:</strong> Unrestricted mode auto-approves workspace edits and commands for this member without prompting.</p>
            <div className="settings-choice-row">
              <button
                type="button"
                className="danger-button"
                disabled={saving}
                onClick={() => { setConfirmUnrestricted(false); onUpdateToolPolicy(enabledToolIds, 'unrestricted'); }}
              >
                Enable Unrestricted
              </button>
              <button type="button" disabled={saving} onClick={() => setConfirmUnrestricted(false)}>Cancel</button>
            </div>
          </div>
        )}
      </section>
    )}
    {config && profileId && (
      <section className="member-section">
        <Suspense fallback={<small>Loading member memory…</small>}>
          <MemoryNotesPanel
            config={config}
            scope="Member"
            agentDefinitionId={profileId}
            refreshKey={memoryRefreshKey}
            title="Member memory"
            description="Private notes for this member only, kept across sessions and model switches."
          />
        </Suspense>
      </section>
    )}
    <p className="member-note">This identity and its instructions belong to this guild member and are shared by its sessions.</p>{saving && <p className="member-note" role="status">Saving guild member…</p>}{storageError && <p role="alert" className="profile-error">{storageError}{retryAvailable && onRetrySave && <> <button className="profile-retry" onClick={onRetrySave}>Retry save</button></>}</p>}
  </aside>;
}
