import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import type { ClipboardEvent, Dispatch, KeyboardEvent, RefObject, SetStateAction } from 'react';
import type { AgentProfile, ConnectionsCatalogData, ConversationDetails, Discovery, MessageView, Readiness } from './api';
import { DropUpSelect } from './DropUpSelect';
import type { DropUpOption } from './DropUpSelect';
import { Icon } from './Icon';
import { formatAttachmentSize, groupModelsByEffort } from './chatPresentation';

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

export interface ChatComposerProps {
  composerInput: RefObject<HTMLTextAreaElement | null>;
  activeProfile: AgentProfile | undefined;
  memberName: string;
  conversation: ConversationDetails | null;
  host: string;
  database: Readiness | null;
  models: Discovery | null;
  connections: ConnectionsCatalogData | null;
  chosenProvider: string;
  chosenModel: string;
  chosenHarnessMode: string;
  draft: string;
  onDraftChange: Dispatch<SetStateAction<string>>;
  selectedFiles: File[];
  onSelectedFilesChange: Dispatch<SetStateAction<File[]>>;
  editingQueuedRunId: string | null;
  onCancelEditQueued: () => void;
  deliveryMode: 'queue' | 'send_now';
  onDeliveryModeChange: (mode: 'queue' | 'send_now') => void;
  chatBusy: boolean;
  canSend: boolean;
  running: boolean;
  queuedCount: number;
  hasPendingWork: boolean;
  hasDraft: boolean;
  archiveMemberError: string;
  hasArchiveMemberTarget: boolean;
  chatError: string;
  onChatError: (message: string) => void;
  onSendMessage: () => void;
  onStopResponse: () => void;
  onPromoteQueued: (runId?: string) => void;
  onEditQueued: (assistantMessage: MessageView, userMessage?: MessageView) => void;
  onRemoveQueued: (runId?: string) => void;
  onSelectBackingEngine: (providerId: string, modelId: string, harnessMode?: string) => void;
  onSelectModel: (modelId: string) => void;
}

export function ChatComposer({
  composerInput,
  activeProfile,
  memberName,
  conversation,
  host,
  database,
  models,
  connections,
  chosenProvider,
  chosenModel,
  chosenHarnessMode,
  draft,
  onDraftChange,
  selectedFiles,
  onSelectedFilesChange,
  editingQueuedRunId,
  onCancelEditQueued,
  deliveryMode,
  onDeliveryModeChange,
  chatBusy,
  canSend,
  running,
  queuedCount,
  hasPendingWork,
  hasDraft,
  archiveMemberError,
  hasArchiveMemberTarget,
  chatError,
  onChatError,
  onSendMessage,
  onStopResponse,
  onPromoteQueued,
  onEditQueued,
  onRemoveQueued,
  onSelectBackingEngine,
  onSelectModel,
}: ChatComposerProps) {
  const filePicker = useRef<HTMLInputElement | null>(null);

  useLayoutEffect(() => {
    if (!composerInput.current) return;
    composerInput.current.style.height = 'auto';
    composerInput.current.style.height = `${Math.min(160, Math.max(30, composerInput.current.scrollHeight))}px`;
  }, [draft, composerInput]);

  function handleComposerKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      onSendMessage();
    }
  }

  function addAttachments(files: File[]) {
    if (chatBusy || database?.status !== 'ready') {
      onChatError('Wait for the current action to finish before attaching files.');
      return;
    }
    const combined = [...selectedFiles, ...files];
    if (combined.length > 5) {
      onChatError('Attach up to five files per message.');
      return;
    }
    if (combined.some(file => file.size > 20 * 1024 * 1024) || combined.reduce((sum, file) => sum + file.size, 0) > 50 * 1024 * 1024) {
      onChatError('Each file can be up to 20 MB and all files together up to 50 MB.');
      return;
    }
    onSelectedFilesChange(combined);
    onChatError('');
  }

  function pasteIntoComposer(event: ClipboardEvent<HTMLTextAreaElement>) {
    const files = Array.from(event.clipboardData.items ?? [])
      .filter(item => item.kind === 'file')
      .map(item => item.getAsFile())
      .filter((file): file is File => file !== null);
    if (!files.length) files.push(...Array.from(event.clipboardData.files ?? []));
    // Leave ordinary text pastes and native selection replacement untouched.
    if (!files.length) return;
    event.preventDefault();
    addAttachments(files);
    const text = event.clipboardData.getData('text/plain');
    if (text) {
      const textarea = event.currentTarget;
      const { selectionStart, selectionEnd } = textarea;
      onDraftChange(value => value.slice(0, selectionStart) + text + value.slice(selectionEnd));
      window.setTimeout(() => {
        if (document.activeElement === textarea) textarea.setSelectionRange(selectionStart + text.length, selectionStart + text.length);
      }, 0);
    }
  }

  const queuedMessages = (conversation?.messages ?? []).filter(message => message.role === 'assistant' && message.status === 'Queued');

  return (
    <div className="composer-region">
      {activeProfile?.isArchived && (
        <p className="archived-member-notice">Archived member · Saved sessions are kept. Restore the member to start a new session.</p>
      )}
      {archiveMemberError && !hasArchiveMemberTarget && <p className="chat-error" role="alert">{archiveMemberError}</p>}
      {chatError && <p className="chat-error" role="alert">{chatError}</p>}
      {!!queuedMessages.length && (
        <section className="queue-panel" aria-label="Queued messages">
          <h3>{queuedMessages.length} queued</h3>
          {queuedMessages.map(message => {
            const userMessage = conversation?.messages.find(candidate => candidate.role === 'user' && candidate.turnNumber === message.turnNumber);
            return (
              <div className="queue-item" key={message.id}>
                <span className="queue-snippet">
                  {userMessage?.text || (userMessage?.attachments?.length ? `${userMessage.attachments.length} attachment${userMessage.attachments.length === 1 ? '' : 's'}` : 'Attachment message')}
                </span>
                {!!userMessage?.attachments?.length && (
                  <span className="queue-file-count">
                    {userMessage.attachments.length} {userMessage.attachments.length === 1 ? 'file' : 'files'}
                  </span>
                )}
                <div className="queue-actions">
                  <button
                    type="button"
                    aria-label={`Send queued message ${message.turnNumber} now`}
                    title="Send now"
                    disabled={chatBusy}
                    onClick={() => onPromoteQueued(message.runId)}
                  >
                    <Icon name="send" size={17} />
                  </button>
                  <button
                    type="button"
                    aria-label={`Edit queued message ${message.turnNumber}`}
                    title={hasDraft ? 'Clear your draft before editing a queued message' : 'Edit'}
                    disabled={chatBusy || hasDraft}
                    onClick={() => onEditQueued(message, userMessage)}
                  >
                    <Icon name="edit" size={17} />
                  </button>
                  <button
                    type="button"
                    aria-label={`Remove queued message ${message.turnNumber}`}
                    title="Delete"
                    disabled={chatBusy}
                    onClick={() => onRemoveQueued(message.runId)}
                  >
                    <Icon name="trash" size={17} />
                  </button>
                </div>
              </div>
            );
          })}
        </section>
      )}
      {editingQueuedRunId && (
        <div className="queue-editing" role="status">
          Editing queued message <button type="button" onClick={onCancelEditQueued}>Cancel edit</button>
        </div>
      )}
      <div className="composer">
        {selectedFiles.length > 0 && (
          <div className="pending-attachments" aria-label="Attachments to send">
            {selectedFiles.map((file, index) => (
              <span key={`${file.name}-${index}`}>
                <AttachmentThumbnail file={file} />
                <span>▤ {file.name} · {formatAttachmentSize(file.size)}</span>
                <button
                  aria-label={`Remove ${file.name}`}
                  disabled={chatBusy}
                  onClick={() => onSelectedFilesChange(items => items.filter((_, itemIndex) => itemIndex !== index))}
                >
                  ×
                </button>
              </span>
            ))}
          </div>
        )}
        {editingQueuedRunId && <span className="queue-editing-label">Editing queued message · Send to replace it</span>}
        <textarea
          ref={composerInput}
          aria-label="Message"
          value={draft}
          onChange={event => onDraftChange(event.target.value)}
          onKeyDown={handleComposerKeyDown}
          onPaste={pasteIntoComposer}
          placeholder={chosenModel ? `Message ${memberName}…` : 'Choose a backing engine & model to start…'}
          disabled={host !== 'Connected' || database?.status !== 'ready' || (chosenProvider === 'ollama' && (!models || models.status !== 'available')) || (!conversation && (!chosenModel || !activeProfile || activeProfile.isArchived))}
          rows={1}
        />
        <input
          ref={filePicker}
          className="sr-only"
          type="file"
          multiple
          aria-label="Choose attachments"
          onChange={event => { addAttachments(Array.from(event.currentTarget.files ?? [])); event.currentTarget.value = ''; }}
        />
        <div className="composer-toolbar">
          <button
            className="attach-button"
            aria-label="Add attachments"
            title="Add files (up to 20 MB each)"
            onClick={() => filePicker.current?.click()}
            disabled={chatBusy || database?.status !== 'ready'}
          >
            <Icon name="attach" size={22} />
          </button>
          {connections && (() => {
            const activeEngines = connections.entries.filter(
              e => !e.isPlanned && (e.engineKind === 'local' || e.engineKind === 'cloud_api' || e.engineKind === 'cli_harness'),
            );
            const connected = activeEngines.filter(e => e.status === 'Connected');
            const disconnected = activeEngines.filter(e => e.status !== 'Connected');
            const kindTag = (kind: string) => kind === 'local' ? 'Local' : kind === 'cloud_api' ? 'API' : 'CLI';
            const engineOptions: DropUpOption[] = [
              ...connected.map(entry => ({
                value: entry.id,
                label: `${kindTag(entry.engineKind)} · ${entry.displayName}`,
                group: 'Connected',
              })),
              ...disconnected.map(entry => ({
                value: entry.id,
                label: `${kindTag(entry.engineKind)} · ${entry.displayName} (${entry.status.toLowerCase()})`,
                group: 'Not connected',
                disabled: true,
              })),
            ];
            return (
              <DropUpSelect
                ariaLabel="Backing engine"
                prefixLabel="Engine"
                value={chosenProvider}
                placeholder="Backing engine"
                disabled={chatBusy}
                options={engineOptions}
                onChange={nextProvider => {
                  const entry = connections.entries.find(e => e.id === nextProvider);
                  const nextModel = entry?.defaultModelId ?? entry?.models[0]?.id ?? '';
                  onSelectBackingEngine(nextProvider, nextModel);
                }}
              />
            );
          })()}
          {(() => {
            const activeEntry = connections?.entries.find(e => e.id === chosenProvider);
            const isCli = activeEntry?.engineKind === 'cli_harness';
            const availableModels = chosenProvider === 'ollama'
              ? (models?.models ?? [])
              : (activeEntry?.models ?? []);
            const groupedModels = groupModelsByEffort(availableModels);
            const activeGroup = groupedModels.find(g => g.defaultModelId === chosenModel || g.variants.some(v => v.modelId === chosenModel));
            const selectedBaseKey = activeGroup?.baseKey ?? chosenModel;
            const modelOptions: DropUpOption[] = [
              ...(chosenModel && !activeGroup
                ? [{ value: chosenModel, label: `${chosenModel} (unavailable)`, disabled: true }]
                : []),
              ...groupedModels.map(g => ({ value: g.baseKey, label: g.displayName })),
            ];
            return (
              <>
                <DropUpSelect
                  ariaLabel={`Model for ${conversation ? 'this conversation' : 'a new conversation'}`}
                  prefixLabel="Model"
                  value={selectedBaseKey}
                  placeholder="Choose a model"
                  disabled={chatBusy || (chosenProvider === 'ollama' && models?.status !== 'available')}
                  options={modelOptions}
                  onChange={nextBaseKey => {
                    const group = groupedModels.find(g => g.baseKey === nextBaseKey);
                    onSelectModel(group ? group.defaultModelId : nextBaseKey);
                  }}
                />
                {activeGroup && activeGroup.variants.length > 1 && (
                  <DropUpSelect
                    ariaLabel="Reasoning effort"
                    prefixLabel="Effort"
                    value={chosenModel}
                    disabled={chatBusy}
                    options={activeGroup.variants.map(v => ({ value: v.modelId, label: v.label }))}
                    onChange={nextModelId => onSelectModel(nextModelId)}
                  />
                )}
                {isCli && (
                  <DropUpSelect
                    ariaLabel="CLI harness mode"
                    prefixLabel="Mode"
                    value={chosenHarnessMode}
                    disabled={chatBusy}
                    options={[
                      { value: 'read_only', label: 'Read-only (safe)' },
                      { value: 'workspace_write', label: 'Workspace write (scoped)' },
                    ]}
                    onChange={nextMode => onSelectBackingEngine(chosenProvider, chosenModel, nextMode)}
                  />
                )}
              </>
            );
          })()}
          <span className="composer-spacer" />
          {hasPendingWork && (
            <select
              className="delivery-mode"
              aria-label="Follow-up delivery"
              value={deliveryMode}
              onChange={event => onDeliveryModeChange(event.target.value as 'queue' | 'send_now')}
            >
              <option value="queue">Queue</option>
              <option value="send_now">Send Now</option>
            </select>
          )}
          {running && (
            <button
              className="send-button stop-button"
              aria-label="Stop response"
              title="Stop response"
              onClick={onStopResponse}
              disabled={chatBusy}
            >
              <svg aria-hidden="true" width="14" height="14" viewBox="0 0 16 16" fill="currentColor">
                <rect x="2" y="2" width="12" height="12" rx="2" />
              </svg>
            </button>
          )}
          {(!running || hasDraft) && (
            <button
              className="send-button"
              aria-label={editingQueuedRunId ? 'Replace queued message' : hasPendingWork ? deliveryMode === 'queue' ? 'Queue message' : 'Send now' : 'Send'}
              title={editingQueuedRunId ? 'Replace queued message (Enter)' : 'Send (Enter)'}
              onClick={onSendMessage}
              disabled={!canSend || (!draft.trim() && selectedFiles.length === 0) || chatBusy || (queuedCount >= 8 && !editingQueuedRunId)}
            >
              <Icon name="send" size={22} />
            </button>
          )}
        </div>
      </div>
      <footer>
        {queuedCount > 0
          ? `${queuedCount} queued · each reply keeps the model selected when sent`
          : hasDraft
            ? 'Enter to send · Shift+Enter for a new line'
            : 'Files stay in this workspace · Text and images are sent to compatible local models'}
      </footer>
    </div>
  );
}
