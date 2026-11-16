# Council profile migration — applied

Applied to `localhost / GuildhallDev` on November 16, 2026, after owner approval. Verification found 7 sessions, 7 distinct linked profiles and no profile shared by multiple legacy sessions. The agent-host readiness endpoint reports SQL ready.

**Target:** `localhost / GuildhallDev` only  
**Migration:** `20261116012111_CouncilProfiles`  
**Current required checkpoint:** `20261115231118_ChatAttachments`

This additive migration turns the existing single shared assistant record into independent guild-member profiles for the sessions already in the database. It adds `Role`, `PortraitId`, `CustomPortrait` and `Accent` to `dbo.AgentDefinitions`. For every row in `dbo.Conversations`, it creates a profile copied from that session's current assistant definition and changes only that conversation's `AgentDefinitionId` to the new profile. Archived sessions are included. This deliberately gives every legacy session an independent starting profile; the old data contains no record of earlier per-session portraits or names, so prior differences cannot be reconstructed. New sessions can then share a member profile.

The migration leaves conversation IDs, titles, selected models, archives, chat runs, messages, attachment rows and run snapshots unchanged. It does not create a database or alter SQL Server configuration. The migration is transaction-wrapped. It includes `GRANT INSERT ON dbo.AgentDefinitions TO guildhall_runtime`, needed to add guild members; it adds no schema, server, delete, or broad writer permissions. A read-only permission check of the current local database already shows explicit runtime `SELECT`, `UPDATE` and `INSERT` grants on this table and no `db_owner`, `db_datawriter` or `db_datareader` role membership, so the migration's INSERT grant is idempotent here. Existing profile reads/updates continue to use their table-specific grants.

The desktop imports the former device-wide appearance preference once into the first migrated legacy profile only. This can preserve the current name/role/portrait/accent, including a bounded JPEG/PNG/WebP portrait. It cannot recover a profile value previously overwritten by the old shared editor. The other profiles keep their separate SQL defaults. After the first import, changing one member updates only that member and all sessions linked to it.

The exact migration implementation is [20261116012111_CouncilProfiles.cs](../../../services/agent-host/Persistence/Migrations/20261116012111_CouncilProfiles.cs); the checked-in EF model snapshot records the resulting schema. A dry-run of the guarded script checks the existing migration checkpoint and makes no changes:

```powershell
pwsh -NoProfile -File G:\repos\guildhall\scripts\database\Apply-CouncilProfiles.ps1
```

For historical reference, the approval applied this migration and its table-specific `INSERT` grant using:

```powershell
pwsh -NoProfile -File G:\repos\guildhall\scripts\database\Apply-CouncilProfiles.ps1 -Execute
```

The script refused any database name other than GuildhallDev and refused to run if the current migration checkpoint differed from the one above. It used the existing Windows migration identity. Tests create and drop only their own `GuildhallTest_*` databases; the isolated tests do not apply this change to GuildhallDev.
