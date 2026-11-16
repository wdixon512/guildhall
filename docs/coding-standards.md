# Guildhall Tech Stack & Coding Standards (.NET 10, EF Core 10, Tauri 2, React 19)

**Status:** Active engineering standard  
**Updated:** November 16, 2026  
**Authoritative Upstream References:** [Microsoft Learn — .NET 10 & C# 14](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview), [Microsoft Learn — Dependency Injection Guidelines](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/guidelines), [Microsoft Learn — EF Core 10 What's New & Performance](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew), [Microsoft Learn — EF Core Lazy Loading (`ILazyLoader`)](https://learn.microsoft.com/en-us/ef/core/querying/related-data/lazy), [Microsoft Learn — Applying Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying), [Microsoft Learn — Safe Storage of App Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets), [Tauri 2 Security & Capabilities](https://v2.tauri.app/security/capabilities/), [React 19 Reference (`lazy`, `Suspense`)](https://react.dev/reference/react/lazy).

---

## 1. Package & Dependency Governance (Zero Unapproved Packages)

1. **Explicit Approval Required:** Coding agents must **never** install, upgrade, or remove NuGet (`dotnet add package`), npm (`npm install <pkg>`), or Cargo (`cargo add`) packages without explicit owner approval of the concrete package name, version, license, and size/security impact.
2. **Locked Restores Everywhere:**
   - **.NET 10:** `RestorePackagesWithLockFile` is enabled in `Directory.Build.props`. Always restore with `dotnet restore --locked-mode`.
   - **Frontend (Node 24):** Always install with `npm.cmd --prefix apps/desktop ci` (or `--offline` in sandboxed test runs) against `package-lock.json`.
   - **Tauri (Rust):** Always verify with `cargo test --locked` / `cargo check --locked` against `Cargo.lock`.
3. **Standard Library & Built-In Primitives First:**
   - Prefer built-in `.NET 10` types (`System.Text.Json`, `TimeProvider`, `System.Threading.Channels`, `ConcurrentDictionary`, `IOptionsMonitor<T>`, `IHttpClientFactory`), built-in `EF Core 10` abstractions (`ILazyLoader`, `IDbContextFactory<T>`), and native Web/DOM APIs before proposing any third-party library.

---

## 2. .NET 10 & C# 14 Dependency Injection Standards

### 2.1 Instance Services Over `static` Classes
- **Rule:** Service, configuration, persistence, provider, and orchestration classes must be `sealed` instance classes registered in `IServiceCollection` and resolved via constructor injection—**never** `static` service classes with static methods that read environment variables, clocks, databases, or network sockets directly.
- **When `static` is allowed:**
  - Pure, deterministic, stateless value functions (e.g., byte/character counters, regex/URL validators, or DTO mappers with zero I/O or ambient state).
  - Required C# extension method syntax (`public static IEndpointRouteBuilder MapChat(this IEndpointRouteBuilder app)`), where the extension method only wires routes and delegates work to injected services.
- **Use C# Primary Constructors for DI:**
  ```csharp
  public sealed class PreambleCacheService(
      IOptionsMonitor<PreambleCacheOptions> options,
      TimeProvider timeProvider,
      ILogger<PreambleCacheService> logger) : IPreambleCacheService
  {
      // Dependencies are captured cleanly without boilerplate field assignments.
  }
  ```

### 2.2 Service Lifetimes & Captive Dependency Prevention
- **Lifetime Selection:**
  - **`Singleton` (`AddSingleton`):** Thread-safe, host-lifetime services (`ChatRunner`, `ConcurrentDictionary` preamble cache, `TimeProvider.System`, `IDbContextFactory<GuildhallDbContext>`).
  - **`Scoped` (`AddScoped`):** Request-lifetime services that depend on per-request state.
  - **`Transient` (`AddTransient`):** Lightweight stateless services or `Lazy<T>` wrappers.
- **Zero Captive Dependencies:**
  - Never inject a `Scoped` service (such as `GuildhallDbContext`) directly into a `Singleton`.
  - Singletons and background `IHostedService` workers must inject `IDbContextFactory<GuildhallDbContext>` and create short-lived, explicitly disposed `DbContext` instances per operation (`await using var db = await dbFactory.CreateDbContextAsync(ct);`).
  - Keep `ValidateScopes = true` and `ValidateOnBuild = true` enabled on the host service provider (`builder.Host.UseDefaultServiceProvider(...)`) so lifetime mismatches fail immediately at startup/test time.

### 2.3 Options Pattern (`IOptionsMonitor<T>`) & `TimeProvider`
- **Configuration via Options:** Bind configuration sections to strongly typed classes with `AddOptions<T>().BindConfiguration(...).ValidateDataAnnotations().ValidateOnStart()`. Inject `IOptionsMonitor<T>` into singletons that need live/hot-reloaded configuration updates.
- **Time Abstraction (`TimeProvider`):**
  - Register `builder.Services.AddSingleton(TimeProvider.System);` in `Program.cs`.
  - Inject `TimeProvider` into services and call `timeProvider.GetUtcNow().UtcDateTime` instead of `DateTime.UtcNow` so timeouts, retention windows, and timestamps can be tested deterministically without `Thread.Sleep` or flaky wall-clock waits.

---

## 3. Multi-Layer Lazy Loading & Performance Architecture

Guildhall enforces lazy loading across all three application layers (**EF Core 10**, **.NET 10 DI**, and **React 19**) without adding third-party proxy packages.

### 3.1 Layer 1 — EF Core 10 Lazy Loading (`ILazyLoader` Without Proxies) & Query Discipline
- **Never Install `Microsoft.EntityFrameworkCore.Proxies` (`Castle.Core`):**
  - Dynamic runtime proxies require an extra NuGet package, force unsealed entities and `virtual` navigation properties, and cause accidental synchronous **N+1 database queries** when serializing entities.
- **Use Built-In `ILazyLoader` on `sealed` Entities When Navigation Lazy Loading Is Needed:**
  - `ILazyLoader` is built into `Microsoft.EntityFrameworkCore.Abstractions` (zero extra packages).
  - Inject `ILazyLoader` via a private constructor that EF Core calls when materializing tracked entities:
    ```csharp
    using Microsoft.EntityFrameworkCore.Infrastructure;

    public sealed class ConversationRecord
    {
        private readonly ILazyLoader? _lazyLoader;
        private ICollection<ChatRunRecord>? _runs;

        public ConversationRecord() { }

        private ConversationRecord(ILazyLoader lazyLoader) => _lazyLoader = lazyLoader;

        public Guid Id { get; set; }
        public string Title { get; set; } = "";

        public ICollection<ChatRunRecord> Runs
        {
            get => _lazyLoader.Load(this, ref _runs) ?? (_runs ??= []);
            set => _runs = value;
        }
    }
    ```
- **Respect the `IDbContextFactory` Scope Boundary:**
  - `ILazyLoader` requires the originating `GuildhallDbContext` to remain open and undisposed. Never return raw EF Core entities across API boundaries or access lazy navigations after `await using var db` exits.
  - On high-volume navigations where accidental loop traversal could cause N+1 queries, configure `.EnableLazyLoading(false)` in `OnModelCreating` or set `db.ChangeTracker.LazyLoadingEnabled = false`.
- **Read-Path & Large-Column Lazy/Deferred Loading Rules:**
  1. **`.AsNoTracking()` on All Read-Only Queries:** Avoid change-tracker allocation overhead whenever entities are not mutated in that scope.
  2. **DTO Projection (`.Select(...)`) & Deferred Blob Loading:** Never load `ChatAttachments.Content` (`varbinary(max)` up to 20 MiB per file) during conversation list or message history queries. Project only attachment metadata (`Id`, `FileName`, `ContentType`, `Length`) in chat queries, and load the binary payload lazily only when `GET /attachments/{id}` is explicitly requested.
  3. **`.AsSplitQuery()` for Multi-Collection Loads:** When loading a parent with multiple one-to-many collections in a single scope, use `.AsSplitQuery()` to avoid cartesian explosion.
  4. **EF Core 10 `.LeftJoin()` / `ExecuteUpdateAsync()`:** Prefer EF Core 10's native `.LeftJoin()` LINQ method for optional joins and `ExecuteUpdateAsync(s => ...)` / `ExecuteDeleteAsync()` (which accept standard non-expression lambdas in EF Core 10) for atomic status/archive updates without materializing full entities into memory.
  5. **Never Hold a SQL Transaction Across Network/Model I/O:** Commit run admission in one short transaction, release the `DbContext`, stream from Ollama/cloud/CLI, and open short bounded `DbContext` scopes only when flushing checkpoints (`~500 ms` or `4 KiB`).

### 3.2 Layer 2 — .NET 10 DI Lazy Service Resolution (`Lazy<T>`)
- Register open generic `Lazy<T>` support in `Program.cs`:
  ```csharp
  builder.Services.AddTransient(typeof(Lazy<>), typeof(Lazier<>));
  ```
  where `Lazier<T>(IServiceProvider provider) : Lazy<T>(provider.GetRequiredService<T>, LazyThreadSafetyMode.ExecutionAndPublication)`.
- Inject `Lazy<TService>` into endpoints or orchestrators when `TService` is expensive to initialize or used only on conditional code paths (e.g., export builders, Markdown preamble disk watchers, or optional provider/MCP adapters). This keeps cold host startup fast (`< 700 ms`).

### 3.3 Layer 3 — React 19 & Asset Lazy Loading
- **Component Code-Splitting (`React.lazy` + `<Suspense>`):**
  - Keep the primary chat shell (`App`, `StreamingMarkdown`, `AssistantResponse`) in the initial bundle so first paint is immediate.
  - Lazy-load secondary overlays and management panels (`GuildhallBehaviorSettings`, future `RunInspector`, `ContextInspector`, and `ConnectionsCatalog`) using `React.lazy(() => import(...))` wrapped in `<Suspense>`.
- **On-Demand Media & Expression Sheets:**
  - Keep neutral council portraits on the shared base WebP sheet; load character expression sheets (`pondering`, `idea`, `reading`, `surprised`, `laughing`) and attachment blob URLs (`URL.createObjectURL`) strictly on demand when displayed, and always revoke object URLs on unmount.

---

## 4. User Secrets, DPAPI Credentials & Configuration Automation

1. **Why DPAPI + `UserSecretsId` Coexist:**
   - Per [Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets), `dotnet user-secrets` (`%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`) keeps developer overrides out of source control and works automatically across all Git worktrees that share `<UserSecretsId>guildhall-agent-host-dev</UserSecretsId>` in `Guildhall.AgentHost.csproj`.
   - However, `secrets.json` is stored in **plaintext** on disk. Therefore:
     - **Passwords, SQL runtime credentials, API keys, and OAuth tokens** must remain encrypted at rest using **Windows DPAPI** under `%LOCALAPPDATA%\Guildhall\dev\runtime.credential.xml` (or Windows Credential Manager).
     - **`dotnet user-secrets`** is enabled (`guildhall-agent-host-dev`) strictly for **non-secret** local development overrides (such as custom `GUILDHALL_OLLAMA_URL`, logging levels, or local feature toggles).
2. **Automatic DPAPI Loading in `Start-Dev.ps1`:**
   - All worktrees automatically share `%LOCALAPPDATA%\Guildhall\dev\runtime.credential.xml` via `Get-LocalSqlConnection -Runtime` in `scripts/misc/Common.ps1`—no manual secret copying between worktrees is ever required or permitted.
3. **Subprocess & Log Scrubbing:**
   - Never pass `ConnectionStrings__*`, `GUILDHALL_SESSION_TOKEN`, or provider API keys to Vite, child MCP servers, or wrapped CLI harnesses (`gemini`, `claude`, `codex`, `agent`), and never log or export them.

---

## 5. Database Migration Automation & Safety Gate

1. **Privilege Separation Invariant:**
   - The running application (`Guildhall.AgentHost`) connects as `guildhall_runtime` (DML-only: table-scoped `SELECT`, `INSERT`, `UPDATE`, and parent-row `DELETE` on `Conversations` and `AgentDefinitions`). It has **zero DDL permissions** (`CREATE`, `ALTER`, `DROP`) and never calls `db.Database.MigrateAsync()` inside `Program.cs`.
   - Schema migrations run under the developer's Windows identity (`ConnectionStrings__GuildhallMigrations` with `Integrated Security=true`).
2. **Launcher-Automated Migrations (`Start-Dev.ps1`):**
   - On launch, `Start-Dev.ps1` runs `Invoke-AutoDatabaseMigration` (unless `-SkipMigrations` is passed):
     - Queries `[__EFMigrationsHistory]` in `GuildhallDev` in `< 15 ms` and compares applied migration IDs against `services/agent-host/Persistence/Migrations/*_*.cs`.
     - **If all migrations are already applied:** continues immediately with zero `dotnet ef` overhead.
     - **If pending migrations are purely additive** (new tables, columns, or indexes with no `DROP`, `TRUNCATE`, `GRANT`, `REVOKE`, or `DENY` statements): automatically applies them via `scripts/database/Migrate-Database.ps1 -DatabaseName GuildhallDev -Execute` before starting the host.
     - **If pending migrations contain destructive SQL (`DROP`/`TRUNCATE`) or permission changes (`GRANT`/`REVOKE`):** prints the SQL preview and prompts for `[Y/N]` confirmation (or applies automatically when `-ApproveMigrations` is passed).
3. **Isolated SQL Integration Tests:**
   - Automated tests (`Category=SqlIntegration`) never touch `GuildhallDev`; they create uniquely named `GuildhallTest_<guid>` databases, verify both repeatable migration application and restricted `guildhall_runtime` permissions, and drop only the scratch databases they created.
