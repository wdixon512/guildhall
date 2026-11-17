import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import type { Dispatch, MutableRefObject, SetStateAction } from 'react';
import { createPortal } from 'react-dom';
import type { AgentProfile, Conversation, ConversationDetails, Readiness } from './api';
import { Portrait } from './CouncilMember';
import type { MemberIdentity } from './CouncilMember';
import { Icon } from './Icon';
import { PaneResizeHandle } from './PaneResizeHandle';
import { isUnread } from './chatPresentation';

const toMemberIdentity = (profile: AgentProfile): MemberIdentity => ({
  name: profile.name,
  role: profile.role,
  portrait: profile.portraitId,
  customPortrait: profile.customPortrait ?? '',
  accent: profile.accent,
  instructions: profile.instructions,
});

export interface CouncilSidebarProps {
  profiles: AgentProfile[];
  history: Conversation[];
  activeAgentId: string | null | undefined;
  activeProfile: AgentProfile | undefined;
  conversation: ConversationDetails | null;
  running: boolean;
  readState: Record<string, string>;
  database: Readiness | null;
  chatBusy: boolean;
  chosenModel: string;
  profileDirty: boolean;
  profileSaving: boolean;
  profileHasPendingChanges: (profileId: string) => boolean;
  search: string;
  onSearchChange: (value: string) => void;
  showArchived: boolean;
  onShowArchivedChange: (archived: boolean) => void;
  showArchivedMembers: boolean;
  onToggleArchivedMembers: () => void;
  collapsedMembers: Record<string, boolean>;
  onToggleMemberCollapsed: (profileId: string, currentlyCollapsed: boolean) => void;
  conversationMenu: string | null;
  onSetConversationMenu: Dispatch<SetStateAction<string | null>>;
  memberMenuId: string | null;
  onSetMemberMenuId: Dispatch<SetStateAction<string | null>>;
  memberReturnFocus: MutableRefObject<HTMLElement | null>;
  uiScale: number;
  councilWidth: number;
  councilResizeMax: number;
  onResizeCouncil: (width: number) => void;
  onSelectMember: (profile: AgentProfile) => void;
  onNewSession: (agentId?: string | null) => void;
  onAddGuildMember: () => void;
  onOpenSavedChat: (id: string) => void;
  onRequestRenameConversation: (item: Conversation, title: string) => void;
  onArchiveConversation: (item: Conversation) => void;
  onRequestDeleteConversation: (item: Conversation) => void;
  onRestoreMember: (profile: AgentProfile) => void;
  onRequestArchiveMember: (profile: AgentProfile) => void;
  onRequestDeleteMember: (profile: AgentProfile) => void;
}

export function CouncilSidebar({
  profiles,
  history,
  activeAgentId,
  activeProfile,
  conversation,
  running,
  readState,
  database,
  chatBusy,
  chosenModel,
  profileDirty,
  profileSaving,
  profileHasPendingChanges,
  search,
  onSearchChange,
  showArchived,
  onShowArchivedChange,
  showArchivedMembers,
  onToggleArchivedMembers,
  collapsedMembers,
  onToggleMemberCollapsed,
  conversationMenu,
  onSetConversationMenu,
  memberMenuId,
  onSetMemberMenuId,
  memberReturnFocus,
  uiScale,
  councilWidth,
  councilResizeMax,
  onResizeCouncil,
  onSelectMember,
  onNewSession,
  onAddGuildMember,
  onOpenSavedChat,
  onRequestRenameConversation,
  onArchiveConversation,
  onRequestDeleteConversation,
  onRestoreMember,
  onRequestArchiveMember,
  onRequestDeleteMember,
}: CouncilSidebarProps) {
  const [memberMenuPosition, setMemberMenuPosition] = useState<{ left: number; top: number; width: number } | null>(null);
  const memberMenuAction = useRef<HTMLButtonElement | null>(null);
  const memberMenuElement = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (!memberMenuId) return;
    memberMenuAction.current?.focus();
    const dismiss = (event: PointerEvent) => {
      if (!(event.target instanceof Element) || !event.target.closest('[data-member-menu]')) onSetMemberMenuId(null);
    };
    document.addEventListener('pointerdown', dismiss);
    return () => document.removeEventListener('pointerdown', dismiss);
  }, [memberMenuId, onSetMemberMenuId]);

  useLayoutEffect(() => {
    if (!memberMenuId) return;
    const positionMenu = () => {
      const anchor = memberReturnFocus.current;
      const menu = memberMenuElement.current;
      const list = anchor?.closest('.conversation-list');
      const sidebar = anchor?.closest('.conversation-sidebar');
      if (!anchor || !menu || !list || !sidebar) return;
      const anchorRect = anchor.getBoundingClientRect();
      const menuRect = menu.getBoundingClientRect();
      const listRect = list.getBoundingClientRect();
      const sidebarRect = sidebar.getBoundingClientRect();
      const margin = 8;
      const scale = Number.parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--ui-scale')) || 1;
      const width = Math.min(menuRect.width, sidebarRect.width - margin * 2);
      const left = Math.max(sidebarRect.left + margin, Math.min(anchorRect.right - width, sidebarRect.right - width - margin));
      let top = anchorRect.bottom + 4;
      if (top + menuRect.height > listRect.bottom) top = anchorRect.top - menuRect.height - 4;
      top = Math.max(margin, Math.min(top, window.innerHeight - menuRect.height - margin));
      setMemberMenuPosition({ left: left / scale, top: top / scale, width: width / scale });
    };
    positionMenu();
    window.addEventListener('resize', positionMenu);
    window.addEventListener('scroll', positionMenu, true);
    return () => {
      window.removeEventListener('resize', positionMenu);
      window.removeEventListener('scroll', positionMenu, true);
    };
  }, [memberMenuId, uiScale, memberReturnFocus]);

  const visibleHistory = history.filter(item => item.title.toLowerCase().includes(search.toLowerCase()));
  const profileGroups = profiles
    .filter(profile => !profile.isArchived)
    .map(profile => ({ profile, sessions: visibleHistory.filter(item => item.agentDefinitionId === profile.id) }));
  const archivedProfileGroups = profiles
    .filter(profile => profile.isArchived)
    .map(profile => ({ profile, sessions: visibleHistory.filter(item => item.agentDefinitionId === profile.id) }));

  const renderSession = (item: Conversation, index: number) => {
    const title = item.title === 'New conversation' ? `Conversation ${history.length - index}` : item.title;
    const activeRun = !!item.hasActiveRun || (conversation?.conversation.id === item.id && running);
    const unread = isUnread(item.lastResponseUtc, readState[item.id]);
    return (
      <div
        className={`conversation-row ${conversation?.conversation.id === item.id ? 'selected' : ''} ${unread ? 'is-unread' : ''}`}
        key={item.id}
        onContextMenu={event => { event.preventDefault(); onSetConversationMenu(item.id); }}
      >
        <button
          className={`conversation-card ${conversation?.conversation.id === item.id ? 'selected' : ''} ${unread ? 'is-unread' : ''}`}
          aria-label={`Open ${title}`}
          title={database?.reason === 'migration_required' ? 'Apply the pending database migration to open chats' : undefined}
          disabled={chatBusy || database?.status !== 'ready'}
          onClick={() => onOpenSavedChat(item.id)}
        >
          <span className="conversation-icon"><Icon name="chat" size={18} /></span>
          <span>
            <strong>{title}</strong>
            <small className="conversation-model" title={item.selectedModelId || 'Choose a model'}>
              <span>{item.selectedModelId || 'Choose a model'}</span>
            </small>
          </span>
          {unread && <span className="unread-dot" aria-label="Unread response" title="Unread response" />}
          <span className="sr-only">{!unread && !activeRun ? 'Read' : ''}</span>
          {activeRun && <span className="dot offline" aria-label="Reply pending" title="Reply pending" />}
        </button>
        <button
          className="conversation-menu-toggle icon-button"
          aria-label={`Actions for ${title}`}
          aria-expanded={conversationMenu === item.id}
          onClick={() => onSetConversationMenu(current => current === item.id ? null : item.id)}
        >
          ···
        </button>
        <div className={`conversation-actions ${conversationMenu === item.id ? 'expanded' : ''}`}>
          <button
            aria-label={`Rename ${title}`}
            title="Rename conversation"
            disabled={chatBusy}
            onClick={() => onRequestRenameConversation(item, title)}
          >
            <Icon name="edit" size={16} /><span>Rename</span>
          </button>
          <button
            aria-label={`${item.isArchived ? 'Restore' : 'Archive'} ${title}`}
            title={activeRun ? 'Wait for the response to finish' : item.isArchived ? 'Restore conversation' : 'Archive conversation'}
            disabled={chatBusy || activeRun}
            onClick={() => onArchiveConversation(item)}
          >
            {item.isArchived ? '↩' : <svg aria-hidden="true" viewBox="0 0 20 20"><path d="M3 4.5h14l-1 3H4l-1-3Zm1.5 4.5v7h11V9M10 10v4m0 0-2-2m2 2 2-2" /></svg>}
            <span>{item.isArchived ? 'Restore' : 'Archive'}</span>
          </button>
          <button
            aria-label={`Delete ${title}`}
            title={activeRun ? 'Wait for the response to finish' : 'Permanently delete conversation'}
            disabled={chatBusy || activeRun}
            onClick={() => onRequestDeleteConversation(item)}
          >
            <Icon name="trash" size={16} /><span>Delete</span>
          </button>
          <button
            type="button"
            aria-label={`Assign ${title} to guild member — planned`}
            title="Assign to guild member — planned for Phase 7"
            disabled
          >
            <Icon name="council" size={16} /><span>Assign to guild member…</span>
          </button>
        </div>
      </div>
    );
  };

  const renderMember = ({ profile, sessions }: { profile: AgentProfile; sessions: Conversation[] }) => {
    const isCollapsed = !!collapsedMembers[profile.id] && !search.trim();
    return (
      <section className={`council-group ${activeAgentId === profile.id ? 'active' : ''}`} key={profile.id}>
        <div
          className="council-group-heading"
          data-member-menu={memberMenuId === profile.id ? '' : undefined}
          onContextMenu={event => {
            event.preventDefault();
            memberReturnFocus.current = document.getElementById(`member-actions-${profile.id}`);
            onSetMemberMenuId(profile.id);
          }}
          onKeyDown={event => {
            if (event.key === 'Escape' && memberMenuId === profile.id) {
              event.preventDefault();
              event.stopPropagation();
              onSetMemberMenuId(null);
              memberReturnFocus.current?.focus();
            }
            if (event.key === 'Tab') onSetMemberMenuId(null);
          }}
        >
          <button
            type="button"
            className={`icon-button council-collapse-toggle ${isCollapsed ? '' : 'expanded'}`}
            aria-expanded={!isCollapsed}
            aria-controls={`member-sessions-${profile.id}`}
            aria-label={`${isCollapsed ? 'Expand' : 'Collapse'} ${profile.name} sessions`}
            title={`${isCollapsed ? 'Expand' : 'Collapse'} sessions`}
            onClick={() => onToggleMemberCollapsed(profile.id, isCollapsed)}
          >
            <Icon name="chevron" size={14} />
          </button>
          <button
            className="council-profile-select"
            aria-label={`Select ${profile.name}`}
            aria-pressed={activeAgentId === profile.id}
            disabled={chatBusy}
            onClick={() => onSelectMember(profile)}
          >
            <Portrait identity={toMemberIdentity(profile)} />
            <span>
              <strong>{profile.name}</strong>
              <small>{profile.isArchived ? 'Archived · ' : ''}{profile.role} · {sessions.length} shown</small>
            </span>
          </button>
          {!profile.isArchived && (
            <button
              className="icon-button council-new-session"
              aria-label={`New session with ${profile.name}`}
              title={`New session with ${profile.name}`}
              disabled={chatBusy || !chosenModel || database?.status !== 'ready'}
              onClick={() => onNewSession(profile.id)}
            >
              <Icon name="plus" size={16} />
            </button>
          )}
          <button
            id={`member-actions-${profile.id}`}
            className="icon-button member-menu-toggle"
            aria-label={`Member actions for ${profile.name}`}
            aria-haspopup="menu"
            aria-expanded={memberMenuId === profile.id}
            aria-controls={memberMenuId === profile.id ? `member-menu-${profile.id}` : undefined}
            onClick={event => {
              memberReturnFocus.current = event.currentTarget;
              onSetMemberMenuId(value => value === profile.id ? null : profile.id);
            }}
          >
            ···
          </button>
          {memberMenuId === profile.id && createPortal(
            <div
              ref={memberMenuElement}
              data-member-menu=""
              style={memberMenuPosition ?? undefined}
              className="member-menu"
              role="menu"
              id={`member-menu-${profile.id}`}
              aria-label={`Member actions for ${profile.name}`}
              onKeyDown={event => {
                if (event.key === 'Escape') {
                  event.preventDefault();
                  onSetMemberMenuId(null);
                  memberReturnFocus.current?.focus();
                  return;
                }
                if (event.key === 'Tab') {
                  event.preventDefault();
                  const group = memberReturnFocus.current?.closest('.council-group');
                  const nextSession = group?.querySelector<HTMLButtonElement>('.conversation-row button');
                  const nextMember = group?.nextElementSibling?.querySelector<HTMLButtonElement>('.council-profile-select');
                  onSetMemberMenuId(null);
                  if (event.shiftKey) memberReturnFocus.current?.focus();
                  else (nextSession ?? nextMember ?? memberReturnFocus.current)?.focus();
                  return;
                }
                if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
                  event.preventDefault();
                  const actions = Array.from(event.currentTarget.querySelectorAll<HTMLButtonElement>('[role="menuitem"]:not(:disabled)'));
                  if (!actions.length) return;
                  const current = actions.indexOf(document.activeElement as HTMLButtonElement);
                  const next = event.key === 'Home' ? 0 : event.key === 'End' ? actions.length - 1 : (current + (event.key === 'ArrowUp' ? -1 : 1) + actions.length) % actions.length;
                  actions[next]?.focus();
                }
              }}
            >
              <button
                ref={memberMenuAction}
                className="member-menu-item"
                role="menuitem"
                aria-label={profile.isArchived ? 'Restore member' : 'Archive member'}
                title={profile.isArchived ? 'Restore this guild member' : 'Archive this member and keep its sessions'}
                disabled={chatBusy || profileHasPendingChanges(profile.id) || database?.status !== 'ready'}
                onClick={() => {
                  if (profile.isArchived) onRestoreMember(profile);
                  else {
                    onSetMemberMenuId(null);
                    onRequestArchiveMember(profile);
                  }
                }}
              >
                <Icon name="archive" size={15} /><span>{profile.isArchived ? 'Restore' : 'Archive'}</span>
              </button>
              <button
                className="member-menu-item destructive"
                role="menuitem"
                aria-label="Delete member and sessions"
                title="Permanently delete this member and all sessions"
                disabled={chatBusy || profileHasPendingChanges(profile.id) || database?.status !== 'ready'}
                onClick={() => {
                  onSetMemberMenuId(null);
                  onRequestDeleteMember(profile);
                }}
              >
                <Icon name="trash" size={15} /><span>Delete…</span>
              </button>
            </div>,
            document.body,
          )}
        </div>
        {!isCollapsed && (
          <div id={`member-sessions-${profile.id}`} className="council-sessions">
            {sessions.map(item => renderSession(item, history.findIndex(entry => entry.id === item.id)))}
            {!sessions.length && (
              <p className="council-no-sessions">
                {search ? 'No matching sessions.' : showArchived ? 'No archived sessions.' : 'No sessions yet.'}
              </p>
            )}
          </div>
        )}
      </section>
    );
  };

  return (
    <aside id="council-pane" className="conversation-sidebar" aria-label="Council and sessions">
      <div className="sidebar-top">
        <h1>Your council</h1>
        <label className="search-box">
          <Icon name="search" />
          <input
            aria-label="Search loaded conversations"
            placeholder="Search chats…"
            value={search}
            onChange={event => onSearchChange(event.target.value)}
          />
        </label>
        <button
          className="new-chat"
          onClick={() => onNewSession()}
          disabled={chatBusy || profileDirty || profileSaving || !chosenModel || !activeProfile || activeProfile.isArchived || database?.status !== 'ready'}
        >
          <Icon name="plus" /> New session
        </button>
        <button
          className="add-member"
          onClick={onAddGuildMember}
          disabled={chatBusy || profileDirty || profileSaving || database?.status !== 'ready'}
        >
          <Icon name="council" size={17} /> Add guild member
        </button>
      </div>
      <div className="conversation-filter" role="group" aria-label="Conversation list filter">
        <button className={!showArchived ? 'active' : ''} aria-pressed={!showArchived} onClick={() => onShowArchivedChange(false)}>
          Chats
        </button>
        <button className={showArchived ? 'active' : ''} aria-pressed={showArchived} onClick={() => onShowArchivedChange(true)}>
          Archived
        </button>
      </div>
      <div className="conversation-list">
        {profileGroups.map(renderMember)}
        <button
          className="archived-members-toggle"
          aria-expanded={showArchivedMembers}
          aria-controls="archived-members"
          onClick={onToggleArchivedMembers}
        >
          {showArchivedMembers ? '▾' : '▸'} Archived members ({archivedProfileGroups.length})
        </button>
        {showArchivedMembers && (
          <div id="archived-members">
            {archivedProfileGroups.map(renderMember)}
            {!archivedProfileGroups.length && <p className="sidebar-empty">No archived members.</p>}
          </div>
        )}
        {!profiles.length && visibleHistory.map(renderSession)}
        {!history.length && !profiles.length && <p className="sidebar-empty">Your conversations will live here.</p>}
      </div>
      <div className="sidebar-footer">
        <span className="dot" /> Local workspace <small>SQL Server · Ollama</small>
      </div>
      <PaneResizeHandle
        className="council-resizer"
        label="Resize council and chat panes"
        controls="council-pane chat-pane"
        value={Math.min(councilWidth, councilResizeMax)}
        min={220}
        max={councilResizeMax}
        direction={1}
        onResize={onResizeCouncil}
      />
    </aside>
  );
}
