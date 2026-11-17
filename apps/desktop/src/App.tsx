import { lazy, Suspense, useCallback, useEffect, useRef, useState } from 'react';
import type { CSSProperties } from 'react';
import { bootstrap, HostRequestError, parseDiscovery, parseReadiness, request, requestBlob } from './api';
import type { AgentProfile, Bootstrap, ConnectionsCatalogData, Conversation, ConversationDetails, Discovery, MessageView, Readiness } from './api';
import { ChatComposer } from './ChatComposer';
import { ChatTranscript } from './ChatTranscript';
import { ConfirmationDialogs } from './ConfirmationDialogs';
import type { MemoryDraft } from './ConfirmationDialogs';
import { CouncilMember, Portrait, useMemberIdentity } from './CouncilMember';
import type { MemberIdentity } from './CouncilMember';
import { CouncilSidebar } from './CouncilSidebar';
import { Icon } from './Icon';
import { PaneResizeHandle } from './PaneResizeHandle';
import { WorkspaceSettingsPanel } from './WorkspaceSettingsPanel';
import { markConversationRead, readWatermarks } from './chatPresentation';
import { usePanelPresence } from './usePanelPresence';
import { useTranscriptScroll } from './useTranscriptScroll';
import './style.css';

const ContextInspector = lazy(() =>
  import('./ContextInspector').then(module => ({ default: module.ContextInspector }))
);
const ConnectionsCatalog = lazy(() =>
  import('./ConnectionsCatalog').then(module => ({ default: module.ConnectionsCatalog }))
);

const sessionTabs = [
  { id: 'git', label: 'Git changes', status: 'No project' },
  { id: 'terminal', label: 'Terminal', status: 'Planned' },
  { id: 'plan', label: 'Plan', status: 'Not linked' },
  { id: 'context', label: 'Context usage', status: 'Live' },
  { id: 'browser', label: 'Browser', status: 'QC harness' },
  { id: 'side-chat', label: 'Side chat', status: 'Planned' },
  { id: 'automations', label: 'Scheduled tasks', status: 'Disabled' },
] as const;

const toMemberIdentity = (profile: AgentProfile): MemberIdentity => ({
  name: profile.name,
  role: profile.role,
  portrait: profile.portraitId,
  customPortrait: profile.customPortrait ?? '',
  accent: profile.accent,
  instructions: profile.instructions,
});

const profileMatchesIdentity = (profile: AgentProfile, identity: MemberIdentity) =>
  identity.name === profile.name &&
  identity.role === profile.role &&
  identity.portrait === profile.portraitId &&
  identity.customPortrait === (profile.customPortrait ?? '') &&
  identity.accent === profile.accent &&
  identity.instructions === profile.instructions;

const sameIdentity = (left: MemberIdentity, right: MemberIdentity) =>
  left.name === right.name &&
  left.role === right.role &&
  left.portrait === right.portrait &&
  left.customPortrait === right.customPortrait &&
  left.accent === right.accent &&
  left.instructions === right.instructions;

function storedPaneWidth(key: string, fallback: number, min: number, max: number) {
  try {
    const saved = Number(localStorage.getItem(key));
    return Number.isFinite(saved) && saved >= min && saved <= max ? saved : fallback;
  } catch { return fallback; }
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
  const [connections, setConnections] = useState<ConnectionsCatalogData | null>(null);
  const [connectionsLoading, setConnectionsLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [databaseError, setDatabaseError] = useState(false);
  const [modelError, setModelError] = useState(false);
  const [conversation, setConversation] = useState<ConversationDetails | null>(null);
  const [chosenProvider, setChosenProvider] = useState('ollama');
  const [chosenModel, setChosenModel] = useState('');
  const [chosenHarnessMode, setChosenHarnessMode] = useState('read_only');
  const [chatBusy, setChatBusy] = useState(false);
  const chatOperation = useRef(false);
  const running = conversation?.messages.some(message => ['Running', 'Cancelling'].includes(message.status)) ?? false;
  const queuedCount = conversation?.messages.filter(message => message.role === 'assistant' && message.status === 'Queued').length ?? 0;
  const hasPendingWork = running || queuedCount > 0;
  const [deliveryMode, setDeliveryMode] = useState<'queue' | 'send_now'>('queue');
  const scrolling = useTranscriptScroll(conversation?.conversation.id ?? null, conversation?.messages);
  const draftsByKey = useRef<Map<string, { draft: string; selectedFiles: File[]; editingQueuedRunId: string | null }>>((() => {
    const map = new Map<string, { draft: string; selectedFiles: File[]; editingQueuedRunId: string | null }>();
    try {
      const parsed = JSON.parse(localStorage.getItem('guildhall.composerDrafts.v1') ?? 'null');
      if (parsed && typeof parsed === 'object') {
        for (const [key, value] of Object.entries(parsed as Record<string, unknown>)) {
          if (typeof value === 'string' && value) map.set(key, { draft: value, selectedFiles: [], editingQueuedRunId: null });
        }
      }
    } catch { /* Ignore unavailable or malformed localStorage */ }
    return map;
  })());
  const [draft, setDraft] = useState(() => draftsByKey.current.get('new:default')?.draft ?? '');
  const [selectedFiles, setSelectedFiles] = useState<File[]>([]);
  const [editingQueuedRunId, setEditingQueuedRunId] = useState<string | null>(null);
  const [collapsedMembers, setCollapsedMembers] = useState<Record<string, boolean>>({});
  const [instanceTag, setInstanceTag] = useState('');
  const composerInput = useRef<HTMLTextAreaElement | null>(null);
  const hasDraft = !!draft.trim() || selectedFiles.length > 0 || editingQueuedRunId !== null;
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
  const memberReturnFocus = useRef<HTMLElement | null>(null);
  const [archiveMemberError, setArchiveMemberError] = useState('');
  const [showArchivedMembers, setShowArchivedMembers] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<Conversation | null>(null);
  const [deleteError, setDeleteError] = useState('');
  const [renameTarget, setRenameTarget] = useState<Conversation | null>(null);
  const [renameDraft, setRenameDraft] = useState('');
  const [renameError, setRenameError] = useState('');
  const [showConnections, setShowConnections] = useState(false);
  const [showSettings, setShowSettings] = useState(false);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(() => window.innerWidth < 900);
  const [showPreview, setShowPreview] = useState(false);
  const [viewportWidth, setViewportWidth] = useState(() => window.innerWidth);
  const wasWideViewport = useRef(window.innerWidth >= 900);
  const [councilWidth, setCouncilWidth] = useState(() => storedPaneWidth('guildhall.pane.council', window.innerWidth >= 1800 ? 310 : window.innerWidth >= 900 ? 290 : 260, 220, 520));
  const [memberPaneWidth, setMemberPaneWidth] = useState(() => storedPaneWidth('guildhall.pane.member', window.innerWidth >= 1800 ? 350 : window.innerWidth >= 900 ? 315 : 302, 240, 900));
  const [toolsPaneWidth, setToolsPaneWidth] = useState(() => storedPaneWidth('guildhall.pane.tools', Math.max(260, Math.min(900, Math.round(window.innerWidth * .42))), 240, 900));
  const railWidth = viewportWidth < 900 ? 58 : viewportWidth >= 1800 ? 80 : 76;
  const rightPaneWidth = showMember ? memberPaneWidth : showPreview ? toolsPaneWidth : 0;
  const councilResizeMax = viewportWidth < 900
    ? Math.max(220, Math.min(520, viewportWidth - railWidth - 72))
    : Math.max(220, Math.min(520, viewportWidth - railWidth - 420 - (viewportWidth > 1279 ? rightPaneWidth : 0)));
  const effectiveCouncilWidth = Math.min(councilWidth, councilResizeMax);
  const rightPaneResizeMax = viewportWidth <= 1279
    ? Math.max(240, Math.min(900, viewportWidth - railWidth - 24))
    : Math.max(240, Math.min(900, viewportWidth - railWidth - effectiveCouncilWidth - 420));
  const memberPanel = usePanelPresence(showMember);
  const toolsPanel = usePanelPresence(showPreview);
  const [previewTab, setPreviewTab] = useState<typeof sessionTabs[number]['id']>('context');
  const configuration = useRef<Bootstrap | null>(null);
  const profileMutation = useRef(false);
  const pending = useRef<AbortController | null>(null);

  useEffect(() => {
    const handleResize = () => {
      const isWide = window.innerWidth >= 900;
      setViewportWidth(window.innerWidth);
      if (wasWideViewport.current && !isWide) setSidebarCollapsed(true);
      wasWideViewport.current = isWide;
    };
    window.addEventListener('resize', handleResize);
    return () => window.removeEventListener('resize', handleResize);
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      try {
        localStorage.setItem('guildhall.pane.council', String(councilWidth));
        localStorage.setItem('guildhall.pane.member', String(memberPaneWidth));
        localStorage.setItem('guildhall.pane.tools', String(toolsPaneWidth));
      } catch { /* Pane sizing still works if local preferences cannot be stored. */ }
    }, 250);
    return () => window.clearTimeout(timer);
  }, [councilWidth, memberPaneWidth, toolsPaneWidth]);

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
      if (config.windowTitle) document.title = config.windowTitle;
      if (config.instanceTag) setInstanceTag(config.instanceTag);
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
        request(config, '/connections', controller.signal).then(value => {
          if (pending.current === controller && value && typeof value === 'object' && 'entries' in value && Array.isArray((value as ConnectionsCatalogData).entries))
            setConnections(value as ConnectionsCatalogData);
        }).catch(() => { /* Optional in older test stubs */ }),
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
      if (!conversation && !selectedAgentId && firstActive) {
        setSelectedAgentId(firstActive.id);
        setIdentity(profileDraftsRef.current[firstActive.id] ?? toMemberIdentity(firstActive));
        const savedDraft = draftsByKey.current.get(`new:${firstActive.id}`)?.draft;
        if (savedDraft) setDraft(current => current || savedDraft);
      }
    }).catch(() => { if (!controller.signal.aborted) setProfileError('Could not load guild members. Check the approved database update and refresh.'); });
    return () => controller.abort();
  }, [database?.status]);

  const activeAgentId = conversation?.agent?.id ?? selectedAgentId;
  const activeProfile = profiles.find(profile => profile.id === activeAgentId) ?? conversation?.agent;
  const profileSaving = profileSavingId === activeProfile?.id;
  const profileDirty = !!activeProfile && !!profileDrafts[activeProfile.id] && !profileMatchesIdentity(activeProfile, profileDrafts[activeProfile.id]);
  const profileHasPendingChanges = (profileId: string) => {
    const profile = profiles.find(item => item.id === profileId) ?? (conversation?.agent?.id === profileId ? conversation.agent : undefined);
    const currentDraft = profileDrafts[profileId];
    return !!profile && (!!currentDraft && !profileMatchesIdentity(profile, currentDraft) || profileSavingId === profileId);
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
    const pendingProfiles = profiles.flatMap(profile => {
      const currentDraft = profileDrafts[profile.id];
      const failed = failedProfileDrafts.current[profile.id];
      const failedAtCurrentRevision = failed && sameIdentity(failed.draft, currentDraft ?? toMemberIdentity(profile)) && failed.retry === (profileRetryById[profile.id] ?? 0);
      return currentDraft && !profileMatchesIdentity(profile, currentDraft) && !failedAtCurrentRevision ? [{ profile, draft: currentDraft }] : [];
    });
    if (!pendingProfiles.length) return;
    const timer = window.setTimeout(async () => {
      if (profileMutation.current) return;
      profileMutation.current = true;
      setProfileError('');
      try {
        for (const { profile, draft: nextDraft } of pendingProfiles) {
          setProfileSavingId(profile.id);
          try {
            const updated = await request(configuration.current!, `/agents/${profile.id}`, new AbortController().signal, {
              method: 'PATCH',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({
                name: nextDraft.name,
                role: nextDraft.role,
                portraitId: nextDraft.portrait,
                customPortrait: nextDraft.customPortrait || null,
                accent: nextDraft.accent,
                instructions: nextDraft.instructions,
                defaultModelId: profile.defaultModelId,
                expectedRevision: profile.revision,
              }),
            }) as AgentProfile;
            delete failedProfileDrafts.current[profile.id];
            setProfileErrors(current => { const remaining = { ...current }; delete remaining[profile.id]; return remaining; });
            setProfiles(items => items.map(item => item.id === profile.id ? updated : item));
            setConversation(current => current?.agent?.id === profile.id ? { ...current, agent: updated } : current);
            setProfileDrafts(current => {
              const latest = current[profile.id];
              if (!latest || !sameIdentity(latest, nextDraft)) return current;
              const remaining = { ...current };
              delete remaining[profile.id];
              profileDraftsRef.current = remaining;
              return remaining;
            });
          } catch {
            failedProfileDrafts.current[profile.id] = { draft: nextDraft, retry: profileRetryById[profile.id] ?? 0 };
            setProfileErrors(current => ({ ...current, [profile.id]: 'Could not save this guild member. The draft is kept; retry the save before sending.' }));
          }
        }
      } finally { profileMutation.current = false; setProfileSavingId(null); }
    }, 450);
    return () => clearTimeout(timer);
  }, [profileDrafts, profiles, database?.status, profileRetryById, profileSavingId]);

  const draftContextKey = (convId: string | null | undefined, agentId: string | null | undefined) =>
    convId ? `chat:${convId}` : `new:${agentId ?? 'default'}`;

  const syncPersistedDrafts = () => {
    try {
      const persisted: Record<string, string> = {};
      for (const [key, entry] of draftsByKey.current.entries()) {
        if (entry.draft) persisted[key] = entry.draft;
      }
      if (Object.keys(persisted).length > 0) localStorage.setItem('guildhall.composerDrafts.v1', JSON.stringify(persisted));
      else localStorage.removeItem('guildhall.composerDrafts.v1');
    } catch { /* Ignore storage quota or private mode errors */ }
  };

  const currentDraftKey = draftContextKey(conversation?.conversation.id, activeAgentId);
  useEffect(() => {
    const existing = draftsByKey.current.get(currentDraftKey);
    if (draft || (existing?.selectedFiles.length ?? 0) > 0 || (existing?.editingQueuedRunId ?? null) !== null) {
      draftsByKey.current.set(currentDraftKey, {
        draft,
        selectedFiles: existing?.selectedFiles ?? selectedFiles,
        editingQueuedRunId: existing?.editingQueuedRunId ?? editingQueuedRunId,
      });
    } else {
      draftsByKey.current.delete(currentDraftKey);
    }
    syncPersistedDrafts();
  }, [draft, currentDraftKey]);

  function switchComposerContext(nextConvId: string | null | undefined, nextAgentId: string | null | undefined) {
    const currentKey = draftContextKey(conversation?.conversation.id, activeAgentId);
    const nextKey = draftContextKey(nextConvId, nextAgentId);
    if (currentKey === nextKey) return;
    if (draft || selectedFiles.length > 0 || editingQueuedRunId !== null) {
      draftsByKey.current.set(currentKey, { draft, selectedFiles, editingQueuedRunId });
    } else {
      draftsByKey.current.delete(currentKey);
    }
    syncPersistedDrafts();
    const restored = draftsByKey.current.get(nextKey);
    setDraft(restored?.draft ?? '');
    setSelectedFiles(restored?.selectedFiles ?? []);
    setEditingQueuedRunId(restored?.editingQueuedRunId ?? null);
  }

  async function openSavedChat(id: string) {
    if (!configuration.current || chatOperation.current) return;
    if (database?.status !== 'ready') { setChatError('The local database needs its approved update before this chat can open.'); return; }
    chatOperation.current = true; setChatBusy(true); setChatError('');
    try {
      const details = await request(configuration.current, `/conversations/${id}`, new AbortController().signal) as ConversationDetails;
      switchComposerContext(id, details.agent?.id ?? activeAgentId);
      setConversation(details); setChosenProvider(details.conversation.selectedProviderId ?? details.agent?.defaultProviderId ?? 'ollama'); setChosenModel(details.conversation.selectedModelId ?? ''); setChosenHarnessMode(details.conversation.selectedHarnessMode ?? details.agent?.defaultHarnessMode ?? 'read_only');
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
      draftsByKey.current.delete(draftContextKey(deleteTarget.id, activeAgentId));
      syncPersistedDrafts();
      setHistory(items => items.filter(row => row.id !== deleteTarget.id));
      if (conversation?.conversation.id === deleteTarget.id) { switchComposerContext(null, activeAgentId); setConversation(null); }
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

  async function editQueuedMessage(assistantMessage: MessageView, userMessage?: MessageView) {
    if (!configuration.current || !assistantMessage.runId || !userMessage || chatOperation.current) return;
    chatOperation.current = true; setChatBusy(true); setChatError('');
    try {
      const files = await Promise.all((userMessage.attachments ?? []).map(async attachment => {
        const blob = await requestBlob(configuration.current!, `/attachments/${attachment.id}`, new AbortController().signal);
        return new File([blob], attachment.fileName, { type: attachment.contentType });
      }));
      setDraft(userMessage.text); setSelectedFiles(files); setEditingQueuedRunId(assistantMessage.runId); setDeliveryMode('queue');
      requestAnimationFrame(() => composerInput.current?.focus());
    } catch { setChatError('Could not load this queued message for editing. The queued message is unchanged.'); }
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
      switchComposerContext(null, profile.id);
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
    if (!configuration.current || chatOperation.current || profileHasPendingChanges(profile.id)) return;
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
    if (!configuration.current || chatOperation.current) return;
    const selectedProfile = profiles.find(profile => profile.id === agentDefinitionId);
    if (createNew && (!selectedProfile || selectedProfile.isArchived)) { setChatError('Choose or restore an active guild member before starting a new session.'); return; }
    chatOperation.current = true; setChatBusy(true);
    setChatError('');
    try {
      const list = createNew ? [] : await request(configuration.current, '/conversations?limit=1', new AbortController().signal) as Conversation[];
      if (!list.length && (!selectedProfile || selectedProfile.isArchived)) { setChatError('Choose or restore an active guild member before starting a new session.'); return; }
      const item = list[0] ?? await request(configuration.current, '/conversations', new AbortController().signal, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ title: 'New conversation', providerId: selectedProfile?.defaultProviderId || chosenProvider || 'ollama', modelId: selectedProfile?.defaultModelId || chosenModel || null, harnessMode: selectedProfile?.defaultHarnessMode || chosenHarnessMode || null, agentDefinitionId }) }) as Conversation;
      const details = await request(configuration.current, `/conversations/${item.id}`, new AbortController().signal) as ConversationDetails;
      switchComposerContext(details.conversation.id, details.agent?.id ?? agentDefinitionId);
      setConversation(details);
      setChosenProvider(details.conversation.selectedProviderId ?? details.agent?.defaultProviderId ?? 'ollama');
      setChosenModel(details.conversation.selectedModelId ?? '');
      setChosenHarnessMode(details.conversation.selectedHarnessMode ?? details.agent?.defaultHarnessMode ?? 'read_only');
      if (details.agent) { setSelectedAgentId(details.agent.id); setIdentity(identityForProfile(details.agent)); }
      if (window.innerWidth < 900) setSidebarCollapsed(true);
    } catch { setChatError('Could not open chat. Check the host and database, then try again.'); }
    finally { chatOperation.current = false; setChatBusy(false); }
  }

  async function probeAllConnections() {
    if (!configuration.current || connectionsLoading) return;
    setConnectionsLoading(true);
    try {
      const result = await request(configuration.current, '/connections?probe=true', new AbortController().signal) as ConnectionsCatalogData;
      setConnections(result);
    } catch { /* Keep existing connection state */ }
    finally { setConnectionsLoading(false); }
  }

  async function selectBackingEngine(providerId: string, modelId: string, harnessMode?: string) {
    if (chatOperation.current) return;
    if (!conversation || !configuration.current) {
      setChosenProvider(providerId);
      setChosenModel(modelId);
      if (harnessMode !== undefined) setChosenHarnessMode(harnessMode);
      return;
    }
    chatOperation.current = true; setChatBusy(true); setChatError('');
    try {
      const updated = await request(configuration.current, `/conversations/${conversation.conversation.id}`, new AbortController().signal, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          providerId,
          modelId,
          harnessMode: harnessMode ?? chosenHarnessMode,
          expectedRevision: conversation.conversation.revision,
        }),
      }) as Conversation;
      setConversation({ ...conversation, conversation: updated });
      setChosenProvider(updated.selectedProviderId ?? providerId);
      setChosenModel(updated.selectedModelId ?? modelId);
      if (updated.selectedHarnessMode) setChosenHarnessMode(updated.selectedHarnessMode);
      setHistory(items => items.map(item => item.id === updated.id ? updated : item));
    } catch { setChatError('Engine change was not saved. Reopen chat to refresh its settings.'); }
    finally { chatOperation.current = false; setChatBusy(false); }
  }

  async function selectModel(modelId: string) {
    await selectBackingEngine(chosenProvider, modelId);
  }

  async function sendMessage() {
    if (!conversation && (!activeProfile || activeProfile.isArchived)) return;
    if (activeProfile && profileHasPendingChanges(activeProfile.id)) { setChatError('This member is still saving. Wait for the save to finish before sending.'); return; }
    if (queuedCount >= 8 && !editingQueuedRunId) { setChatError('Eight messages are already queued. Remove one or wait for a reply.'); return; }
    const providerId = conversation?.conversation.selectedProviderId ?? chosenProvider ?? 'ollama';
    const modelId = conversation?.conversation.selectedModelId ?? chosenModel;
    const isOllama = providerId === 'ollama';
    const ollamaReady = isOllama && !!models?.models.some(model => model.id === modelId);
    const engineEntry = connections?.entries.find(entry => entry.id === providerId);
    const externalReady = !isOllama && (engineEntry ? engineEntry.status === 'Connected' : !!modelId);
    if (!configuration.current || database?.status !== 'ready' || (!draft.trim() && selectedFiles.length === 0) || chatOperation.current || !modelId || (!ollamaReady && !externalReady)) return;
    chatOperation.current = true; setChatBusy(true);
    scrolling.pinToBottom();
    const text = draft; let replacedRunCancelled = false; setDraft(''); setChatError('');
    try {
      let target = conversation;
      if (!target) {
        const created = await request(configuration.current, '/conversations', new AbortController().signal, {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ title: 'New conversation', modelId, agentDefinitionId: activeAgentId }),
        }) as Conversation;
        target = await request(configuration.current, `/conversations/${created.id}`, new AbortController().signal) as ConversationDetails;
        draftsByKey.current.delete(draftContextKey(null, activeAgentId));
        setConversation(target);
        if (target.agent) { setSelectedAgentId(target.agent.id); setIdentity(identityForProfile(target.agent)); }
        setHistory(items => [created, ...items.filter(item => item.id !== created.id)]);
      }
      target = await request(configuration.current, `/conversations/${target.conversation.id}`, new AbortController().signal) as ConversationDetails;
      if (editingQueuedRunId) {
        const original = target.messages.find(message => message.role === 'assistant' && message.runId === editingQueuedRunId);
        if (original?.status !== 'Queued') { setEditingQueuedRunId(null); throw new Error('queued_message_already_started'); }
        await request(configuration.current, `/runs/${editingQueuedRunId}/cancel`, new AbortController().signal, { method: 'POST' });
        replacedRunCancelled = true; setEditingQueuedRunId(null);
        target = await request(configuration.current, `/conversations/${target.conversation.id}`, new AbortController().signal) as ConversationDetails;
      }
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
      setConversation(updated); setSelectedFiles([]); setEditingQueuedRunId(null);
      setHistory(items => items.map(item => item.id === updated.conversation.id ? updated.conversation : item));
    } catch (error) {
      setDraft(text);
      setChatError(error instanceof Error && error.message === 'queued_message_already_started'
        ? 'That queued reply has already started. Your edited text is still here; send it as a new message after the reply finishes.'
        : error instanceof HostRequestError && error.serverMessage && (error.code === 'context_overflow' || error.code === 'cross_turn_budget_exceeded')
          ? error.serverMessage
        : replacedRunCancelled
          ? 'The original queued message was removed, but its replacement could not be sent. Your text and attachments are still here; send again to retry.'
          : 'Message could not be sent. Your text and attachments are still in the composer.');
    }
    finally { chatOperation.current = false; setChatBusy(false); }
  }

  async function copyMessage(id: string, text: string) {
    try { await navigator.clipboard.writeText(text); setCopiedMessageId(id); window.setTimeout(() => setCopiedMessageId(current => current === id ? null : current), 1400); }
    catch { setChatError('Could not copy this message. Check clipboard access for Guildhall.'); }
  }

  const [memoryDraft, setMemoryDraft] = useState<MemoryDraft | null>(null);
  const [memorySaving, setMemorySaving] = useState(false);
  const [memorySaveError, setMemorySaveError] = useState('');
  const [memoryRefreshKey, setMemoryRefreshKey] = useState(0);
  const memoryReturnFocus = useRef<HTMLElement | null>(null);
  function closeMemoryDialog() {
    setMemoryDraft(null);
    const trigger = memoryReturnFocus.current;
    memoryReturnFocus.current = null;
    if (trigger?.isConnected) window.setTimeout(() => trigger.focus(), 0);
  }
  async function saveMessageToMemory() {
    if (!configuration.current || !memoryDraft || memorySaving) return;
    const trimmed = memoryDraft.text.trim();
    if (!trimmed || trimmed.length > 2000) return;
    if (memoryDraft.scope === 'Member' && !activeProfile) return;
    setMemorySaving(true); setMemorySaveError('');
    try {
      await request(configuration.current, '/memory-notes', new AbortController().signal, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          scope: memoryDraft.scope,
          agentDefinitionId: memoryDraft.scope === 'Member' ? activeProfile?.id ?? null : null,
          body: trimmed,
          isEnabled: true,
          sourceConversationId: conversation?.conversation.id ?? null,
        }),
      });
      setMemoryRefreshKey(value => value + 1);
      closeMemoryDialog();
    } catch {
      setMemorySaveError('Could not save this memory note. Check the host and database migration.');
    } finally {
      setMemorySaving(false);
    }
  }

  const cancelDeleteMember = useCallback(() => setDeleteMemberTarget(null), []);
  const cancelArchiveMember = useCallback(() => setArchiveMemberTarget(null), []);
  const cancelDeleteConversation = useCallback(() => setDeleteTarget(null), []);

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

  return (
    <main
      className={`workspace ${sidebarCollapsed ? 'sidebar-collapsed' : ''}`}
      style={{
        '--accent': identity.accent,
        '--rail-width': `${railWidth}px`,
        '--council-width': `${effectiveCouncilWidth}px`,
        '--member-width': `${Math.min(memberPaneWidth, rightPaneResizeMax)}px`,
        '--tools-width': `${Math.min(toolsPaneWidth, rightPaneResizeMax)}px`,
        '--right-pane-width': `${viewportWidth > 1279 ? Math.min(rightPaneWidth, rightPaneResizeMax) : 0}px`,
      } as CSSProperties}
    >
      <header className="app-bar">
        <div className="wordmark"><Icon name="gate" size={34} /><span>Guildhall</span></div>
        <span className="app-bar-divider" /><span className="workspace-title">Personal council</span>
        {instanceTag && <span className="instance-badge" aria-label={`Instance ${instanceTag}`} title={configuration.current?.windowTitle || instanceTag}>{instanceTag}</span>}
        <button className="sidebar-toggle icon-button" aria-label={sidebarCollapsed ? 'Expand chats sidebar' : 'Collapse chats sidebar'} title="Toggle chats sidebar" onClick={() => setSidebarCollapsed(value => !value)}>
          <Icon name="panel" />
        </button>
        <span className="app-bar-spacer" />
        <button className="connection-button" aria-label="Connections" title="Connections" onClick={() => { setShowSettings(false); setShowConnections(!showConnections); }} aria-expanded={showConnections}>
          <span className={`dot ${host === 'Connected' ? '' : 'offline'}`} /><Icon name="connections" size={17} /><span>Connections</span>
        </button>
        <button className="member-toggle" aria-label="Customize guild member" aria-expanded={showMember} onClick={() => { setShowPreview(false); if (!showMember && window.innerWidth < 900) setSidebarCollapsed(true); setShowMember(value => !value); }}>
          <Portrait identity={identity} /><span>{memberName}</span>
        </button>
      </header>
      <nav className="nav-rail" aria-label="Workspace navigation">
        <button className="rail-active" aria-label="Chats" title="Chats" onClick={() => { setShowConnections(false); setShowSettings(false); }}><Icon name="chat" size={25} /></button>
        <button aria-label="Guild member appearance" title="Guild member" onClick={() => { setShowPreview(false); if (!showMember && window.innerWidth < 900) setSidebarCollapsed(true); setShowMember(value => !value); }}><Icon name="council" size={25} /></button>
        <button disabled title="Projects — planned" aria-label="Projects — planned"><Icon name="folder" size={25} /></button>
        <button disabled title="Scheduled tasks — planned for a later phase" aria-label="Scheduled tasks — planned"><Icon name="clock" size={25} /></button>
        <button aria-label="Workspace settings" title="Workspace settings" aria-expanded={showSettings} onClick={() => { setShowConnections(false); setShowSettings(value => !value); }}><Icon name="shield" size={25} /></button>
        <span className="rail-spacer" /><span className="rail-local" title="Local workspace"><span className="dot" /></span>
      </nav>
      <CouncilSidebar
        profiles={profiles}
        history={history}
        activeAgentId={activeAgentId}
        activeProfile={activeProfile}
        conversation={conversation}
        running={running}
        readState={readState}
        database={database}
        chatBusy={chatBusy}
        chosenModel={chosenModel}
        profileDirty={profileDirty}
        profileSaving={profileSaving}
        profileHasPendingChanges={profileHasPendingChanges}
        search={search}
        onSearchChange={setSearch}
        showArchived={showArchived}
        onShowArchivedChange={setShowArchived}
        showArchivedMembers={showArchivedMembers}
        onToggleArchivedMembers={() => setShowArchivedMembers(value => !value)}
        collapsedMembers={collapsedMembers}
        onToggleMemberCollapsed={(profileId, currentlyCollapsed) => setCollapsedMembers(current => ({ ...current, [profileId]: !currentlyCollapsed }))}
        conversationMenu={conversationMenu}
        onSetConversationMenu={setConversationMenu}
        memberMenuId={memberMenuId}
        onSetMemberMenuId={setMemberMenuId}
        memberReturnFocus={memberReturnFocus}
        uiScale={uiScale}
        councilWidth={councilWidth}
        councilResizeMax={councilResizeMax}
        onResizeCouncil={setCouncilWidth}
        onSelectMember={profile => {
          switchComposerContext(null, profile.id);
          setConversation(null);
          setSelectedAgentId(profile.id);
          setIdentity(identityForProfile(profile));
          setChosenModel(profile.defaultModelId ?? chosenModel);
        }}
        onNewSession={agentId => void openChat(true, agentId ?? activeAgentId)}
        onAddGuildMember={() => void addGuildMember()}
        onOpenSavedChat={id => void openSavedChat(id)}
        onRequestRenameConversation={(item, title) => { setRenameError(''); setRenameTarget(item); setRenameDraft(title); }}
        onArchiveConversation={item => void archiveConversation(item)}
        onRequestDeleteConversation={item => { setDeleteError(''); setDeleteTarget(item); }}
        onRestoreMember={profile => { setArchiveMemberError(''); void setGuildMemberArchived(profile, false); }}
        onRequestArchiveMember={profile => { setArchiveMemberError(''); setArchiveMemberTarget(profile); }}
        onRequestDeleteMember={profile => { setDeleteMemberError(''); setDeleteMemberTarget(profile); }}
      />
      <div className={`workspace-content ${showPreview ? 'with-preview' : ''} ${showMember ? 'with-member' : ''}`}>
        <div id="chat-pane" className="primary-pane">
          {showConnections && (
            <aside className="connections-panel">
              <div className="toolbar">
                <h2>Connection status</h2>
                <button onClick={() => void refresh()} disabled={busy}>{busy ? 'Checking…' : 'Refresh'}</button>
              </div>
              <section aria-label="Connection status" aria-live="polite">
                <article><h3>Agent host</h3><p>{host}</p><small>Authenticated connection on this computer</small></article>
                <article><h3>SQL Server</h3><p>{databaseLabel}</p><small>Dedicated application database</small></article>
                <article>
                  <h3>Local models</h3><p>{modelLabel}</p>
                  {models?.status === 'available' && <ul>{models.models.map(model => <li key={model.id}>{model.displayName}</li>)}</ul>}
                  <small>Discovered through the agent host</small>
                </article>
              </section>
              <Suspense fallback={<section className="connection-catalog" aria-busy="true"><small>Loading connections catalog…</small></section>}>
                <ConnectionsCatalog config={configuration.current} catalog={connections} loading={connectionsLoading} onCatalogUpdated={setConnections} onProbeAll={() => void probeAllConnections()} />
              </Suspense>
            </aside>
          )}
          {showSettings && (
            <WorkspaceSettingsPanel
              config={configuration.current}
              memoryRefreshKey={memoryRefreshKey}
              uiScale={uiScale}
              onUiScaleChange={setUiScale}
              messages={conversation?.messages ?? []}
              onClose={() => setShowSettings(false)}
            />
          )}
          <section className="chat" aria-label="Local chat">
            <div className="pane-header">
              <span className="pane-icon"><Icon name="chat" size={17} /></span>
              <h2>{conversation?.conversation.title || 'A new conversation'}</h2>
              <span className="lane-label">Saved locally</span>
              <div className="chat-actions">
                <button
                  onClick={() => { setShowMember(false); if (!showPreview && window.innerWidth < 900) setSidebarCollapsed(true); setShowPreview(!showPreview); }}
                  aria-pressed={showPreview}
                  title="Toggle session tools"
                >
                  <Icon name="panel" size={16} /><span>Session tools</span>
                </button>
              </div>
            </div>
            <ChatTranscript
              config={configuration.current}
              conversation={conversation}
              activeProfile={activeProfile}
              identity={identity}
              memberName={memberName}
              database={database}
              scrolling={scrolling}
              copiedMessageId={copiedMessageId}
              onCopyMessage={(id, text) => void copyMessage(id, text)}
              onSelectStarter={suggestion => { setDraft(suggestion); composerInput.current?.focus(); }}
              onSaveToMemory={(text, trigger) => {
                memoryReturnFocus.current = trigger;
                setMemorySaveError('');
                setMemoryDraft({ text, scope: activeProfile ? 'Member' : 'AboutYou' });
              }}
            />
            <ChatComposer
              composerInput={composerInput}
              activeProfile={activeProfile}
              memberName={memberName}
              conversation={conversation}
              host={host}
              database={database}
              models={models}
              connections={connections}
              chosenProvider={chosenProvider}
              chosenModel={chosenModel}
              chosenHarnessMode={chosenHarnessMode}
              draft={draft}
              onDraftChange={setDraft}
              selectedFiles={selectedFiles}
              onSelectedFilesChange={setSelectedFiles}
              editingQueuedRunId={editingQueuedRunId}
              onCancelEditQueued={() => { setEditingQueuedRunId(null); setDraft(''); setSelectedFiles([]); }}
              deliveryMode={deliveryMode}
              onDeliveryModeChange={setDeliveryMode}
              chatBusy={chatBusy}
              canSend={canSend}
              running={running}
              queuedCount={queuedCount}
              hasPendingWork={hasPendingWork}
              hasDraft={hasDraft}
              archiveMemberError={archiveMemberError}
              hasArchiveMemberTarget={!!archiveMemberTarget}
              chatError={chatError}
              onChatError={setChatError}
              onSendMessage={() => void sendMessage()}
              onStopResponse={() => void stopResponse()}
              onPromoteQueued={runId => void promoteQueuedMessage(runId)}
              onEditQueued={(assistantMessage, userMessage) => void editQueuedMessage(assistantMessage, userMessage)}
              onRemoveQueued={runId => void removeQueuedMessage(runId)}
              onSelectBackingEngine={(providerId, modelId, harnessMode) => void selectBackingEngine(providerId, modelId, harnessMode)}
              onSelectModel={modelId => void selectModel(modelId)}
            />
          </section>
        </div>
        {memberPanel.present && (
          <button className={`member-scrim ${memberPanel.closing ? 'panel-closing' : ''}`} tabIndex={-1} aria-label="Dismiss guild member" onClick={() => setShowMember(false)} />
        )}
        {memberPanel.present && (
          <CouncilMember
            config={configuration.current}
            profileId={activeProfile?.id}
            memoryRefreshKey={memoryRefreshKey}
            activity={running ? conversation?.messages.some(message => message.role === 'assistant' && message.status === 'Running' && !!message.text) ? 'idea' : 'pondering' : 'neutral'}
            closing={memberPanel.closing}
            identity={identity}
            resizePane={{ value: Math.min(memberPaneWidth, rightPaneResizeMax), min: 240, max: rightPaneResizeMax, onResize: setMemberPaneWidth }}
            onChange={updateMemberIdentity}
            onClose={() => setShowMember(false)}
            storageError={profileErrorForActive || storageError}
            saving={profileSaving}
            onRetrySave={() => {
              if (!activeProfile) return;
              const profileId = activeProfile.id;
              setProfileErrors(current => { const remaining = { ...current }; delete remaining[profileId]; return remaining; });
              setProfileRetryById(current => ({ ...current, [profileId]: (current[profileId] ?? 0) + 1 }));
            }}
            retryAvailable={profileDirty && !!profileErrorForActive}
          />
        )}
        {toolsPanel.present && (
          <aside id="session-tools-pane" className={`preview-pane ${toolsPanel.closing ? 'panel-closing' : ''}`} aria-label="Session tools">
            <PaneResizeHandle
              className="right-pane-resizer tools-resizer"
              label="Resize Session Tools pane"
              controls="session-tools-pane"
              value={Math.min(toolsPaneWidth, rightPaneResizeMax)}
              min={240}
              max={rightPaneResizeMax}
              direction={-1}
              onResize={setToolsPaneWidth}
            />
            <div className="pane-header">
              <span>◫</span><h2>Session tools</h2><span className="preview-label">LOCAL WORKSPACE</span>
              <button aria-label="Collapse session tools" onClick={() => setShowPreview(false)}>×</button>
            </div>
            <div className="session-tabs" role="group" aria-label="Session tools">
              {sessionTabs.map(tab => (
                <button key={tab.id} aria-pressed={previewTab === tab.id} className={previewTab === tab.id ? 'selected' : ''} onClick={() => setPreviewTab(tab.id)}>
                  {tab.label}<small>{tab.status}</small>
                </button>
              ))}
            </div>
            {previewTab === 'context' ? (
              <Suspense fallback={<section className="session-placeholder" aria-busy="true"><small>Loading context inspector…</small></section>}>
                <ContextInspector config={configuration.current} conversationId={conversation?.conversation.id ?? null} refreshToken={(conversation?.conversation.revision ?? 0) + memoryRefreshKey + (running ? 1 : 0)} />
              </Suspense>
            ) : (
              <section key={previewTab} className="session-placeholder">
                <span className="session-placeholder-icon">
                  {previewTab === 'git' ? '⌘' : previewTab === 'terminal' ? '>_' : previewTab === 'plan' ? '☷' : previewTab === 'browser' ? '◎' : previewTab === 'automations' ? '◷' : '◌'}
                </span>
                <span className="eyebrow">{activeSessionPanel.title}</span>
                <p>{activeSessionPanel.detail}</p>
                <small>Preview only · This panel does not run commands or connect external services.</small>
              </section>
            )}
          </aside>
        )}
      </div>
      <ConfirmationDialogs
        chatBusy={chatBusy}
        chatOperation={chatOperation}
        memberReturnFocus={memberReturnFocus}
        deleteMemberTarget={deleteMemberTarget}
        deleteMemberError={deleteMemberError}
        onCancelDeleteMember={cancelDeleteMember}
        onConfirmDeleteMember={profile => void deleteGuildMember(profile)}
        archiveMemberTarget={archiveMemberTarget}
        archiveMemberError={archiveMemberError}
        onCancelArchiveMember={cancelArchiveMember}
        onConfirmArchiveMember={profile => void setGuildMemberArchived(profile, true)}
        deleteTarget={deleteTarget}
        deleteError={deleteError}
        onCancelDeleteConversation={cancelDeleteConversation}
        onConfirmDeleteConversation={() => void deleteConversation()}
        renameTarget={renameTarget}
        renameDraft={renameDraft}
        renameError={renameError}
        onRenameDraftChange={setRenameDraft}
        onCancelRenameConversation={() => setRenameTarget(null)}
        onConfirmRenameConversation={() => void renameConversation()}
        memoryDraft={memoryDraft}
        memorySaving={memorySaving}
        memorySaveError={memorySaveError}
        activeProfile={activeProfile}
        memberName={memberName}
        onMemoryDraftChange={setMemoryDraft}
        onCloseMemoryDialog={closeMemoryDialog}
        onConfirmSaveMemory={() => void saveMessageToMemory()}
      />
    </main>
  );
}
