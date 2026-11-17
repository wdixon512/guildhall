import { useEffect, useRef } from 'react';
import type { Dispatch, MutableRefObject, RefObject, SetStateAction } from 'react';
import type { AgentProfile, Conversation } from './api';

export interface MemoryDraft {
  text: string;
  scope: 'AboutYou' | 'Member';
}

export interface ConfirmationDialogsProps {
  chatBusy: boolean;
  chatOperation: RefObject<boolean>;
  memberReturnFocus: MutableRefObject<HTMLElement | null>;
  deleteMemberTarget: AgentProfile | null;
  deleteMemberError: string;
  onCancelDeleteMember: () => void;
  onConfirmDeleteMember: (profile: AgentProfile) => void;
  archiveMemberTarget: AgentProfile | null;
  archiveMemberError: string;
  onCancelArchiveMember: () => void;
  onConfirmArchiveMember: (profile: AgentProfile) => void;
  deleteTarget: Conversation | null;
  deleteError: string;
  onCancelDeleteConversation: () => void;
  onConfirmDeleteConversation: () => void;
  renameTarget: Conversation | null;
  renameDraft: string;
  renameError: string;
  onRenameDraftChange: (value: string) => void;
  onCancelRenameConversation: () => void;
  onConfirmRenameConversation: () => void;
  memoryDraft: MemoryDraft | null;
  memorySaving: boolean;
  memorySaveError: string;
  activeProfile: AgentProfile | undefined;
  memberName: string;
  onMemoryDraftChange: Dispatch<SetStateAction<MemoryDraft | null>>;
  onCloseMemoryDialog: () => void;
  onConfirmSaveMemory: () => void;
}

export function ConfirmationDialogs({
  chatBusy,
  chatOperation,
  memberReturnFocus,
  deleteMemberTarget,
  deleteMemberError,
  onCancelDeleteMember,
  onConfirmDeleteMember,
  archiveMemberTarget,
  archiveMemberError,
  onCancelArchiveMember,
  onConfirmArchiveMember,
  deleteTarget,
  deleteError,
  onCancelDeleteConversation,
  onConfirmDeleteConversation,
  renameTarget,
  renameDraft,
  renameError,
  onRenameDraftChange,
  onCancelRenameConversation,
  onConfirmRenameConversation,
  memoryDraft,
  memorySaving,
  memorySaveError,
  activeProfile,
  memberName,
  onMemoryDraftChange,
  onCloseMemoryDialog,
  onConfirmSaveMemory,
}: ConfirmationDialogsProps) {
  const deleteMemberCancel = useRef<HTMLButtonElement | null>(null);
  const archiveMemberDialog = useRef<HTMLElement | null>(null);
  const deleteDialog = useRef<HTMLElement | null>(null);
  const cancelDeleteButton = useRef<HTMLButtonElement | null>(null);

  useEffect(() => {
    if (!archiveMemberTarget) return;
    const dialog = archiveMemberDialog.current;
    dialog?.querySelector<HTMLButtonElement>('button')?.focus();
    const keepFocus = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        event.stopImmediatePropagation();
        if (!chatOperation.current) {
          onCancelArchiveMember();
          memberReturnFocus.current?.focus();
        }
      }
      if (event.key !== 'Tab') return;
      const buttons = dialog?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)');
      if (!buttons?.length) { event.preventDefault(); return; }
      const first = buttons[0];
      const last = buttons[buttons.length - 1];
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    document.addEventListener('keydown', keepFocus, true);
    return () => document.removeEventListener('keydown', keepFocus, true);
  }, [archiveMemberTarget, chatOperation, memberReturnFocus, onCancelArchiveMember]);

  useEffect(() => {
    if (!deleteMemberTarget) return;
    deleteMemberCancel.current?.focus();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        event.stopImmediatePropagation();
        if (!chatBusy) {
          onCancelDeleteMember();
          memberReturnFocus.current?.focus();
        }
        return;
      }
      if (event.key !== 'Tab') return;
      const buttons = document.querySelectorAll<HTMLButtonElement>('[aria-labelledby="delete-member-title"] button:not(:disabled)');
      if (!buttons.length) { event.preventDefault(); return; }
      if (event.shiftKey && document.activeElement === buttons[0]) { event.preventDefault(); buttons[buttons.length - 1].focus(); }
      else if (!event.shiftKey && document.activeElement === buttons[buttons.length - 1]) { event.preventDefault(); buttons[0].focus(); }
    };
    document.addEventListener('keydown', onKeyDown, true);
    return () => document.removeEventListener('keydown', onKeyDown, true);
  }, [deleteMemberTarget, chatBusy, memberReturnFocus, onCancelDeleteMember]);

  useEffect(() => {
    if (!deleteTarget) return;
    cancelDeleteButton.current?.focus();
    const keepDialogFocus = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !chatBusy) { event.preventDefault(); onCancelDeleteConversation(); return; }
      if (event.key !== 'Tab') return;
      const buttons = deleteDialog.current?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)');
      if (!buttons?.length) return;
      const first = buttons[0];
      const last = buttons[buttons.length - 1];
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    document.addEventListener('keydown', keepDialogFocus);
    return () => document.removeEventListener('keydown', keepDialogFocus);
  }, [deleteTarget, chatBusy, onCancelDeleteConversation]);

  return (
    <>
      {deleteMemberTarget && (
        <div className="dialog-backdrop">
          <section
            className="confirm-dialog"
            role="alertdialog"
            aria-modal="true"
            aria-labelledby="delete-member-title"
            aria-describedby="delete-member-description"
          >
            <h2 id="delete-member-title">Permanently delete {deleteMemberTarget.name}?</h2>
            <p id="delete-member-description">
              This permanently deletes <strong>{deleteMemberTarget.name}</strong>, all of their saved sessions, messages, and attachments. This cannot be undone. Existing SQL backups may still contain copies. Replies must finish before deletion.
            </p>
            {deleteMemberError && <p className="chat-error" role="alert">{deleteMemberError}</p>}
            <div>
              <button
                ref={deleteMemberCancel}
                disabled={chatBusy}
                onClick={() => { onCancelDeleteMember(); memberReturnFocus.current?.focus(); }}
              >
                Cancel
              </button>
              <button
                className="danger-button"
                disabled={chatBusy}
                onClick={() => onConfirmDeleteMember(deleteMemberTarget)}
              >
                {chatBusy ? 'Deleting…' : 'Delete member and sessions'}
              </button>
            </div>
          </section>
        </div>
      )}
      {archiveMemberTarget && (
        <div className="dialog-backdrop">
          <section
            ref={archiveMemberDialog}
            tabIndex={-1}
            className="confirm-dialog"
            role="alertdialog"
            aria-modal="true"
            aria-labelledby="archive-member-title"
            aria-describedby="archive-member-description"
          >
            <h2 id="archive-member-title">Archive this member?</h2>
            <p id="archive-member-description">
              <strong>{archiveMemberTarget.name}</strong> will leave the active council. All saved sessions and messages are kept in Archived members. You can restore this member later. Running or queued replies must finish first.
            </p>
            {archiveMemberError && <p className="chat-error" role="alert">{archiveMemberError}</p>}
            <div>
              <button
                disabled={chatBusy}
                onClick={() => { onCancelArchiveMember(); memberReturnFocus.current?.focus(); }}
              >
                Cancel
              </button>
              <button disabled={chatBusy} onClick={() => onConfirmArchiveMember(archiveMemberTarget)}>
                {chatBusy ? 'Archiving…' : 'Archive member'}
              </button>
            </div>
          </section>
        </div>
      )}
      {deleteTarget && (
        <div className="dialog-backdrop">
          <section
            ref={deleteDialog}
            className="confirm-dialog"
            role="alertdialog"
            aria-modal="true"
            aria-labelledby="delete-title"
            aria-describedby="delete-description"
          >
            <h2 id="delete-title">Delete this conversation?</h2>
            <p id="delete-description">
              <strong>{deleteTarget.title}</strong> and its messages will be permanently removed from Guildhall. This cannot be undone. Existing SQL backups may still contain copies.
            </p>
            {deleteError && <p className="chat-error" role="alert">{deleteError}</p>}
            <div>
              <button ref={cancelDeleteButton} onClick={onCancelDeleteConversation} disabled={chatBusy}>Cancel</button>
              <button
                className="danger-button"
                onClick={onConfirmDeleteConversation}
                disabled={chatBusy || deleteTarget.hasActiveRun}
              >
                {chatBusy ? 'Deleting…' : 'Delete conversation'}
              </button>
            </div>
          </section>
        </div>
      )}
      {renameTarget && (
        <div className="dialog-backdrop">
          <form
            className="confirm-dialog"
            onSubmit={event => { event.preventDefault(); onConfirmRenameConversation(); }}
            aria-labelledby="rename-title"
          >
            <h2 id="rename-title">Rename chat</h2>
            <label className="rename-field">
              Chat name
              <input
                autoFocus
                maxLength={200}
                value={renameDraft}
                onChange={event => onRenameDraftChange(event.target.value)}
              />
            </label>
            {renameError && <p className="chat-error" role="alert">{renameError}</p>}
            <div>
              <button type="button" onClick={onCancelRenameConversation} disabled={chatBusy}>Cancel</button>
              <button type="submit" disabled={chatBusy || !renameDraft.trim()}>
                {chatBusy ? 'Saving…' : 'Save name'}
              </button>
            </div>
          </form>
        </div>
      )}
      {memoryDraft && (
        <div className="dialog-backdrop">
          <form
            className="confirm-dialog"
            role="dialog"
            aria-modal="true"
            aria-labelledby="save-memory-title"
            onKeyDown={event => {
              if (event.key === 'Escape' && !memorySaving) {
                event.preventDefault();
                onCloseMemoryDialog();
              }
            }}
            onSubmit={event => { event.preventDefault(); onConfirmSaveMemory(); }}
          >
            <h2 id="save-memory-title">Save to memory</h2>
            <div className="memory-scope-choice" role="group" aria-label="Memory scope">
              <button
                type="button"
                className={memoryDraft.scope === 'AboutYou' ? 'selected' : ''}
                aria-pressed={memoryDraft.scope === 'AboutYou'}
                onClick={() => onMemoryDraftChange(current => current ? { ...current, scope: 'AboutYou' } : null)}
              >
                About you (shared)
              </button>
              {activeProfile && (
                <button
                  type="button"
                  className={memoryDraft.scope === 'Member' ? 'selected' : ''}
                  aria-pressed={memoryDraft.scope === 'Member'}
                  onClick={() => onMemoryDraftChange(current => current ? { ...current, scope: 'Member' } : null)}
                >
                  {memberName} (private)
                </button>
              )}
            </div>
            <label className="rename-field">
              Memory note
              <textarea
                autoFocus
                maxLength={2000}
                rows={4}
                value={memoryDraft.text}
                onChange={event => onMemoryDraftChange(current => current ? { ...current, text: event.target.value } : null)}
              />
            </label>
            <small>{memoryDraft.text.trim().length}/2000</small>
            {memorySaveError && <p className="chat-error" role="alert">{memorySaveError}</p>}
            <div>
              <button type="button" onClick={onCloseMemoryDialog} disabled={memorySaving}>Cancel</button>
              <button
                type="submit"
                disabled={memorySaving || !memoryDraft.text.trim() || memoryDraft.text.trim().length > 2000}
              >
                {memorySaving ? 'Saving…' : 'Save note'}
              </button>
            </div>
          </form>
        </div>
      )}
    </>
  );
}
