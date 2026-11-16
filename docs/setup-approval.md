# Concrete setup actions for approval

The user explicitly approved development SQL provisioning plus isolated SQL tests, the nvm switch to Node 24.13.0, and official Ollama installation plus the small model download. Those actions are now complete; see [verification.md](verification.md) for results. The details below retain the reviewed setup contract. This approval does not authorize later-phase features or arbitrary system changes.

## SQL Server development provisioning

Run `./scripts/Provision-Database.ps1 -Execute` once, after approval. Without `-Execute`, the script only prints its plan.

- Connect only to the existing default `localhost` instance using the current Windows identity, with encryption and the accepted local certificate exception.
- Refuse to proceed if `GuildhallDev`, `guildhall_runtime`, or the local runtime credential already exists. Read-only checks found the database/login names unused.
- Create the new `GuildhallDev` database and `guildhall_runtime` SQL login with a random password. Do not change existing databases, logins, server authentication mode, services, ports, or firewall settings.
- Save the password as a Windows DPAPI-protected PSCredential in ignored `.local/runtime.credential.xml`. It is bound to the current Windows user/machine. Never print the password or pass it on command lines.
- Apply the checked-in EF bootstrap migration using the Windows identity. Create only application metadata and EF migration history, and seed one installation identifier.
- Map the runtime login into the database; grant only CONNECT and SELECT on these two tables. Explicitly deny schema-changing database permissions. Phase 0 needs no runtime writes.
- The launcher loads the credential into the host process environment only. It never passes SQL or migration credentials to the desktop.
- A partial failure preserves created Guildhall objects for inspection rather than deleting automatically. Rerunning refuses to overwrite them.

Review the script and generated `.local/migrations.sql` before execution. There is no destructive reset/uninstall command.

## SQL integration tests

Separate approval is needed for tests on the selected instance. The test connection uses the Windows identity against local master and is never forwarded to application processes. Tests create uniquely named `GuildhallTest_<32 hex digits>` databases and test-only users, exercise migration/readiness/permission/failure behavior, and drop only databases the test run successfully created. They never use `GuildhallDev` or existing user databases. No persistent test SQL login is required.

## Node selection

Node `24.13.0` is already installed under nvm and its executable was verified. Proposed system selection is `nvm use 24.13.0`, retaining the previously installed version for switching back. No download or uninstall is proposed. Until approved, development commands can use that existing executable through a process-only PATH prefix, without changing system selection.

## Ollama

The user approved installation of the official per-user Windows app and the `smollm2:135m` discovery-test model. Completed through the Windows package manager with installer hash verification, followed by the approved model pull. Ollama now serves on `127.0.0.1:11434`; no model inference was performed. The Guildhall launcher continues to leave Ollama lifecycle management independent.
