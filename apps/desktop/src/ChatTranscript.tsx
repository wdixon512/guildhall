import type { KeyboardEvent, RefObject, WheelEvent } from 'react';
import type { AgentProfile, Bootstrap, ConversationDetails, MessageView, Readiness, RegisteredProject } from './api';
import { AssistantResponse } from './AssistantResponse';
import { Portrait } from './CouncilMember';
import type { MemberIdentity } from './CouncilMember';
import { Icon } from './Icon';
import { MessageAttachment } from './MessageAttachment';
import { RunTraceList } from './RunTraceList';
import { StreamingMarkdown } from './StreamingMarkdown';
import { queuedPairs, starterSuggestions, waitingMessages } from './chatPresentation';
import { splitThinking } from './thinking';

const ERROR_MESSAGES: Record<string, string> = {
  model_does_not_support_images: 'This model cannot read images. Choose a vision-capable model and send the image again.',
  cli_not_installed: 'This CLI harness was not found on PATH. Open Connections to install it.',
  cli_needs_signin: 'This CLI harness requires sign-in in your terminal before it can run.',
  cli_permission_denied: 'This CLI harness tried to run a tool or command that requires interactive approval (coming in Phases 3–4). Attach files directly in the composer or ask without tool/shell execution.',
  provider_auth_failed: 'Provider rejected the configured API key. Check Connections.',
  provider_rate_limited: 'Provider rate limit exceeded. Wait a moment and try again.',
  provider_unavailable: 'Backing engine is unavailable. Check Connections and try again.',
  generation_timeout: 'The reply timed out before finishing.',
};

function renderEmptyMessageBody(message: MessageView, memberName: string) {
  if (message.status === 'Queued') return 'Queued — waiting for a turn.';
  if (message.status === 'Running') {
    return (
      <span className="waiting-label">
        {waitingMessages(memberName, message.modelId).map((status, index) => (
          <span key={index} aria-hidden="true">
            {status}
            <span className="waiting-dots"><span>.</span><span>.</span><span>.</span></span>
          </span>
        ))}
        <span className="sr-only">Waiting for the first response text from {message.modelId || 'the model'}.</span>
      </span>
    );
  }
  if (message.errorCode && ERROR_MESSAGES[message.errorCode]) return ERROR_MESSAGES[message.errorCode];
  if (message.status === 'Failed') return 'The reply failed before any text was received. Check Connections and the selected model.';
  if (message.errorCode === 'queue_cancelled') return 'Queued message removed.';
  if (message.status === 'Cancelled') return 'Response stopped before any text arrived.';
  if (message.status === 'Interrupted') return 'Response interrupted before any text arrived. Send a new message to continue.';
  return 'The model returned no reply text.';
}

export interface ChatTranscriptProps {
  config: Bootstrap | null;
  conversation: ConversationDetails | null;
  activeProfile: AgentProfile | undefined;
  identity: MemberIdentity;
  memberName: string;
  database: Readiness | null;
  scrolling: {
    transcript: RefObject<HTMLDivElement | null>;
    content: RefObject<HTMLDivElement | null>;
    onScroll: () => void;
    onWheel: (event: WheelEvent) => void;
    onKeyDown: (event: KeyboardEvent) => void;
  };
  copiedMessageId: string | null;
  toolActionBusy?: boolean;
  onCopyMessage: (id: string, text: string) => void;
  onSelectStarter: (suggestion: string) => void;
  onSaveToMemory: (text: string, trigger: HTMLElement) => void;
  onResolveToolDecision?: (runId: string, eventId: string, approved: boolean) => void;
  onRollbackToolEvent?: (runId: string, eventId: string) => void;
  projects?: RegisteredProject[];
  selectedProjectId?: string | null;
  branches?: string[];
  selectedBranch?: string;
  isolateNewWorktree?: boolean;
  onSelectProject?: (project: RegisteredProject | null) => void;
  onSelectBranch?: (branch: string) => void;
  onBrowseProject?: () => void;
  onToggleIsolateNewWorktree?: (value: boolean) => void;
}

export function ChatTranscript({
  config,
  conversation,
  activeProfile,
  identity,
  memberName,
  database,
  scrolling,
  copiedMessageId,
  toolActionBusy = false,
  onCopyMessage,
  onSelectStarter,
  onSaveToMemory,
  onResolveToolDecision,
  onRollbackToolEvent,
  projects = [],
  selectedProjectId = null,
  branches = [],
  selectedBranch = '',
  isolateNewWorktree = false,
  onSelectProject,
  onSelectBranch,
  onBrowseProject,
  onToggleIsolateNewWorktree,
}: ChatTranscriptProps) {
  const queuedTurns = queuedPairs(conversation?.messages ?? []);

  return (
    <div
      className="messages"
      ref={scrolling.transcript}
      tabIndex={0}
      aria-label="Messages"
      onScroll={scrolling.onScroll}
      onWheel={scrolling.onWheel}
      onKeyDown={scrolling.onKeyDown}
    >
      <div className="transcript-content" ref={scrolling.content}>
        {!conversation?.messages.length && !activeProfile?.isArchived && (
          <div className="chat-welcome">
            <span className="welcome-symbol"><Icon name="gate" size={52} /></span>
            <span className="eyebrow">YOUR SEAT AT THE TABLE</span>
            <h1>What shall we work on?</h1>
            <p>Bring an idea. We’ll find a way forward.<br />Everything stays in your workspace.</p>
            <div className="welcome-cards">
              {starterSuggestions(activeProfile).map(suggestion => (
                <button key={suggestion} type="button" onClick={() => onSelectStarter(suggestion)}>
                  {suggestion}
                </button>
              ))}
            </div>
            {onSelectProject && (
              <div className="welcome-project-picker" aria-label="Choose project for session">
                <div className="welcome-project-row">
                  <span className="welcome-project-icon" aria-hidden="true">
                    <Icon name="folder" size={15} />
                  </span>
                  <select
                    className="workspace-project-select"
                    aria-label="Session project"
                    value={selectedProjectId ?? ''}
                    onChange={e => {
                      const found = projects.find(p => p.id === e.target.value) ?? null;
                      onSelectProject(found);
                    }}
                  >
                    <option value="">No project selected</option>
                    {projects.map(p => (
                      <option key={p.id} value={p.id}>
                        {p.name}
                      </option>
                    ))}
                  </select>
                  {onBrowseProject && (
                    <button type="button" className="welcome-browse-btn" onClick={onBrowseProject}>Browse…</button>
                  )}
                </div>
                {(() => {
                  const chosen = projects.find(p => p.id === selectedProjectId);
                  if (!chosen?.isGitRepository || conversation?.conversation.activeWorktreeId) return null;
                  return (
                    <div className="welcome-project-stack">
                      {branches.length > 0 && onSelectBranch && (
                        <div className="welcome-project-row">
                          <span className="welcome-project-icon" aria-hidden="true">
                            <Icon name="branch" size={15} />
                          </span>
                          <select
                            className="workspace-project-select welcome-branch-select"
                            aria-label="Base branch"
                            value={selectedBranch}
                            onChange={e => onSelectBranch(e.target.value)}
                          >
                            {branches.map(b => (
                              <option key={b} value={b}>{b}</option>
                            ))}
                          </select>
                        </div>
                      )}
                      {onToggleIsolateNewWorktree && (
                        <label className="welcome-worktree-check" title="Creates an isolated Git branch on first message">
                          <input
                            type="checkbox"
                            checked={isolateNewWorktree}
                            onChange={e => onToggleIsolateNewWorktree(e.target.checked)}
                          />
                          <span>New worktree</span>
                        </label>
                      )}
                    </div>
                  );
                })()}
              </div>
            )}
          </div>
        )}
        {conversation && [...conversation.messages]
          .sort((a, b) => a.turnNumber - b.turnNumber || (a.role === 'user' ? 0 : 1) - (b.role === 'user' ? 0 : 1))
          .filter(message => !(queuedTurns.has(message.turnNumber) && (message.role === 'user' || message.status === 'Queued')))
          .map(message => (
            <article key={message.id} className={`message ${message.role}`}>
              {message.role === 'assistant' ? (
                <div className="message-identity">
                  <Portrait
                    identity={identity}
                    expression={message.status === 'Running' ? message.text ? 'idea' : 'pondering' : 'neutral'}
                  />
                  <div>
                    <strong>{memberName}</strong>
                    <span>{identity.role.trim() || 'Steward'}{message.modelId ? ` · ${message.modelId}` : ''}</span>
                  </div>
                </div>
              ) : (
                <span className="sr-only">You</span>
              )}
              <div className="message-surface">
                <div className="markdown-body">
                  {message.text ? (
                    message.role === 'assistant' ? (
                      <AssistantResponse
                        text={message.text}
                        streaming={message.status === 'Running'}
                        durationSeconds={message.status === 'Completed' ? message.responseDurationSeconds : null}
                      />
                    ) : (
                      <StreamingMarkdown text={message.text} streaming={false} />
                    )
                  ) : (
                    <p>{renderEmptyMessageBody(message, memberName)}</p>
                  )}
                </div>
                {!!message.attachments?.length && (
                  <div className="message-attachments">
                    {message.attachments.map(file => (
                      <MessageAttachment key={file.id} attachment={file} config={config} />
                    ))}
                  </div>
                )}
                {message.role === 'assistant' && !!message.events?.length && (
                  <RunTraceList
                    runId={message.runId}
                    events={message.events}
                    busy={toolActionBusy}
                    onResolveDecision={onResolveToolDecision}
                    onRollback={onRollbackToolEvent}
                  />
                )}
              </div>
              {message.role === 'assistant' && message.status !== 'Completed' && message.status !== 'Running' && message.status !== 'Queued' && (
                <span className="run-label">{message.status}</span>
              )}
              {!!message.text && (
                <div className="message-tools">
                  <button
                    aria-label={`Copy ${message.role === 'user' ? 'your message' : 'assistant response'}`}
                    onClick={() => onCopyMessage(message.id, message.role === 'assistant' ? splitThinking(message.text).answer : message.text)}
                  >
                    {copiedMessageId === message.id ? (
                      <svg aria-hidden="true" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
                        <path d="m5 12 4 4L19 6" />
                      </svg>
                    ) : (
                      <svg aria-hidden="true" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
                        <rect x="8" y="8" width="12" height="13" rx="2" />
                        <path d="M16 8V5a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h3" />
                      </svg>
                    )}
                  </button>
                  {database?.status === 'ready' && (
                    <button
                      type="button"
                      className="save-memory-button"
                      aria-label="Save to memory"
                      title="Save to memory"
                      onClick={event => {
                        const raw = message.role === 'assistant' ? splitThinking(message.text).answer : message.text;
                        onSaveToMemory(raw.slice(0, 2000), event.currentTarget);
                      }}
                    >
                      Save to memory
                    </button>
                  )}
                  <span className="sr-only" role="status">{copiedMessageId === message.id ? 'Copied to clipboard' : ''}</span>
                </div>
              )}
            </article>
          ))}
      </div>
    </div>
  );
}
