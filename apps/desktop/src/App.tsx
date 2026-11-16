import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { bootstrap, parseDiscovery, parseReadiness, request } from './api';
import type { AgentProfile, Bootstrap, Conversation, ConversationDetails, Discovery, Readiness } from './api';
import { MessageAttachment } from './MessageAttachment';
import { StreamingMarkdown } from './StreamingMarkdown';
import { GuildhallBehaviorSettings } from './GuildhallBehaviorSettings';

import { Icon } from './Icon';
import { usePanelPresence } from './usePanelPresence';
import { useTranscriptScroll } from './useTranscriptScroll';
import { CouncilMember, Portrait, useMemberIdentity } from './CouncilMember';
import type { MemberIdentity } from './CouncilMember';
import { formatResponseDuration, isUnread, markConversationRead, queuedPairs, readWatermarks, starterSuggestions, waitingMessages } from './chatPresentation';
import type { CSSProperties } from 'react';
import './style.css';

const sessionTabs = [
  { id: 'git', label: 'Git changes', status: 'No project' },
  { id: 'terminal', label: 'Terminal', status: 'Planned' },
  { id: 'plan', label: 'Plan', status: 'Not linked' },
  { id: 'context', label: 'Context usage', status: 'Planned' },
  { id: 'browser', label: 'Browser', status: 'QC harness' },
  { id: 'side-chat', label: 'Side chat', status: 'Planned' },
  { id: 'automations', label: 'Scheduled tasks', status: 'Disabled' },
] as const;
const futureConnections = ['Claude', 'Cursor', 'ChatGPT / OpenAI', 'GitHub', 'Google Drive', 'Gmail'];
const toMemberIdentity = (profile: AgentProfile): MemberIdentity => ({ name: profile.name, role: profile.role, portrait: profile.portraitId, customPortrait: profile.customPortrait ?? '', accent: profile.accent, instructions: profile.instructions });
const profileMatchesIdentity = (profile: AgentProfile, identity: MemberIdentity) => identity.name === profile.name && identity.role === profile.role && identity.portrait === profile.portraitId && identity.customPortrait === (profile.customPortrait ?? '') && identity.accent === profile.accent && identity.instructions === profile.instructions;
const sameIdentity = (left: MemberIdentity, right: MemberIdentity) => left.name === right.name && left.role === right.role && left.portrait === right.portrait && left.customPortrait === right.customPortrait && left.accent === right.accent && left.instructions === right.instructions;

function AttachmentThumbnail({ file }: { file: File }) {
  const [url, setUrl] = useState('');
  useEffect(() => {
    if (!/^image\/(png|jpeg|gif|webp)$/i.test(file.type) || !URL.createObjectURL) return;
    const preview = URL.createObjectURL(file);
    setUrl(preview);
    return () => URL.revokeObjectURL(preview);
  }, [file]);
  return url ? <img className="pending-image" src={url} alt={`Preview of ${file.name}`} /> : null;
}

export default function App() {
  const { identity, setIdentity, storageError } = useMemberIdentity(false);
  const [showMember, setShowMember] = useState(() => window.innerWidth >= 1280);
  const [conversationMenu, setConversationMenu] = useState<string | null>(null);
  const memberName = identity.name.trim() || 'Maren';
  useEffect(() => {
    const dismiss = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !document.querySelector('[data-member-menu], [aria-labelledby="archive-member-title"], [aria-labelledby="delete-member-title"]')) {
        setShowMember(false); setShowPreview(false); setShowSettings(false); setShowConnections(false); setConversationMenu(null); setMemberMenuId(null);
        if (window.innerWidth < 900) setSidebarCollapsed(true);
      }
    };
    window.addEventListener('keydown', dismiss);
    return () => window.removeEventListener('keydown', dismiss);
  }, []);
  const [uiScale, setUiScale] = useState(() => {
    try { const saved = Number(localStorage.getItem('guildhall.uiScale')); return Number.isFinite(saved) && saved >= 0.8 && saved <= 1.5 ? saved : 1; }
    catch { return 1; }
  });
  const [host, setHost] = useState('Checking host…');
  const [database, setDatabase] = useState<Readiness | null>(null);
  const [models, setModels] = useState<Discovery | null>(null);
  const [busy, setBusy] = useState(false);
  const [databaseError, setDatabaseError] = useState(false);
  const [modelError, setModelError] = useState(false);
  const [conversation, setConversation] = useState<ConversationDetails | null>(null);
  const [chosenModel, setChosenModel] = useState('');
  const [chatBusy, setChatBusy] = useState(false);
  const chatOperation = useRef(false);
  const running = conversation?.messages.some(message => ['Running', 'Cancelling'].includes(message.status)) ?? false;
  const queuedCount = conversation?.messages.filter(message => message.role === 'assistant' && message.status === 'Queued').length ?? 0;
  const hasPendingWork = running || queuedCount > 0;
  const [deliveryMode, setDeliveryMode] = useState<'queue' | 'send_now'>('queue');
  const scrolling = useTranscriptScroll(conversation?.conversation.id ?? null, conversation?.messages);
  const transcript = scrolling.transcript;
  const [draft, setDraft] = useState('');
  const [selectedFiles, setSelectedFiles] = useState<File[]>([]);
  const filePicker = useRef<HTMLInputElement | null>(null);
  const composerInput = useRef<HTMLTextAreaElement | null>(null);
  useLayoutEffect(() => {
    if (!composerInput.current) return;
    composerInput.current.style.height = 'auto';
    composerInput.current.style.height = `${Math.min(160, Math.max(30, composerInput.current.scrollHeight))}px`;
  }, [draft]);
  const hasDraft = !!draft.trim() || selectedFiles.length > 0;
  const [chatError, setChatError] = useState('');
  const [copiedMessageId, setCopiedMessageId] = useState<string | null>(null);
  const [history, setHistory] = useState<Conversation[]>([]);
  const anyKnownPendingWork = hasPendingWork || history.some(item => item.hasActiveRun);
  const [readState, setReadState] = useState(() => readWatermarks());
  const [profiles, setProfiles] = useState<AgentProfile[]>([]);
  const [profileDrafts, setProfileDrafts] = useState<Record<string, MemberIdentity>>({});
  const profileDraftsRef = useRef<Record<string, MemberIdentity>>({});
  const failedProfileDrafts = useRef<Record<string, { draft: MemberIdentity; retry: number }>>({});
  const [selectedAgentId, setSelectedAgentId] = useState<string | null>(null);
  const [profileSavingId, setProfileSavingId] = useState<string | null>(null);
  const [profileError, setProfileError] = useState('');
  const [profileErrors, setProfileErrors] = useState<Record<string, string>>({});
  const [profileRetryById, setProfileRetryById] = useState<Record<string, number>>({});
  const [search, setSearch] = useState('');
  const [showArchived, setShowArchived] = useState(false);
  const [memberMenuId, setMemberMenuId] = useState<string | null>(null);
  const [archiveMemberTarget, setArchiveMemberTarget] = useState<AgentProfile | null>(null);
  const [deleteMemberTarget, setDeleteMemberTarget] = useState<AgentProfile | null>(null);
  const [deleteMemberError, setDeleteMemberError] = useState('');
  const deleteMemberCancel = useRef<HTMLButtonElement | null>(null);
  const archiveMemberDialog = useRef<HTMLElement | null>(null);
  const memberMenuAction = useRef<HTMLButtonElement | null>(null);
  const memberReturnFocus = useRef<HTMLElement | null>(null);
  const [archiveMemberError, setArchiveMemberError] = useState('');
  const [showArchivedMembers, setShowArchivedMembers] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<Conversation | null>(null);
  const [deleteError, setDeleteError] = useState('');
  const [renameTarget, setRenameTarget] = useState<Conversation | null>(null);
  const [renameDraft, setRenameDraft] = useState('');
  const [renameError, setRenameError] = useState('');
  const deleteDialog = useRef<HTMLElement | null>(null);
  const cancelDeleteButton = useRef<HTMLButtonElement | null>(null);
  const [showConnections, setShowConnections] = useState(false);
  const [showSettings, setShowSettings] = useState(false);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(() => window.innerWidth < 900);
  const [showPreview, setShowPreview] = useState(false);
  const memberPanel = usePanelPresence(showMember);
  const toolsPanel = usePanelPresence(showPreview);
  const [previewTab, setPreviewTab] = useState<typeof sessionTabs[number]['id']>('git');
  const configuration = useRef<Bootstrap | null>(null);
  const profileMutation = useRef(false);
  const pending = useRef<AbortController | null>(null);

  async function refresh() {
    pending.current?.abort();
    const controller = new AbortController();
    pending.current = controller;
    const timeout = setTimeout(() => controller.abort(), 7000);
    setBusy(true); setHost('Checking host…'); setDatabase(null); setModels(null);
    setDatabaseError(false); setModelError(false);
    try {
      configuration.current ??= await bootstrap();
      if (controller.signal.aborted) return;
      const config = configuration.current;
      const health = await request(config, '/health', controller.signal);
      if (health === null || typeof health !== 'object' || !('status' in health) || health.status !== 'ok' ||
        !('apiVersion' in health) || health.apiVersion !== 1) throw new Error('Invalid health');
      setHost('Connected');
      await Promise.all([
        request(config, '/ready', controller.signal).then(parseReadiness).then(value => {
          if (pending.current === controller) {
            setDatabase(value);
            if (value.reason === 'migration_required') setChatError('The local database needs an approved update before chats can open.');
            else if (value.status === 'ready') setChatError(current => current === 'The local database needs an approved update before chats can open.' ? '' : current);
          }
        }).catch(() => { if (pending.current === controller) setDatabaseError(true); }),
        request(config, '/models', controller.signal).then(parseDiscovery).then(value => {
          if (pending.current === controller) {
            setModels(value);
            if (!conversation && value.status === 'available') {
              const defaultModel = value.models.find(model => model.id === 'qwen3:1.7b');
              if (defaultModel) setChosenModel(current => current || defaultModel.id);
            }
          }
        }).catch(() => { if (pending.current === controller) setModelError(true); }),
      ]);
    } catch {
      if (pending.current === controller) setHost('Disconnected — check the development launcher, then refresh.');
    } finally {
      clearTimeout(timeout);
      if (pending.current === controller) setBusy(false);
    }
  }

  useEffect(() => { void refresh(); return () => { pending.current?.abort(); pending.current = null; }; }, []);

  useEffect(() => {
    document.documentElement.style.zoom = String(uiScale);
    document.documentElement.style.setProperty('--ui-scale', String(uiScale));
    try { localStorage.setItem('guildhall.uiScale', String(uiScale)); } catch { /* Scaling still works for this session. */ }
  }, [uiScale]);

  useEffect(() => {
    const handleZoom = (event: KeyboardEvent) => {
      if (!(event.ctrlKey || event.metaKey) || !['+', '=', '-', '_', '0'].includes(event.key)) return;
      event.preventDefault();
      setUiScale(current => event.key === '0' ? 1 : Math.min(1.5, Math.max(0.8, Math.round((current + (event.key === '-' || event.key === '_' ? -0.1 : 0.1)) * 10) / 10)));
    };
    document.addEventListener('keydown', handleZoom);
    return () => document.removeEventListener('keydown', handleZoom);
  }, []);

  useEffect(() => {
    if (!memberMenuId) return;
    memberMenuAction.current?.focus();
    const dismiss = (event: PointerEvent) => {
      if (!(event.target instanceof Element) || !event.target.closest('[data-member-menu]')) setMemberMenuId(null);
    };
    document.addEventListener('pointerdown', dismiss);
    return () => document.removeEventListener('pointerdown', dismiss);
  }, [memberMenuId]);

  useEffect(() => {
    if (!archiveMemberTarget) return;
    const dialog = archiveMemberDialog.current;
    dialog?.querySelector<HTMLButtonElement>('button')?.focus();
    const keepFocus = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault(); event.stopImmediatePropagation();
        if (!chatOperation.current) { setArchiveMemberTarget(null); memberReturnFocus.current?.focus(); }
      }
      if (event.key !== 'Tab') return;
      const buttons = dialog?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)');
      if (!buttons?.length) { event.preventDefault(); return; }
      const first = buttons[0]; const last = buttons[buttons.length - 1];
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    document.addEventListener('keydown', keepFocus, true);
    return () => document.removeEventListener('keydown', keepFocus, true);
  }, [archiveMemberTarget]);

  useEffect(() => {
    if (!deleteMemberTarget) return;
    deleteMemberCancel.current?.focus();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !chatBusy) { event.preventDefault(); setDeleteMemberTarget(null); memberReturnFocus.current?.focus(); return; }
      if (event.key !== 'Tab') return;
      const buttons = document.querySelectorAll<HTMLButtonElement>('[aria-labelledby="delete-member-title"] button:not(:disabled)');
      if (!buttons.length) { event.preventDefault(); return; }
      if (event.shiftKey && document.activeElement === buttons[0]) { event.preventDefault(); buttons[buttons.length - 1].focus(); }
      else if (!event.shiftKey && document.activeElement === buttons[buttons.length - 1]) { event.preventDefault(); buttons[0].focus(); }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [deleteMemberTarget, chatBusy]);

  useEffect(() => {
    if (!deleteTarget) return;
    cancelDeleteButton.current?.focus();
    const keepDialogFocus = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !chatBusy) { event.preventDefault(); setDeleteTarget(null); return; }
      if (event.key !== 'Tab') return;
      const buttons = deleteDialog.current?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)');
      if (!buttons?.length) return;
      const first = buttons[0]; const last = buttons[buttons.length - 1];
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    document.addEventListener('keydown', keepDialogFocus);
    return () => document.removeEventListener('keydown', keepDialogFocus);
  }, [deleteTarget, chatBusy]);

  useEffect(() => {
    if (database?.status !== 'ready' || !configuration.current) return;
    const controller = new AbortController();
    void request(configuration.current, `/conversations?limit=25&archived=${showArchived}`, controller.signal).then(value => {
      if (Array.isArray(value) && !controller.signal.aborted) setHistory(value);
    }).catch(() => {});
    return () => controller.abort();
  }, [database?.status, conversation?.conversation.id, showArchived]);

  useEffect(() => {
    if (database?.status !== 'ready' || !configuration.current) return;
    let stopped = false;
    const refreshSummaries = async () => {
      try {
        const value = await request(configuration.current!, `/conversations?limit=25&archived=${showArchived}`, new AbortController().signal);
        if (!stopped && Array.isArray(value)) setHistory(value as Conversation[]);
      } catch { /* Keep the last committed list during a temporary connection issue. */ }
    };
    const onFocus = () => {
      if (document.visibilityState !== 'visible') return;
      void refreshSummaries();
      const selected = conversation?.conversation;
      if (selected?.id && selected.lastResponseUtc) { markConversationRead(selected.id, selected.lastResponseUtc); setReadState(readWatermarks()); }
    };
    const timer = anyKnownPendingWork ? window.setInterval(() => void refreshSummaries(), 3000) : undefined;
    window.addEventListener('focus', onFocus); document.addEventListener('visibilitychange', onFocus);
    return () => { stopped = true; if (timer) window.clearInterval(timer); window.removeEventListener('focus', onFocus); document.removeEventListener('visibilitychange', onFocus); };
  }, [database?.status, showArchived, anyKnownPendingWork, conversation?.conversation.id, conversation?.conversation.lastResponseUtc]);

  useEffect(() => {
    const config = configuration.current;
    if (database?.status !== 'ready' || !config) return;
    const controller = new AbortController();
    void request(config, '/agents', controller.signal).then(async value => {
      if (!Array.isArray(value) || controller.signal.aborted) return;
      let loaded = value as AgentProfile[];
      let legacy: Partial<MemberIdentity> | null = null;
      try { legacy = JSON.parse(localStorage.getItem('guildhall.memberAppearance') ?? 'null'); } catch { /* Ignore malformed old device preferences. */ }
      const importMarker = 'guildhall.profileImported.v1';
      if (legacy && loaded[0] && localStorage.getItem(importMarker) !== 'done') {
        // The former preference was one shared identity. Import it into one profile only;
        // copying it into every migrated profile would recreate the cross-member coupling.
        const profile = loaded[0];
        const supportedPortrait = typeof legacy.customPortrait === 'string' && /^data:image\/(jpeg|png|webp);base64,/.test(legacy.customPortrait) && legacy.customPortrait.length <= 350000 ? legacy.customPortrait : profile.customPortrait;
        const requestBody = { name: typeof legacy.name === 'string' ? legacy.name.slice(0, 40) : profile.name, role: typeof legacy.role === 'string' ? legacy.role.slice(0, 40) : profile.role, portraitId: Number.isInteger(legacy.portrait) && legacy.portrait! >= 0 && legacy.portrait! <= 3 ? legacy.portrait : profile.portraitId, customPortrait: supportedPortrait, accent: typeof legacy.accent === 'string' ? legacy.accent : profile.accent, instructions: profile.instructions, defaultModelId: profile.defaultModelId, expectedRevision: profile.revision };
        try {
          const migrated = await request(config, `/agents/${profile.id}`, controller.signal, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(requestBody) }) as AgentProfile;
          loaded = loaded.map(item => item.id === migrated.id ? migrated : item);
          localStorage.setItem(importMarker, 'done');
        } catch { if (!controller.signal.aborted) setProfileError('Could not import the previous appearance into the first guild member. Check the host permissions and refresh.'); }
      }
      if (controller.signal.aborted) return;
      setProfiles(loaded);
      const firstActive = loaded.find(profile => !profile.isArchived);
      if (!conversation && !selectedAgentId && firstActive) { setSelectedAgentId(firstActive.id); setIdentity(profileDraftsRef.current[firstActive.id] ?? toMemberIdentity(firstActive)); }
    }).catch(() => { if (!controller.signal.aborted) setProfileError('Could not load guild members. Check the approved database update and refresh.'); });
    return () => controller.abort();
  }, [database?.status]);

  const activeAgentId = conversation?.agent?.id ?? selectedAgentId;
  const activeProfile = profiles.find(profile => profile.id === activeAgentId) ?? conversation?.agent;
  const profileSaving = profileSavingId === activeProfile?.id;
  const profileDirty = !!activeProfile && !!profileDrafts[activeProfile.id] && !profileMatchesIdentity(activeProfile, profileDrafts[activeProfile.id]);
  const profileHasPendingChanges = (profileId: string) => {
    const profile = profiles.find(item => item.id === profileId) ?? (conversation?.agent?.id === profileId ? conversation.agent : undefined);
    const draft = profileDrafts[profileId];
    return !!profile && (!!draft && !profileMatchesIdentity(profile, draft) || profileSavingId === profileId);
  };
  const profileErrorForActive = activeProfile ? profileErrors[activeProfile.id] || profileError : profileError;
  const updateMemberIdentity = (next: MemberIdentity) => {
    setProfileError('');
    setIdentity(next);
    if (!activeProfile) return;
    delete failedProfileDrafts.current[activeProfile.id];
    setProfileErrors(current => { const remaining = { ...current }; delete remaining[activeProfile.id]; return remaining; });
    const updated = { ...profileDraftsRef.current, [activeProfile.id]: next };
    profileDraftsRef.current = updated;
    setProfileDrafts(updated);
  };
  const identityForProfile = (profile: AgentProfile) => profileDraftsRef.current[profile.id] ?? toMemberIdentity(profile);
  useEffect(() => {
    if (database?.status !== 'ready' || !configuration.current || profileMutation.current) return;
    const pending = profiles.flatMap(profile => {
      const draft = profileDrafts[profile.id];
      const failed = failedProfileDrafts.current[profile.id];
      const failedAtCurrentRevision = failed && sameIdentity(failed.draft, draft ?? toMemberIdentity(profile)) && failed.retry === (profileRetryById[profile.id] ?? 0);
      return draft && !profileMatchesIdentity(profile, draft) && !failedAtCurrentRevision ? [{ profile, draft }] : [];
    });
    if (!pending.length) return;
    const timer = window.setTimeout(async () => {
      if (profileMutation.current) return;
      profileMutation.current = true;
      setProfileError('');
      try {
        for (const { profile, draft } of pending) {
          setProfileSavingId(profile.id);
          try {
            const updated = await request(configuration.current!, `/agents/${profile.id}`, new AbortController().signal, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name: draft.name, role: draft.role, portraitId: draft.portrait, customPortrait: draft.customPortrait || null, accent: draft.accent, instructions: draft.instructions, defaultModelId: profile.defaultModelId, expectedRevision: profile.revision }) }) as AgentProfile;
            delete failedProfileDrafts.current[profile.id];
            setProfileErrors(current => { const remaining = { ...current }; delete remaining[profile.id]; return remaining; });
            setProfiles(items => items.map(item => item.id === profile.id ? updated : item));
            setConversation(current => current?.agent?.id === profile.id ? { ...current, agent: updated } : current);
            setProfileDrafts(current => {
              const latest = current[profile.id];
              if (!latest || !sameIdentity(latest, draft)) return current;
              const remaining = { ...current };
              delete remaining[profile.id];
              profileDraftsRef.current = remaining;
              return remaining;
            });
          } catch {
            failedProfileDrafts.current[profile.id] = { draft, retry: profileRetryById[profile.id] ?? 0 };
            setProfileErrors(current => ({ ...current, [profile.id]: 'Could not save this guild member. The draft is kept; retry the save before sending.' }));
          }
        }
      } finally { profileMutation.current = false; setProfileSavingId(null); }
    }, 450);
    return () => clearTimeout(timer);
  }, [profileDrafts, profiles, database?.status, profileRetryById, profileSavingId]);

  async function openSavedChat(id: string) {
    if (!configuration.current || chatOperation.current || hasDraft) return;
    if (database?.status !== 'ready') { setChatError('The local database needs its approved update before this chat can open.'); return; }
    chatOperation.current = true; setChatBusy(true); setChatError('');
    try {
      const details = await request(configuration.current, `/conversations/${id}`, new AbortController().signal) as ConversationDetails;
      setConversation(details); setChosenModel(details.conversation.selectedModelId ?? '');
      markConversationRead(id, details.conversation.lastResponseUtc); setReadState(readWatermarks());
      if (details.agent) { setSelectedAgentId(details.agent.id); setIdentity(identityForProfile(details.agent)); }
      setConversationMenu(null);
      if (window.innerWidth < 900) setSidebarCollapsed(true);
      setHistory(items => items.map(item => item.id === id ? details.conversation : item));
    } catch { setChatError(database?.reason === 'migration_required' ? 'The local database needs its approved update before chats can open.' : 'Could not open the saved conversation. Try again.'); }
    finally { chatOperation.current = false; setChatBusy(false); }
  }

  async function archiveConversation(item: Conversation) {
    if (!configuration.current || chatOperation.current || item.hasActiveRun) return;
    chatOperation.current = true; setChatBusy(true); setChatError('');
    try {
      const updated = await request(configuration.current, `/conversations/${item.id}`, new AbortController().signal, {
        method: 'PATCH', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ isArchived: !item.isArchived, expectedRevision: item.revision }),
      }) as Conversation;
      setHistory(items => items.filter(row => row.id !== item.id));
      if (conversation?.conversation.id === item.id) setConversation({ ...conversation, conversation: updated });
    } catch { setChatError('Could not update this conversation. Refresh the list and try again.'); }
    finally { chatOperation.current = false; setChatBusy(false); }
  }

  async function deleteConversation() {
    if (!configuration.current || !deleteTarget || chatOperation.current || deleteTarget.hasActiveRun) return;
    chatOperation.current = true; setChatBusy(true); setDeleteError('');
    try {
      await request(configuration.current, `/conversations/${deleteTarget.id}`, new AbortController().signal, { method: 'DELETE' });
      setHistory(items => items.filter(row => row.id !== deleteTarget.id));
      if (conversation?.conversation.id === deleteTarget.id) setConversation(null);
      setDeleteTarget(null);
    } catch { setDeleteError('Could not delete this conversation. It may have an active response; refresh and try again when it has finished.'); }
    finally { chatOperation.current = false; setChatBusy(false); }
  }

  async function renameConversation() {
    if (!configuration.current || !renameTarget || chatOperation.current || !renameDraft.trim()) return;
    chatOperation.current = true; setChatBusy(true); setRenameError('');
    try {
      const updated = await request(configuration.current, `/conversations/${renameTarget.id}`, new AbortController().signal, {
        method: 'PATCH', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ title: renameDraft.trim(), expectedRevision: renameTarget.revision }),
      }) as Conversation;
      setHistory(items => items.map(item => item.id === updated.id ? updated : item));
      setConversation(current => current?.conversation.id === updated.id ? { ...current, conversation: updated } : current);
      setRenameTarget(null);
    } catch { setRenameError('Could not rename this chat. Refresh the list and try again.'); }
    finally { chatOperation.current = false; setChatBusy(false); }
  }

  async function stopResponse() {
    const message = conversation?.messages.find(item => item.role === 'assistant' && ['Running', 'Cancelling'].includes(item.status));
    if (!configuration.current || !conversation || !message?.runId || chatOperation.current) return;
    chatOperation.current = true; setChatBusy(true); setChatError('');
    try {
      await request(configuration.current, `/runs/${message.runId}/cancel`, new AbortController().signal, { method: 'POST' });
      const updated = await request(configuration.current, `/conversations/${conversation.conversation.id}`, new AbortController().signal) as ConversationDetails;
      setConversation(updated);
      setHistory(items => items.map(item => item.id === updated.conversation.id ? updated.conversation : item));
    } catch { setChatError('Could not stop the response. Check the host connection and try again.'); }
    finally { chatOperation.current = false; setChatBusy(false); }
  }

  async function removeQueuedMessage(runId?: string) {
    if (!configuration.current || !conversation || !runId || chatOperation.current) return;
    chatOperation.current = true; setChatBusy(true);
    try {
      await request(configuration.current, `/runs/${runId}/cancel`, new AbortController().signal, { method: 'POST' });
      const updated = await request(configuration.current, `/conversations/${conversation.conversation.id}`, new AbortController().signal) as ConversationDetails;
      setConversation(updated);
    } catch { setChatError('Could not remove the queued message. Try again.'); }
    finally { chatOperation.current = false; setChatBusy(false); }
  }

  async function promoteQueuedMessage(runId?: string) {
    if (!configuration.current || !conversation || !runId || chatOperation.current) return;
    chatOperation.current = true; setChatBusy(true); setChatError('');
    try {
      await request(configuration.current, `/runs/${runId}/promote`, new AbortController().signal, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ expectedConversationRevision: conversation.conversation.revision }) });
      const updated = await request(configuration.current, `/conversations/${conversation.conversation.id}`, new AbortController().signal) as ConversationDetails;
      setConversation(updated); setHistory(items => items.map(item => item.id === updated.conversation.id ? updated.conversation : item));
    } catch { setChatError('Could not send this queued message now. Refresh the conversation and try again.'); }
    finally { chatOperation.current = false; setChatBusy(false); }
  }

  useEffect(() => {
    const conversationId = conversation?.conversation?.id;
    if (!conversationId || !configuration.current || !hasPendingWork) return;
    let stopped = false;
    let refreshing = false;
    const controller = new AbortController();
    const poll = async () => {
      if (refreshing || stopped) return;
      refreshing = true;
      try {
        const latest = await request(configuration.current!, `/conversations/${conversationId}`, controller.signal) as ConversationDetails;
        if (!stopped) {
          setConversation(current => current?.conversation.id === conversationId && latest.conversation.revision >= current.conversation.revision ? latest : current);
          if (document.visibilityState === 'visible' && latest.conversation.lastResponseUtc) {
            markConversationRead(conversationId, latest.conversation.lastResponseUtc); setReadState(readWatermarks());
          }
          setHistory(items => items.map(item => item.id === conversationId && latest.conversation.revision >= item.revision ? latest.conversation : item));
        }
      } catch { if (!stopped) setChatError('Could not refresh this response. Try opening the conversation again.'); }
      finally { refreshing = false; }
    };
    const timer = setInterval(() => void poll(), 250);
    return () => { stopped = true; controller.abort(); clearInterval(timer); };
  }, [conversation?.conversation.id, hasPendingWork]);

  async function addGuildMember() {
    if (!configuration.current || database?.status !== 'ready' || chatOperation.current) return;
    chatOperation.current = true; setChatBusy(true); setProfileError('');
    try {
      const profile = await request(configuration.current, '/agents', new AbortController().signal, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name: 'New guild member', role: 'Steward', portraitId: 0, customPortrait: null, accent: '#71465f', instructions: 'You are a helpful local assistant.', defaultModelId: chosenModel || null }) }) as AgentProfile;
      setProfiles(items => [...items, profile]); setSelectedAgentId(profile.id); setIdentity(identityForProfile(profile)); setConversation(null); setChosenModel(profile.defaultModelId ?? chosenModel); setShowMember(true);
    } catch { setProfileError('Could not add a guild member. Check the local database permissions.'); }
    finally { chatOperation.current = false; setChatBusy(false); }
  }

  async function setGuildMemberArchived(profile: AgentProfile, isArchived: boolean) {
    if (!configuration.current || database?.status !== 'ready' || chatOperation.current || profileHasPendingChanges(profile.id)) return;
    chatOperation.current = true; setChatBusy(true); setArchiveMemberError('');
    try {
      const updated = await request(configuration.current, `/agents/${profile.id}/lifecycle`, new AbortController().signal, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ isArchived, expectedRevision: profile.revision }) }) as AgentProfile;
      setProfiles(items => items.map(item => item.id === updated.id ? updated : item)); setMemberMenuId(null); setArchiveMemberTarget(null);
      setConversation(current => current?.agent?.id === updated.id ? { ...current, agent: updated } : current);
      setShowArchivedMembers(true);
      requestAnimationFrame(() => document.getElementById(`member-actions-${updated.id}`)?.focus());
       if (!isArchived && selectedAgentId === profile.id && !conversation) { setIdentity(identityForProfile(updated)); }
      if (isArchived && selectedAgentId === profile.id && !conversation) {
        const nextProfile = profiles.find(item => item.id !== profile.id && !item.isArchived);
        setSelectedAgentId(nextProfile?.id ?? null);
         if (nextProfile) { setIdentity(identityForProfile(nextProfile)); setChosenModel(nextProfile.defaultModelId ?? chosenModel); }
      }
    } catch { setArchiveMemberError('Could not update this guild member. Wait for running or queued replies to finish, then try again. If this member changed elsewhere, reopen Guildhall.'); }
    finally { chatOperation.current = false; setChatBusy(false); }
  }

  async function deleteGuildMember(profile: AgentProfile) {
    if (!configuration.current || chatOperation.current || hasDraft || profileHasPendingChanges(profile.id)) return;
    chatOperation.current = true; setChatBusy(true); setDeleteMemberError('');
    try {
      await request(configuration.current, `/agents/${profile.id}?expectedRevision=${profile.revision}`, new AbortController().signal, { method: 'DELETE' });
      setProfiles(items => items.filter(item => item.id !== profile.id));
      setHistory(items => items.filter(item => item.agentDefinitionId !== profile.id));
      if (conversation?.agent?.id === profile.id) { setConversation(null); setChosenModel(''); }
      const next = profiles.find(item => item.id !== profile.id && !item.isArchived);
      if (selectedAgentId === profile.id || conversation?.agent?.id === profile.id) {
        setSelectedAgentId(next?.id ?? null);
         if (next) { setIdentity(identityForProfile(next)); setChosenModel(next.defaultModelId ?? chosenModel); }
      }
      setMemberMenuId(null); setDeleteMemberTarget(null);
      requestAnimationFrame(() => document.getElementById(`member-actions-${next?.id ?? ''}`)?.focus());
    } catch { setDeleteMemberError('Could not delete this member. Make sure its replies have finished, then refresh and try again.'); }
    finally { chatOperation.current = false; setChatBusy(false); }
  }

  async function openChat(createNew = false, agentDefinitionId = activeAgentId) {
    if (!configuration.current || chatOperation.current || hasDraft) return;
    const selectedProfile = profiles.find(profile => profile.id === agentDefinitionId);
    if (createNew && (!selectedProfile || selectedProfile.isArchived)) { setChatError('Choose or restore an active guild member before starting a new session.'); return; }
    chatOperation.current = true; setChatBusy(true);
    setChatError('');
    try {
      const list = createNew ? [] : await request(configuration.current, '/conversations?limit=1', new AbortController().signal) as Conversation[];
      if (!list.length && (!selectedProfile || selectedProfile.isArchived)) { setChatError('Choose or restore an active guild member before starting a new session.'); return; }
      const item = list[0] ?? await request(configuration.current, '/conversations', new AbortController().signal, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ title: 'New conversation', modelId: selectedProfile?.defaultModelId || chosenModel || null, agentDefinitionId }) }) as Conversation;
      const details = await request(configuration.current, `/conversations/${item.id}`, new AbortController().signal) as ConversationDetails;
      setConversation(details);
      setChosenModel(details.conversation.selectedModelId ?? '');
       if (details.agent) { setSelectedAgentId(details.agent.id); setIdentity(identityForProfile(details.agent)); }
      if (window.innerWidth < 900) setSidebarCollapsed(true);
    } catch { setChatError('Could not open chat. Check the host and database, then try again.'); }
    finally { chatOperation.current = false; setChatBusy(false); }
  }
  async function selectModel(modelId: string) {
    if (chatOperation.current) return;
    if (!conversation || !configuration.current) { setChosenModel(modelId); return; }
    chatOperation.current = true; setChatBusy(true); setChatError('');
    try {
      const updated = await request(configuration.current, `/conversations/${conversation.conversation.id}`, new AbortController().signal, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ modelId, expectedRevision: conversation.conversation.revision }) }) as Conversation;
      setConversation({ ...conversation, conversation: updated }); setChosenModel(updated.selectedModelId ?? '');
      setHistory(items => items.map(item => item.id === updated.id ? updated : item));
    } catch { setChatError('Model change was not saved. Reopen chat to refresh its settings.'); }
    finally { chatOperation.current = false; setChatBusy(false); }
  }
  async function sendMessage() {
    if (!conversation && (!activeProfile || activeProfile.isArchived)) return;
    if (activeProfile && profileHasPendingChanges(activeProfile.id)) { setChatError('This member is still saving. Wait for the save to finish before sending.'); return; }
    if (queuedCount >= 8) { setChatError('Eight messages are already queued. Remove one or wait for a reply.'); return; }
    const modelId = conversation?.conversation.selectedModelId ?? chosenModel;
    if (!configuration.current || database?.status !== 'ready' || (!draft.trim() && selectedFiles.length === 0) || chatOperation.current || !modelId || !models?.models.some(model => model.id === modelId)) return;
    chatOperation.current = true; setChatBusy(true);
    scrolling.pinToBottom();
    const text = draft; setDraft(''); setChatError('');
    try {
      let target = conversation;
      if (!target) {
        const created = await request(configuration.current, '/conversations', new AbortController().signal, {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ title: 'New conversation', modelId, agentDefinitionId: activeAgentId }),
        }) as Conversation;
        target = await request(configuration.current, `/conversations/${created.id}`, new AbortController().signal) as ConversationDetails;
        setConversation(target);
         if (target.agent) { setSelectedAgentId(target.agent.id); setIdentity(identityForProfile(target.agent)); }
        setHistory(items => [created, ...items.filter(item => item.id !== created.id)]);
      }
      target = await request(configuration.current, `/conversations/${target.conversation.id}`, new AbortController().signal) as ConversationDetails;
      const clientRequestId = crypto.randomUUID();
      const init: RequestInit = { method: 'POST' };
      if (selectedFiles.length) {
        const form = new FormData();
        form.set('clientRequestId', clientRequestId); form.set('expectedConversationRevision', String(target.conversation.revision)); form.set('text', text); form.set('deliveryMode', deliveryMode);
        selectedFiles.forEach(file => form.append('attachments', file, file.name));
        init.body = form;
      } else {
        init.headers = { 'Content-Type': 'application/json' };
        init.body = JSON.stringify({ clientRequestId, expectedConversationRevision: target.conversation.revision, text, deliveryMode });
      }
      await request(configuration.current, `/conversations/${target.conversation.id}/messages`, new AbortController().signal, init);
      const updated = await request(configuration.current, `/conversations/${target.conversation.id}`, new AbortController().signal) as ConversationDetails;
      setConversation(updated); setSelectedFiles([]);
      setHistory(items => items.map(item => item.id === updated.conversation.id ? updated.conversation : item));
    } catch { setDraft(text); setChatError('Message could not be sent.'); }
    finally { chatOperation.current = false; setChatBusy(false); }
  }

  async function copyMessage(id: string, text: string) {
    try { await navigator.clipboard.writeText(text); setCopiedMessageId(id); window.setTimeout(() => setCopiedMessageId(current => current === id ? null : current), 1400); }
    catch { setChatError('Could not copy this message. Check clipboard access for Guildhall.'); }
  }

  function handleComposerKeyDown(event: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      void sendMessage();
    }
  }

  function addAttachments(files: File[]) {
    if (chatBusy || database?.status !== 'ready') {
      setChatError('Wait for the current action to finish before attaching files.');
      return;
    }
    const combined = [...selectedFiles, ...files];
    if (combined.length > 5) { setChatError('Attach up to five files per message.'); return; }
    if (combined.some(file => file.size > 20 * 1024 * 1024) || combined.reduce((sum, file) => sum + file.size, 0) > 50 * 1024 * 1024) {
      setChatError('Each file can be up to 20 MB and all files together up to 50 MB.'); return;
    }
    setSelectedFiles(combined); setChatError('');
  }

  function pasteIntoComposer(event: React.ClipboardEvent<HTMLTextAreaElement>) {
    const files = Array.from(event.clipboardData.items ?? [])
      .filter(item => item.kind === 'file').map(item => item.getAsFile()).filter((file): file is File => file !== null);
    if (!files.length) files.push(...Array.from(event.clipboardData.files ?? []));
    // Leave ordinary text pastes and native selection replacement untouched.
    if (!files.length) return;
    event.preventDefault();
    addAttachments(files);
    const text = event.clipboardData.getData('text/plain');
    if (text) {
      const textarea = event.currentTarget;
      const { selectionStart, selectionEnd } = textarea;
      setDraft(value => value.slice(0, selectionStart) + text + value.slice(selectionEnd));
      window.setTimeout(() => {
        if (document.activeElement === textarea) textarea.setSelectionRange(selectionStart + text.length, selectionStart + text.length);
      }, 0);
    }
  }

  const databaseLabel = databaseError ? 'Could not read database status.' : !database ? 'Not checked' :
    database.status === 'ready' ? 'Ready' : database.reason === 'migration_required' ? 'Migration required — run the explicit migration command.' :
      database.reason === 'database_not_configured' ? 'Not configured — save the runtime credential first.' : 'Unavailable — check the local SQL connection.';
  const modelLabel = modelError ? 'Could not read model status.' : !models ? 'Not checked' :
    models.status === 'available' ? 'Available locally' : models.status === 'no_models' ? 'No local models installed.' :
      models.status === 'invalid_response' ? 'Ollama returned an invalid response.' : 'Ollama unavailable — start it separately, then refresh.';
  const selectedModelId = conversation?.conversation.selectedModelId ?? chosenModel;
  const canSend = !(!conversation && (!activeProfile || activeProfile.isArchived)) && database?.status === 'ready' && !!selectedModelId && !!models?.models.some(model => model.id === selectedModelId);
  const sessionPanelText: Record<typeof sessionTabs[number]['id'], { title: string; detail: string }> = {
    git: { title: 'Choose a project to see its Git changes', detail: 'Project selection will connect a local repository to this workspace. No repository is attached to this chat yet.' },
    terminal: { title: 'Terminal is planned', detail: 'A project-scoped terminal will run locally after project access and command controls are in place.' },
    plan: { title: 'No active plan is linked', detail: 'When Guildhall has a task plan for this session, it will appear here.' },
    context: { title: 'Context usage is planned', detail: 'This panel will show the conversation context sent to the selected model and how much capacity remains.' },
    browser: { title: 'Browser controls are planned', detail: 'Playwright browser interaction will appear here when the local QC harness is connected.' },
    'side-chat': { title: 'Side chat is planned', detail: 'A separate question thread will let you ask about this session without interrupting its main conversation.' },
    automations: { title: 'Scheduled tasks are disabled', detail: 'Local scheduled automations are planned for a later phase. They will run on this computer, with the selected agent access policy, when enabled.' },
  };
  const activeSessionPanel = sessionPanelText[previewTab];
  const visibleHistory = history.filter(item => item.title.toLowerCase().includes(search.toLowerCase()));
  const queuedTurns = queuedPairs(conversation?.messages ?? []);
  const queuedMessages = (conversation?.messages ?? []).filter(message => message.role === 'assistant' && message.status === 'Queued');
  const profileGroups = profiles.filter(profile => !profile.isArchived).map(profile => ({ profile, sessions: visibleHistory.filter(item => item.agentDefinitionId === profile.id) }));
  const archivedProfileGroups = profiles.filter(profile => profile.isArchived).map(profile => ({ profile, sessions: visibleHistory.filter(item => item.agentDefinitionId === profile.id) }));
  const renderSession = (item: Conversation, index: number) => {
    const title = item.title === 'New conversation' ? `Conversation ${history.length - index}` : item.title;
    const activeRun = !!item.hasActiveRun || (conversation?.conversation.id === item.id && running);
    const unread = isUnread(item.lastResponseUtc, readState[item.id]);
    return <div className={`conversation-row ${conversation?.conversation.id === item.id ? 'selected' : ''} ${unread ? 'is-unread' : ''}`} key={item.id}>
      <button className={`conversation-card ${conversation?.conversation.id === item.id ? 'selected' : ''} ${unread ? 'is-unread' : ''}`} aria-label={`Open ${title}`} title={database?.reason === 'migration_required' ? 'Apply the pending database migration to open chats' : undefined} disabled={chatBusy || hasDraft || database?.status !== 'ready'} onClick={() => void openSavedChat(item.id)}><span className="conversation-icon"><Icon name="chat" size={18} /></span><span><strong>{title}</strong><small className="conversation-model" title={item.selectedModelId || 'Choose a model'}><span>{item.selectedModelId || 'Choose a model'}</span></small></span>{unread && <span className="unread-dot" aria-label="Unread response" title="Unread response" /> }<span className="sr-only">{!unread && !activeRun ? 'Read' : ''}</span>{activeRun && <span className="dot offline" aria-label="Reply pending" title="Reply pending" />}</button>
      <button className="conversation-menu-toggle icon-button" aria-label={`Actions for ${title}`} aria-expanded={conversationMenu === item.id} onClick={() => setConversationMenu(current => current === item.id ? null : item.id)}>···</button>
      <div className={`conversation-actions ${conversationMenu === item.id ? 'expanded' : ''}`}>
        <button aria-label={`Rename ${title}`} title="Rename conversation" disabled={chatBusy || hasDraft} onClick={() => { setRenameError(''); setRenameTarget(item); setRenameDraft(title); }}><Icon name="edit" size={16} /><span>Rename</span></button>
        <button aria-label={`${item.isArchived ? 'Restore' : 'Archive'} ${title}`} title={activeRun ? 'Wait for the response to finish' : item.isArchived ? 'Restore conversation' : 'Archive conversation'} disabled={chatBusy || hasDraft || activeRun} onClick={() => void archiveConversation(item)}>{item.isArchived ? '↩' : <svg aria-hidden="true" viewBox="0 0 20 20"><path d="M3 4.5h14l-1 3H4l-1-3Zm1.5 4.5v7h11V9M10 10v4m0 0-2-2m2 2 2-2" /></svg>}<span>{item.isArchived ? 'Restore' : 'Archive'}</span></button>
        <button aria-label={`Delete ${title}`} title={activeRun ? 'Wait for the response to finish' : 'Permanently delete conversation'} disabled={chatBusy || hasDraft || activeRun} onClick={() => { setDeleteError(''); setDeleteTarget(item); }}><Icon name="trash" size={16} /><span>Delete</span></button>
      </div>
    </div>;
  };

  const renderMember = ({ profile, sessions }: { profile: AgentProfile; sessions: Conversation[] }) => <section className={`council-group ${activeAgentId === profile.id ? 'active' : ''}`} key={profile.id}>
    <div className="council-group-heading" data-member-menu={memberMenuId === profile.id ? '' : undefined} onContextMenu={event => { event.preventDefault(); memberReturnFocus.current = document.getElementById(`member-actions-${profile.id}`); setMemberMenuId(profile.id); }} onKeyDown={event => {
      if (event.key === 'Escape' && memberMenuId === profile.id) { event.preventDefault(); event.stopPropagation(); setMemberMenuId(null); memberReturnFocus.current?.focus(); }
      if (event.key === 'Tab') setMemberMenuId(null);
      if (memberMenuId === profile.id && ['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
        event.preventDefault();
        const actions = Array.from(event.currentTarget.querySelectorAll<HTMLButtonElement>('[role="menuitem"]:not(:disabled)'));
        if (!actions.length) return;
        const current = actions.indexOf(document.activeElement as HTMLButtonElement);
        const next = event.key === 'Home' ? 0 : event.key === 'End' ? actions.length - 1 : (current + (event.key === 'ArrowUp' ? -1 : 1) + actions.length) % actions.length;
        actions[next]?.focus();
      }
    }}>
      <button className="council-profile-select" aria-label={`Select ${profile.name}`} aria-pressed={activeAgentId === profile.id} disabled={chatBusy || hasDraft} onClick={() => { setConversation(null); setSelectedAgentId(profile.id); setIdentity(identityForProfile(profile)); setChosenModel(profile.defaultModelId ?? chosenModel); }}><Portrait identity={toMemberIdentity(profile)} /><span><strong>{profile.name}</strong><small>{profile.isArchived ? 'Archived · ' : ''}{profile.role} · {sessions.length} shown</small></span></button>
      {!profile.isArchived && <button className="icon-button council-new-session" aria-label={`New session with ${profile.name}`} title={`New session with ${profile.name}`} disabled={chatBusy || hasDraft || !chosenModel || database?.status !== 'ready'} onClick={() => void openChat(true, profile.id)}><Icon name="plus" size={16} /></button>}
      <button id={`member-actions-${profile.id}`} className="icon-button member-menu-toggle" aria-label={`Member actions for ${profile.name}`} aria-haspopup="menu" aria-expanded={memberMenuId === profile.id} aria-controls={memberMenuId === profile.id ? `member-menu-${profile.id}` : undefined} onClick={event => { memberReturnFocus.current = event.currentTarget; setMemberMenuId(value => value === profile.id ? null : profile.id); }}>···</button>
      {memberMenuId === profile.id && <div className="member-menu" role="menu" id={`member-menu-${profile.id}`} aria-label={`Member actions for ${profile.name}`}>
        <button ref={memberMenuAction} role="menuitem" disabled={chatBusy || hasDraft || profileHasPendingChanges(profile.id) || database?.status !== 'ready'} onClick={() => { setArchiveMemberError(''); if (profile.isArchived) void setGuildMemberArchived(profile, false); else { setMemberMenuId(null); setArchiveMemberTarget(profile); } }}>{profile.isArchived ? 'Restore member' : 'Archive member'}</button>
        <button role="menuitem" disabled={chatBusy || hasDraft || profileHasPendingChanges(profile.id) || database?.status !== 'ready'} onClick={() => { setDeleteMemberError(''); setMemberMenuId(null); setDeleteMemberTarget(profile); }}>Delete member and sessions</button>
      </div>}
    </div>
    {sessions.map(item => renderSession(item, history.findIndex(entry => entry.id === item.id)))}
    {!sessions.length && <p className="council-no-sessions">{search ? 'No matching sessions.' : showArchived ? 'No archived sessions.' : 'No sessions yet.'}</p>}
  </section>;

  return <main className={`workspace ${sidebarCollapsed ? 'sidebar-collapsed' : ''}`} style={{ '--accent': identity.accent } as CSSProperties}>
    <header className="app-bar">
      <div className="wordmark"><Icon name="gate" size={34} /><span>Guildhall</span></div>
      <span className="app-bar-divider" /><span className="workspace-title">Personal council</span>
      <button className="sidebar-toggle icon-button" aria-label={sidebarCollapsed ? 'Expand chats sidebar' : 'Collapse chats sidebar'} title="Toggle chats sidebar" onClick={() => setSidebarCollapsed(value => !value)}><Icon name="panel" /></button>
      <span className="app-bar-spacer" /><button className="connection-button" aria-label="Connections" title="Connections" onClick={() => { setShowSettings(false); setShowConnections(!showConnections); }} aria-expanded={showConnections}><span className={`dot ${host === 'Connected' ? '' : 'offline'}`} /><Icon name="connections" size={17} /><span>Connections</span></button>
      <button className="member-toggle" aria-label="Customize guild member" aria-expanded={showMember} onClick={() => { setShowPreview(false); if (!showMember && window.innerWidth <= 480) setSidebarCollapsed(true); setShowMember(value => !value); }}><Portrait identity={identity} /><span>{memberName}</span></button>
    </header>
    <nav className="nav-rail" aria-label="Workspace navigation">
      <button className="rail-active" aria-label="Chats" title="Chats" onClick={() => { setShowConnections(false); setShowSettings(false); }}><Icon name="chat" size={25} /></button>
      <button aria-label="Guild member appearance" title="Guild member" onClick={() => { setShowPreview(false); if (!showMember && window.innerWidth <= 480) setSidebarCollapsed(true); setShowMember(value => !value); }}><Icon name="council" size={25} /></button>
      <button disabled title="Projects — planned" aria-label="Projects — planned"><Icon name="folder" size={25} /></button>
      <button disabled title="Scheduled tasks — planned for a later phase" aria-label="Scheduled tasks — planned"><Icon name="clock" size={25} /></button>
      <button aria-label="Workspace settings" title="Workspace settings" aria-expanded={showSettings} onClick={() => { setShowConnections(false); setShowSettings(value => !value); }}><Icon name="shield" size={25} /></button><span className="rail-spacer" /><span className="rail-local" title="Local workspace"><span className="dot" /></span>
    </nav>
    <aside className="conversation-sidebar" aria-label="Council and sessions"><div className="sidebar-top"><h1>Your council</h1><label className="search-box"><Icon name="search" /><input aria-label="Search loaded conversations" placeholder="Search chats…" value={search} onChange={event => setSearch(event.target.value)} /></label><button className="new-chat" onClick={() => void openChat(true)} disabled={chatBusy || hasDraft || !chosenModel || !activeProfile || activeProfile.isArchived || database?.status !== 'ready'}><Icon name="plus" /> New session</button><button className="add-member" onClick={() => void addGuildMember()} disabled={chatBusy || database?.status !== 'ready'}><Icon name="council" size={17} /> Add guild member</button></div>
      <div className="conversation-filter" role="group" aria-label="Conversation list filter"><button className={!showArchived ? 'active' : ''} aria-pressed={!showArchived} onClick={() => setShowArchived(false)}>Chats</button><button className={showArchived ? 'active' : ''} aria-pressed={showArchived} onClick={() => setShowArchived(true)}>Archived</button><span className="filter-count" aria-label={`${visibleHistory.length} conversations shown`}><strong>{visibleHistory.length}</strong><small>shown</small></span></div>
      <div className="conversation-list">{profileGroups.map(renderMember)}
        <button className="archived-members-toggle" aria-expanded={showArchivedMembers} aria-controls="archived-members" onClick={() => setShowArchivedMembers(value => !value)}>{showArchivedMembers ? '▾' : '▸'} Archived members ({archivedProfileGroups.length})</button>
        {showArchivedMembers && <div id="archived-members">{archivedProfileGroups.map(renderMember)}{!archivedProfileGroups.length && <p className="sidebar-empty">No archived members.</p>}</div>}
        {!profiles.length && visibleHistory.map(renderSession)}
        {!history.length && !profiles.length && <p className="sidebar-empty">Your conversations will live here.</p>}
      </div>
      <div className="sidebar-footer"><span className="dot" /> Local workspace <small>SQL Server · Ollama</small></div>
    </aside>
    <div className={`workspace-content ${showPreview ? 'with-preview' : ''} ${showMember ? 'with-member' : ''}`}>
    <div className="primary-pane">
    {showConnections && <aside className="connections-panel"><div className="toolbar"><h2>Connection status</h2><button onClick={() => void refresh()} disabled={busy}>{busy ? 'Checking…' : 'Refresh'}</button></div>
    <section aria-label="Connection status" aria-live="polite">
      <article><h3>Agent host</h3><p>{host}</p><small>Authenticated connection on this computer</small></article>
      <article><h3>SQL Server</h3><p>{databaseLabel}</p><small>Dedicated application database</small></article>
      <article><h3>Local models</h3><p>{modelLabel}</p>{models?.status === 'available' && <ul>{models.models.map(model => <li key={model.id}>{model.displayName}</li>)}</ul>}<small>Discovered through the agent host</small></article>
    </section><section className="connection-catalog" aria-label="Provider connections preview"><div><h3>Provider connections</h3><small>Planned providers · sign-in is not connected</small></div><div className="provider-grid">{futureConnections.map(name => <article key={name}><span>{name}</span><button disabled title={`${name} connection is not implemented yet`}>Planned</button></article>)}</div></section></aside>}
    {showSettings && <aside className="settings-panel" aria-label="Workspace settings"><div className="toolbar"><div><h2>Workspace settings</h2><small>Preferences are stored on this computer.</small></div><button aria-label="Close workspace settings" onClick={() => setShowSettings(false)}>×</button></div><GuildhallBehaviorSettings config={configuration.current} /><section className="settings-section"><div><h3>Appearance</h3><small>Display scale: {Math.round(uiScale * 100)}%</small></div><input aria-label="Display scale" type="range" min="80" max="150" step="5" value={Math.round(uiScale * 100)} onChange={event => setUiScale(Number(event.target.value) / 100)} /><div className="settings-choice-row"><span>Theme</span><button aria-pressed="true">Dark</button><button disabled title="Additional themes are planned">Light · planned</button><button disabled title="Additional themes are planned">System · planned</button></div><small>Keyboard: Ctrl/Cmd + or − to scale; Ctrl/Cmd 0 to reset.</small></section><section className="settings-section"><div><h3>Agent access</h3><small>Execution policy · planned for project tools</small></div><div className="planned-policy" aria-label="Planned agent access modes"><button disabled>Always ask</button><button disabled>Ask for unsafe actions</button><button disabled>Unrestricted · danger</button></div><small>These modes will be enforced by the local host when tool access is implemented.</small></section><section className="settings-section"><div><h3>Usage &amp; spending</h3><small>No provider usage data is connected.</small></div><p>Local model usage and provider-reported costs will appear here when available. Guildhall will label estimates and will not invent spending totals.</p></section></aside>}
    <section className="chat" aria-label="Local chat">
      <div className="pane-header"><span className="pane-icon"><Icon name="chat" size={17} /></span><h2>{conversation?.conversation.title || 'A new conversation'}</h2><span className="lane-label">Saved locally</span><div className="chat-actions"><button onClick={() => { setShowMember(false); setShowPreview(!showPreview); }} aria-pressed={showPreview} title="Toggle session tools"><Icon name="panel" size={16} /><span>Session tools</span></button></div></div>
      <div className="messages" ref={transcript} tabIndex={0} aria-label="Messages" onScroll={scrolling.onScroll} onWheel={scrolling.onWheel} onKeyDown={scrolling.onKeyDown}><div className="transcript-content" ref={scrolling.content}>
        {!conversation?.messages.length && !activeProfile?.isArchived && <div className="chat-welcome"><span className="welcome-symbol"><Icon name="gate" size={52} /></span><span className="eyebrow">YOUR SEAT AT THE TABLE</span><h1>What shall we work on?</h1><p>Bring an idea. We’ll find a way forward.<br />Everything stays in your workspace.</p><div className="welcome-cards">{starterSuggestions(activeProfile).map(suggestion => <button key={suggestion} type="button" onClick={() => { setDraft(suggestion); composerInput.current?.focus(); }}>{suggestion}</button>)}</div></div>}
        {conversation && [...conversation.messages].sort((a, b) => a.turnNumber - b.turnNumber || (a.role === 'user' ? 0 : 1) - (b.role === 'user' ? 0 : 1)).filter(message => !(queuedTurns.has(message.turnNumber) && (message.role === 'user' || message.status === 'Queued'))).map(message => <article key={message.id} className={`message ${message.role}`}>{message.role === 'assistant' ? <div className="message-identity"><Portrait identity={identity} expression={message.status === 'Running' ? message.text ? 'idea' : 'pondering' : 'neutral'} /><div><strong>{memberName}</strong><span>{identity.role.trim() || 'Steward'}{message.modelId ? ` · ${message.modelId}` : ''}</span></div>{message.role === 'assistant' && message.status === 'Completed' && formatResponseDuration(message.responseDurationSeconds) && <span className="run-label response-duration" title="Includes time waiting in the queue">{formatResponseDuration(message.responseDurationSeconds)}</span>}</div> : <span className="sr-only">You</span>}<div className="message-surface"><div className="markdown-body">{message.text ? <StreamingMarkdown text={message.text} streaming={message.role === 'assistant' && message.status === 'Running'} /> : <p>{message.status === 'Queued' ? 'Queued — waiting for a turn.' : message.status === 'Running' ? <span className="waiting-label">{waitingMessages(memberName, message.modelId).map((status, index) => <span key={index} aria-hidden="true">{status}</span>)}<span className="sr-only">Waiting for the first response text from {message.modelId || 'the model'}.</span></span> : message.errorCode === 'model_does_not_support_images' ? 'This model cannot read images. Choose a vision-capable model and send the image again.' : message.status === 'Failed' ? 'The reply failed before any text was received. Check Ollama and the selected model; image attachments require a vision-capable model.' : message.errorCode === 'queue_cancelled' ? 'Queued message removed.' : message.status === 'Cancelled' ? 'Response stopped before any text arrived.' : message.status === 'Interrupted' ? 'Response interrupted before any text arrived. Send a new message to continue.' : 'The model returned no reply text.'}</p>}</div>{!!message.attachments?.length && <div className="message-attachments">{message.attachments.map(file => <MessageAttachment key={file.id} attachment={file} config={configuration.current} />)}</div>}</div>{message.role === 'assistant' && message.status !== 'Completed' && message.status !== 'Running' && message.status !== 'Queued' && <span className="run-label">{message.status}</span>}{!!message.text && <div className="message-tools"><button aria-label={`Copy ${message.role === 'user' ? 'your message' : 'assistant response'}`} onClick={() => void copyMessage(message.id, message.text)}>{copiedMessageId === message.id ? <svg aria-hidden="true" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><path d="m5 12 4 4L19 6" /></svg> : <svg aria-hidden="true" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><rect x="8" y="8" width="12" height="13" rx="2" /><path d="M16 8V5a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h3" /></svg>}</button><span className="sr-only" role="status">{copiedMessageId === message.id ? 'Copied to clipboard' : ''}</span></div>}</article>)}
      </div></div>
      <div className="composer-region">{activeProfile?.isArchived && <p className="archived-member-notice">Archived member · Saved sessions are kept. Restore the member to start a new session.</p>}{archiveMemberError && !archiveMemberTarget && <p className="chat-error" role="alert">{archiveMemberError}</p>}{chatError && <p className="chat-error" role="alert">{chatError}</p>}
      {!!queuedMessages.length && <div className="queue-panel" aria-label="Queued messages">{queuedMessages.map(message => {
        const userMessage = conversation?.messages.find(candidate => candidate.role === 'user' && candidate.turnNumber === message.turnNumber);
        return <div className="queue-item" key={message.id}><button type="button" aria-label={`Send queued message ${message.turnNumber} now`} title="Send now" disabled={chatBusy} onClick={() => void promoteQueuedMessage(message.runId)}><Icon name="send" size={17} /></button><button type="button" aria-label={`Remove queued message ${message.turnNumber}`} title="Remove from queue" disabled={chatBusy} onClick={() => void removeQueuedMessage(message.runId)}><Icon name="trash" size={17} /></button><span className="queue-snippet">{userMessage?.text || (userMessage?.attachments?.length ? `${userMessage.attachments.length} attachment${userMessage.attachments.length === 1 ? '' : 's'}` : 'Attachment message')}</span>{!!userMessage?.attachments?.length && <details className="queue-attachments"><summary>{userMessage.attachments.length} files</summary>{userMessage.attachments.map(file => <MessageAttachment key={file.id} attachment={file} config={configuration.current} />)}</details>}</div>;
      })}</div>}
      <div className="composer">{selectedFiles.length > 0 && <div className="pending-attachments" aria-label="Attachments to send">{selectedFiles.map((file, index) => <span key={`${file.name}-${index}`}><AttachmentThumbnail file={file} /><span>▤ {file.name} · {(file.size / (1024 * 1024)).toFixed(1)} MB</span><button aria-label={`Remove ${file.name}`} disabled={chatBusy} onClick={() => setSelectedFiles(items => items.filter((_, itemIndex) => itemIndex !== index))}>×</button></span>)}</div>}<textarea ref={composerInput} aria-label="Message" value={draft} onChange={event => setDraft(event.target.value)} onKeyDown={handleComposerKeyDown} onPaste={pasteIntoComposer} placeholder={chosenModel ? `Message ${memberName}…` : 'Choose a local model to start…'} disabled={host !== 'Connected' || database?.status !== 'ready' || !models || models.status !== 'available' || (!conversation && (!chosenModel || !activeProfile || activeProfile.isArchived))} rows={1} />
      <input ref={filePicker} className="sr-only" type="file" multiple aria-label="Choose attachments" onChange={event => { addAttachments(Array.from(event.currentTarget.files ?? [])); event.currentTarget.value = ''; }} />
      <div className="composer-toolbar"><button className="attach-button" aria-label="Add attachments" title="Add files (up to 20 MB each)" onClick={() => filePicker.current?.click()} disabled={chatBusy || database?.status !== 'ready'}><Icon name="attach" size={22} /></button><label className="model-control"><span className="sr-only">Model for {conversation ? 'this conversation' : 'a new conversation'}</span>
        <select value={chosenModel} onChange={event => void selectModel(event.target.value)} disabled={chatBusy || models?.status !== 'available'}>
          <option value="" disabled>Choose a local model</option>
          {chosenModel && !models?.models.some(model => model.id === chosenModel) && <option value={chosenModel} disabled>{chosenModel} (unavailable)</option>}
          {models?.models.map(model => <option key={model.id} value={model.id}>{model.displayName}</option>)}
        </select>
      </label><span className="composer-spacer" />{hasPendingWork && <select className="delivery-mode" aria-label="Follow-up delivery" value={deliveryMode} onChange={event => setDeliveryMode(event.target.value as 'queue' | 'send_now')}><option value="queue">Queue</option><option value="send_now">Send Now</option></select>}{running && <button className="send-button stop-button" aria-label="Stop response" title="Stop response" onClick={() => void stopResponse()} disabled={chatBusy}><svg aria-hidden="true" width="14" height="14" viewBox="0 0 16 16" fill="currentColor"><rect x="2" y="2" width="12" height="12" rx="2" /></svg></button>}{(!running || hasDraft) && <button className="send-button" aria-label={hasPendingWork ? deliveryMode === 'queue' ? 'Queue message' : 'Send now' : 'Send'} title="Send (Enter)" onClick={() => void sendMessage()} disabled={!canSend || !hasDraft || chatBusy || queuedCount >= 8}><Icon name="send" size={22} /></button>}</div></div>
      <footer>{queuedCount > 0 ? `${queuedCount} queued · each reply keeps the model selected when sent` : hasDraft ? 'Enter to send · Shift+Enter for a new line · Clear your draft to change chats' : 'Files stay in this workspace · Text and images are sent to compatible local models'}</footer></div>
    </section></div>
    {memberPanel.present && <button className={`member-scrim ${memberPanel.closing ? 'panel-closing' : ''}`} tabIndex={-1} aria-label="Dismiss guild member" onClick={() => setShowMember(false)} />}{memberPanel.present && <CouncilMember activity={running ? conversation?.messages.some(message => message.role === 'assistant' && message.status === 'Running' && !!message.text) ? 'idea' : 'pondering' : 'neutral'} closing={memberPanel.closing} identity={identity} onChange={updateMemberIdentity} onClose={() => setShowMember(false)} storageError={profileErrorForActive || storageError} saving={profileSaving} onRetrySave={() => { if (!activeProfile) return; const profileId = activeProfile.id; setProfileErrors(current => { const remaining = { ...current }; delete remaining[profileId]; return remaining; }); setProfileRetryById(current => ({ ...current, [profileId]: (current[profileId] ?? 0) + 1 })); }} retryAvailable={profileDirty && !!profileErrorForActive} />}
    {toolsPanel.present && <aside className={`preview-pane ${toolsPanel.closing ? 'panel-closing' : ''}`} aria-label="Session tools"><div className="pane-header"><span>◫</span><h2>Session tools</h2><span className="preview-label">LOCAL WORKSPACE</span><button aria-label="Collapse session tools" onClick={() => setShowPreview(false)}>×</button></div><div className="session-tabs" role="group" aria-label="Session tools">{sessionTabs.map(tab => <button key={tab.id} aria-pressed={previewTab === tab.id} className={previewTab === tab.id ? 'selected' : ''} onClick={() => setPreviewTab(tab.id)}>{tab.label}<small>{tab.status}</small></button>)}</div><section key={previewTab} className="session-placeholder"><span className="session-placeholder-icon">{previewTab === 'git' ? '⌘' : previewTab === 'terminal' ? '>_' : previewTab === 'plan' ? '☷' : previewTab === 'context' ? '◉' : previewTab === 'browser' ? '◎' : previewTab === 'automations' ? '◷' : '◌'}</span><span className="eyebrow">{activeSessionPanel.title}</span><p>{activeSessionPanel.detail}</p><small>Preview only · This panel does not run commands or connect external services.</small></section></aside>}
    </div>
    {deleteMemberTarget && <div className="dialog-backdrop"><section className="confirm-dialog" role="alertdialog" aria-modal="true" aria-labelledby="delete-member-title" aria-describedby="delete-member-description"><h2 id="delete-member-title">Permanently delete {deleteMemberTarget.name}?</h2><p id="delete-member-description">This permanently deletes <strong>{deleteMemberTarget.name}</strong>, all of their saved sessions, messages, and attachments. This cannot be undone. Existing SQL backups may still contain copies. Replies must finish before deletion.</p>{deleteMemberError && <p className="chat-error" role="alert">{deleteMemberError}</p>}<div><button ref={deleteMemberCancel} disabled={chatBusy} onClick={() => { setDeleteMemberTarget(null); memberReturnFocus.current?.focus(); }}>Cancel</button><button className="danger-button" disabled={chatBusy} onClick={() => void deleteGuildMember(deleteMemberTarget)}>{chatBusy ? 'Deleting…' : 'Delete member and sessions'}</button></div></section></div>}
    {archiveMemberTarget && <div className="dialog-backdrop"><section ref={archiveMemberDialog} tabIndex={-1} className="confirm-dialog" role="alertdialog" aria-modal="true" aria-labelledby="archive-member-title" aria-describedby="archive-member-description"><h2 id="archive-member-title">Archive this member?</h2><p id="archive-member-description"><strong>{archiveMemberTarget.name}</strong> will leave the active council. All saved sessions and messages are kept in Archived members. You can restore this member later. Running or queued replies must finish first.</p>{archiveMemberError && <p className="chat-error" role="alert">{archiveMemberError}</p>}<div><button disabled={chatBusy} onClick={() => { setArchiveMemberTarget(null); memberReturnFocus.current?.focus(); }}>Cancel</button><button disabled={chatBusy} onClick={() => void setGuildMemberArchived(archiveMemberTarget, true)}>{chatBusy ? 'Archiving…' : 'Archive member'}</button></div></section></div>}
    {deleteTarget && <div className="dialog-backdrop"><section ref={deleteDialog} className="confirm-dialog" role="alertdialog" aria-modal="true" aria-labelledby="delete-title" aria-describedby="delete-description"><h2 id="delete-title">Delete this conversation?</h2><p id="delete-description"><strong>{deleteTarget.title}</strong> and its messages will be permanently removed from Guildhall. This cannot be undone. Existing SQL backups may still contain copies.</p>{deleteError && <p className="chat-error" role="alert">{deleteError}</p>}<div><button ref={cancelDeleteButton} onClick={() => setDeleteTarget(null)} disabled={chatBusy}>Cancel</button><button className="danger-button" onClick={() => void deleteConversation()} disabled={chatBusy || deleteTarget.hasActiveRun}>{chatBusy ? 'Deleting…' : 'Delete conversation'}</button></div></section></div>}
    {renameTarget && <div className="dialog-backdrop"><form className="confirm-dialog" onSubmit={event => { event.preventDefault(); void renameConversation(); }} aria-labelledby="rename-title"><h2 id="rename-title">Rename chat</h2><label className="rename-field">Chat name<input autoFocus maxLength={200} value={renameDraft} onChange={event => setRenameDraft(event.target.value)} /></label>{renameError && <p className="chat-error" role="alert">{renameError}</p>}<div><button type="button" onClick={() => setRenameTarget(null)} disabled={chatBusy}>Cancel</button><button type="submit" disabled={chatBusy || !renameDraft.trim()}>{chatBusy ? 'Saving…' : 'Save name'}</button></div></form></div>}
  </main>;
}












