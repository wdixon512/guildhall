# Decision 0004 — Chat attachments

Status: approved and applied to GuildhallDev on November 15, 2026.

## Product behavior

- A message may contain up to five files, 20 MiB each and 50 MiB total.
- Files are stored as SQL Server `varbinary(max)` in `dbo.ChatAttachments`, related to their user message. Deleting a conversation cascades its attachments.
- File names, media types, and sizes are returned with conversation history. Downloads require the authenticated local host API.
- The current Ollama adapter sends UTF-8 text files up to 256 KiB total and recognized PNG, JPEG, GIF, and WebP images. Other uploaded files remain available to download, and the assistant is told their contents were not sent. Image interpretation still depends on the selected model supporting vision.
- No cloud storage, external file service, OCR, PDF/Office parser, or project-file access is introduced.

## SQL change prepared

The additive migration creates `dbo.ChatAttachments`, a foreign key to `dbo.ChatMessages` with cascade delete, and an index on `MessageId`. It grants `guildhall_runtime` SELECT, INSERT, and UPDATE on this table only. It grants no direct DELETE or schema changes. The existing conversation-only DELETE grant removes attachments indirectly through the existing cascade chain.

The approved apply command:

```powershell
pwsh -NoProfile -File .\scripts\database\Migrate-Database.ps1 -DatabaseName GuildhallDev
```

It applies the pending `20261115231118_ChatAttachments` migration and the table-specific grant. Applied after explicit owner approval. Verification using the runtime identity confirmed SELECT/INSERT/UPDATE and no direct DELETE permission on this table. A temporary authenticated host reported SQL ready and successfully opened all seven loaded active/archived saved conversations; no inference was requested.

## Limits and retention

Guildhall keeps the exact uploaded bytes in the local application database. The 50 MiB request cap bounds each message; total database growth remains controlled by deleting conversations and normal SQL Server backup retention. Attachment previews are fetched through the authenticated host and created as temporary in-memory object URLs.

