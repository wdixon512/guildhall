using System.Net;
using System.Net.Http.Headers;
using Guildhall.AgentHost.Api;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Mcp;
using Guildhall.AgentHost.Providers.Cli;
using Guildhall.AgentHost.Providers.Cloud;
using Guildhall.AgentHost.Providers.Ollama;

namespace Guildhall.AgentHost.Providers;

public sealed class ConnectionCatalogService(
    OllamaDiscovery ollamaDiscovery,
    ICredentialStore credentialStore,
    ICliProcessRunner cliProcessRunner,
    EnvironmentScrubber environmentScrubber,
    IHttpClientFactory httpClients,
    TimeProvider timeProvider,
    McpRegistry? mcpRegistry = null,
    McpHealthSupervisor? mcpHealth = null)
{
    public static class Statuses
    {
        public const string Connected = "Connected";
        public const string NotConnected = "Not connected";
        public const string NeedsSignIn = "Needs sign-in";
        public const string Unavailable = "Unavailable";
        public const string Planned = "Planned";
    }

    public async Task<string?> ResolveDefaultModelIdAsync(string? providerId, CancellationToken ct)
    {
        var effectiveProvider = string.IsNullOrWhiteSpace(providerId) ? "ollama" : providerId.Trim();

        var cli = CliHarnessDescriptors.Find(effectiveProvider);
        if (cli is not null)
            return cli.DefaultModelId;

        if (string.Equals(effectiveProvider, "ollama", StringComparison.OrdinalIgnoreCase))
        {
            var discovery = await ollamaDiscovery.DiscoverAsync(ct);
            return discovery.Models.FirstOrDefault(m => m.Id == "qwen3:4b")?.Id
                ?? discovery.Models.FirstOrDefault(m => m.Id == "qwen3:1.7b")?.Id
                ?? discovery.Models.FirstOrDefault()?.Id;
        }

        return CloudModelCatalog.GetModels(effectiveProvider).FirstOrDefault()?.Id;
    }

    public async Task<ConnectionsCatalogResponse> GetCatalogAsync(bool runLiveProbes, CancellationToken ct)
    {
        var entries = new List<ConnectionEntryResponse>
        {
            await BuildOllamaEntryAsync(ct),
            await BuildCloudEntryAsync("openai-api", "OpenAI API", runLiveProbes, ct),
            await BuildCloudEntryAsync("gemini-api", "Google Gemini API", runLiveProbes, ct),
            await BuildCloudEntryAsync("anthropic-api", "Anthropic Claude API", runLiveProbes, ct)
        };

        foreach (var descriptor in CliHarnessDescriptors.All)
            entries.Add(await BuildCliEntryAsync(descriptor, runLiveProbes, ct));

        entries.Add(await BuildLocalMcpEntryAsync("filesystem-mcp", "Local Filesystem MCP (Read-Only)", runLiveProbes, ct));
        entries.Add(await BuildLocalMcpEntryAsync("git-mcp", "Local Git MCP (Read-Only)", runLiveProbes, ct));
        entries.Add(await BuildCredentialMcpEntryAsync("github-mcp", "GitHub MCP", "Personal Access Token (DPAPI)", runLiveProbes, ct));
        entries.Add(await BuildCredentialMcpEntryAsync("google-drive-mcp", "Google Drive MCP", "OAuth access token (DPAPI)", runLiveProbes, ct));
        entries.Add(await BuildCredentialMcpEntryAsync("gmail-mcp", "Gmail MCP", "OAuth access token (DPAPI)", runLiveProbes, ct));
        entries.Add(await BuildSqlMcpEntryAsync(runLiveProbes, ct));

        return new ConnectionsCatalogResponse(entries, timeProvider.GetUtcNow());
    }

    public async Task<ConnectionEntryResponse?> ProbeAsync(string id, CancellationToken ct)
    {
        if (string.Equals(id, "ollama", StringComparison.OrdinalIgnoreCase))
            return await BuildOllamaEntryAsync(ct);

        if (id is "openai-api" or "gemini-api" or "anthropic-api")
        {
            var name = id switch
            {
                "openai-api" => "OpenAI API",
                "gemini-api" => "Google Gemini API",
                _ => "Anthropic Claude API"
            };
            return await BuildCloudEntryAsync(id, name, runLiveProbe: true, ct);
        }

        if (id is "filesystem-mcp" or "git-mcp")
        {
            var name = id == "filesystem-mcp" ? "Local Filesystem MCP (Read-Only)" : "Local Git MCP (Read-Only)";
            return await BuildLocalMcpEntryAsync(id, name, runLiveProbe: true, ct);
        }

        if (id is "github-mcp" or "google-drive-mcp" or "gmail-mcp")
        {
            var (name, hint) = id switch
            {
                "github-mcp" => ("GitHub MCP", "Personal Access Token (DPAPI)"),
                "google-drive-mcp" => ("Google Drive MCP", "OAuth access token (DPAPI)"),
                _ => ("Gmail MCP", "OAuth access token (DPAPI)")
            };
            return await BuildCredentialMcpEntryAsync(id, name, hint, runLiveProbe: true, ct);
        }

        if (string.Equals(id, "sql-server-mcp", StringComparison.OrdinalIgnoreCase))
            return await BuildSqlMcpEntryAsync(runLiveProbe: true, ct);

        var descriptor = CliHarnessDescriptors.Find(id);
        if (descriptor is not null)
            return await BuildCliEntryAsync(descriptor, runLiveProbe: true, ct);

        return null;
    }

    private async Task<ConnectionEntryResponse> BuildOllamaEntryAsync(CancellationToken ct)
    {
        var discovery = await ollamaDiscovery.DiscoverAsync(ct);
        var connected = discovery.Status is "available" or "no_models";
        var models = discovery.Models
            .Select(m => new ConnectionModelOption(m.Id, m.DisplayName, false))
            .ToArray();

        return new ConnectionEntryResponse(
            Id: "ollama",
            Pillar: "Local Runtimes",
            EngineKind: BackingEngineKinds.Local,
            DisplayName: "Ollama (Local)",
            AuthType: "local_loopback",
            Status: connected ? Statuses.Connected : Statuses.Unavailable,
            StatusDetail: discovery.Status == "available"
                ? $"{models.Length} local model(s) available"
                : discovery.Status == "no_models"
                    ? "Ollama is running with no local models installed"
                    : "Ollama is not running on 127.0.0.1:11434",
            KeyHint: null,
            DefaultModelId: models.FirstOrDefault(m => m.Id == "qwen3:4b")?.Id
                ?? models.FirstOrDefault(m => m.Id == "qwen3:1.7b")?.Id
                ?? models.FirstOrDefault()?.Id,
            Models: models,
            CanConfigureApiKey: false,
            CanProbe: true,
            IsPlanned: false);
    }

    private async Task<ConnectionEntryResponse> BuildCloudEntryAsync(
        string providerId,
        string displayName,
        bool runLiveProbe,
        CancellationToken ct)
    {
        var credStatus = await credentialStore.GetStatusAsync(providerId, ct);
        var models = CloudModelCatalog.GetModels(providerId)
            .Select(m => new ConnectionModelOption(m.Id, m.DisplayName, m.SupportsVision))
            .ToArray();

        var status = credStatus.HasKey ? Statuses.Connected : Statuses.NotConnected;
        var detail = credStatus.HasKey
            ? $"API key stored in Windows DPAPI ({credStatus.MaskedHint})"
            : "Add an API key (encrypted with Windows DPAPI)";

        if (credStatus.HasKey && runLiveProbe)
        {
            var (probeStatus, probeDetail) = await ProbeCloudKeyAsync(providerId, ct);
            status = probeStatus;
            detail = probeDetail;
        }

        return new ConnectionEntryResponse(
            Id: providerId,
            Pillar: "Cloud Model APIs",
            EngineKind: BackingEngineKinds.CloudApi,
            DisplayName: displayName,
            AuthType: "api_key_dpapi",
            Status: status,
            StatusDetail: detail,
            KeyHint: credStatus.MaskedHint,
            DefaultModelId: models.FirstOrDefault()?.Id,
            Models: models,
            CanConfigureApiKey: true,
            CanProbe: true,
            IsPlanned: false);
    }

    private async Task<(string Status, string Detail)> ProbeCloudKeyAsync(string providerId, CancellationToken ct)
    {
        var key = await credentialStore.GetApiKeyAsync(providerId, ct);
        if (string.IsNullOrWhiteSpace(key))
            return (Statuses.NotConnected, "API key is not configured.");

        try
        {
            using var request = providerId switch
            {
                "openai-api" => CreateBearerProbe("/v1/models", key),
                "gemini-api" => CreateHeaderProbe("/v1beta/models?pageSize=1", "x-goog-api-key", key),
                "anthropic-api" => CreateAnthropicProbe(key),
                _ => throw new InvalidOperationException()
            };

            using var response = await httpClients.CreateClient(providerId).SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
                return (Statuses.Connected, "API key verified against provider endpoint.");

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return (Statuses.NeedsSignIn, $"Provider rejected API key (HTTP {(int)response.StatusCode}).");

            return (Statuses.Unavailable, $"Provider returned HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (Statuses.Unavailable, "Could not reach provider endpoint.");
        }
    }

    private async Task<ConnectionEntryResponse> BuildCliEntryAsync(
        CliHarnessDescriptor descriptor,
        bool runLiveProbe,
        CancellationToken ct)
    {
        var executable = cliProcessRunner.ResolveExecutable(descriptor.ExecutableName);
        var models = BuildCliModels(descriptor);

        if (executable is null)
        {
            return new ConnectionEntryResponse(
                Id: descriptor.ProviderId,
                Pillar: "Local CLI Agent Harnesses",
                EngineKind: BackingEngineKinds.CliHarness,
                DisplayName: descriptor.DisplayName,
                AuthType: "cli_oauth",
                Status: Statuses.Unavailable,
                StatusDetail: $"'{descriptor.ExecutableName}' was not found on PATH.",
                KeyHint: null,
                DefaultModelId: descriptor.DefaultModelId,
                Models: models,
                CanConfigureApiKey: false,
                CanProbe: true,
                IsPlanned: false);
        }

        var hasLocalAuth = IsCliLocallyAuthenticated(descriptor.ProviderId);
        var status = hasLocalAuth ? Statuses.Connected : Statuses.NeedsSignIn;
        var detail = hasLocalAuth
            ? $"Installed at {Path.GetFileName(executable)}."
            : descriptor.SignInHint;

        if (runLiveProbe)
        {
            try
            {
                var env = environmentScrubber.BuildScrubbedEnvironment();
                var result = await cliProcessRunner.RunBufferedAsync(
                    executable,
                    descriptor.ProbeArgs,
                    Environment.CurrentDirectory,
                    env,
                    ct);
                var combined = $"{result.StdOut}\n{result.StdErr}";
                if (result.ExitCode != 0 ||
                    combined.Contains("not logged in", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("login required", StringComparison.OrdinalIgnoreCase) ||
                    !IsCliLocallyAuthenticated(descriptor.ProviderId, combined))
                {
                    status = Statuses.NeedsSignIn;
                    detail = descriptor.SignInHint;
                }
                else
                {
                    status = Statuses.Connected;
                    var firstLine = combined.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
                    detail = string.IsNullOrWhiteSpace(firstLine) ? "CLI ready" : firstLine;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                status = Statuses.Unavailable;
                detail = "CLI probe failed.";
            }
        }

        if (string.Equals(descriptor.ProviderId, "agy-cli", StringComparison.OrdinalIgnoreCase) &&
            status == Statuses.Connected)
        {
            models = await DiscoverAgyModelsAsync(executable, ct);
        }

        return new ConnectionEntryResponse(
            Id: descriptor.ProviderId,
            Pillar: "Local CLI Agent Harnesses",
            EngineKind: BackingEngineKinds.CliHarness,
            DisplayName: descriptor.DisplayName,
            AuthType: "cli_oauth",
            Status: status,
            StatusDetail: detail,
            KeyHint: null,
            DefaultModelId: descriptor.DefaultModelId,
            Models: models,
            CanConfigureApiKey: false,
            CanProbe: true,
            IsPlanned: false);
    }

    private static bool IsCliLocallyAuthenticated(string providerId, string? probeOutput = null)
    {
        if (!string.IsNullOrWhiteSpace(probeOutput) &&
            probeOutput.Contains("logged in", StringComparison.OrdinalIgnoreCase))
            return true;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home)) return false;

        return providerId.ToLowerInvariant() switch
        {
            "agy-cli" => HasAgyCliAuth(),
            "codex-cli" => File.Exists(Path.Combine(home, ".codex", "auth.json")),
            "claude-cli" => File.Exists(Path.Combine(home, ".claude", ".credentials.json")) ||
                            (File.Exists(Path.Combine(home, ".claude.json")) && FileContains(Path.Combine(home, ".claude.json"), "oauthAccount")),
            "cursor-cli" => File.Exists(Path.Combine(home, ".cursor", "cli-config.json")) ||
                            File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor", "User", "globalStorage", "state.vscdb")),
            _ => true
        };
    }

    private static bool FileContains(string path, string needle)
    {
        try { return File.ReadAllText(path).Contains(needle, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private async Task<ConnectionModelOption[]> DiscoverAgyModelsAsync(string executable, CancellationToken ct)
    {
        var fallback = new ConnectionModelOption("agy-default", "Antigravity (Default)", false);
        try
        {
            var env = environmentScrubber.BuildScrubbedEnvironment();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var result = await cliProcessRunner.RunBufferedAsync(
                executable,
                ["models"],
                Environment.CurrentDirectory,
                env,
                timeout.Token);

            if (result.ExitCode != 0)
                return [fallback];

            var parsed = new List<ConnectionModelOption> { fallback };
            foreach (var rawLine in result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var tab = rawLine.IndexOf('\t');
                if (tab <= 0) continue;
                var id = rawLine[..tab].Trim();
                var display = rawLine[(tab + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(display))
                    parsed.Add(new ConnectionModelOption(id, display, false));
            }
            return parsed.Count > 1 ? parsed.ToArray() : [fallback];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [fallback];
        }
    }

    private static bool HasAgyCliAuth()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home)) return false;
        var agyDir = Path.Combine(home, ".gemini", "antigravity-cli");
        return Directory.Exists(agyDir) && Directory.EnumerateFiles(agyDir).Any();
    }

    private static ConnectionModelOption[] BuildCliModels(CliHarnessDescriptor descriptor) =>
        descriptor.ProviderId.ToLowerInvariant() switch
        {
            "agy-cli" =>
            [
                new("agy-default", "Antigravity (Default)", false)
            ],
            "codex-cli" =>
            [
                new("codex-default", "Codex (Default)", false),
                new("gpt-5-codex", "GPT-5 Codex", false),
                new("o4-mini", "o4-mini", false)
            ],
            "claude-cli" =>
            [
                new("claude-sonnet-4", "Claude Sonnet 4", false),
                new("claude-opus-4", "Claude Opus 4", false),
                new("claude-3-5-haiku", "Claude 3.5 Haiku", false)
            ],
            _ => [new(descriptor.DefaultModelId, $"{descriptor.DisplayName} (Default)", false)]
        };

    private async Task<ConnectionEntryResponse> BuildLocalMcpEntryAsync(
        string serverId,
        string displayName,
        bool runLiveProbe,
        CancellationToken ct)
    {
        var state = runLiveProbe && mcpRegistry is not null
            ? await mcpRegistry.ProbeServerAsync(serverId, ct)
            : mcpHealth?.Get(serverId);

        var status = state?.Status switch
        {
            McpHealthStatuses.Failed => Statuses.Unavailable,
            McpHealthStatuses.Disconnected => Statuses.Unavailable,
            _ => Statuses.Connected,
        };

        var detail = state?.LastError
            ?? (serverId == "filesystem-mcp"
                ? "Built-in Tier 1 read-only workspace inspector"
                : "Built-in Tier 1 read-only Git inspector");

        return new ConnectionEntryResponse(
            Id: serverId,
            Pillar: "MCP & Service Integrations",
            EngineKind: "mcp",
            DisplayName: displayName,
            AuthType: "local_workspace",
            Status: status,
            StatusDetail: detail,
            KeyHint: null,
            DefaultModelId: null,
            Models: [],
            CanConfigureApiKey: false,
            CanProbe: true,
            IsPlanned: false);
    }

    private async Task<ConnectionEntryResponse> BuildCredentialMcpEntryAsync(
        string serverId,
        string displayName,
        string authDescription,
        bool runLiveProbe,
        CancellationToken ct)
    {
        var cred = await credentialStore.GetStatusAsync(serverId, ct);
        var status = cred.HasKey ? Statuses.Connected : Statuses.NotConnected;
        var detail = cred.HasKey
            ? $"Token stored in Windows DPAPI ({cred.MaskedHint})"
            : $"Add {authDescription}";

        if (cred.HasKey && runLiveProbe && mcpRegistry is not null)
        {
            var probe = await mcpRegistry.ProbeServerAsync(serverId, ct);
            if (probe is not null)
            {
                if (probe.Status == McpHealthStatuses.Connected)
                {
                    status = Statuses.Connected;
                    detail = probe.LastError ?? $"Verified via {displayName} ({cred.MaskedHint})";
                }
                else
                {
                    status = Statuses.Unavailable;
                    detail = probe.LastError ?? $"Could not reach {displayName}";
                }
            }
        }

        return new ConnectionEntryResponse(
            Id: serverId,
            Pillar: "MCP & Service Integrations",
            EngineKind: "mcp",
            DisplayName: displayName,
            AuthType: "dpapi_token",
            Status: status,
            StatusDetail: detail,
            KeyHint: cred.MaskedHint,
            DefaultModelId: null,
            Models: [],
            CanConfigureApiKey: true,
            CanProbe: true,
            IsPlanned: false);
    }

    private async Task<ConnectionEntryResponse> BuildSqlMcpEntryAsync(bool runLiveProbe, CancellationToken ct)
    {
        var status = Statuses.NotConnected;
        var detail = "Configure a read-only SQL Server profile (Windows Integrated or DPAPI login)";

        if (runLiveProbe && mcpRegistry is not null)
        {
            var probe = await mcpRegistry.ProbeServerAsync("sql-server-mcp", ct);
            if (probe is not null)
            {
                if (probe.Status == McpHealthStatuses.Connected)
                {
                    status = Statuses.Connected;
                    detail = probe.LastError ?? "Read-only SQL Server MCP connected";
                }
                else if (!string.IsNullOrWhiteSpace(probe.LastError) &&
                         probe.LastError.Contains("No read-only SQL Server profiles configured", StringComparison.OrdinalIgnoreCase))
                {
                    status = Statuses.NotConnected;
                    detail = probe.LastError;
                }
                else
                {
                    status = Statuses.Unavailable;
                    detail = probe.LastError ?? "SQL Server MCP probe failed";
                }
            }
        }
        else if (mcpHealth is not null)
        {
            var existing = mcpHealth.Get("sql-server-mcp");
            if (existing?.Status == McpHealthStatuses.Connected)
            {
                status = Statuses.Connected;
                detail = "Read-only SQL Server MCP ready";
            }
        }

        return new ConnectionEntryResponse(
            Id: "sql-server-mcp",
            Pillar: "MCP & Service Integrations",
            EngineKind: "mcp",
            DisplayName: "SQL Server MCP (Read-Only)",
            AuthType: "sql_readonly",
            Status: status,
            StatusDetail: detail,
            KeyHint: null,
            DefaultModelId: null,
            Models: [],
            CanConfigureApiKey: false,
            CanProbe: true,
            IsPlanned: false);
    }

    private static ConnectionEntryResponse BuildPlannedEntry(string id, string displayName, string detail) =>
        new(
            Id: id,
            Pillar: "MCP & Service Integrations",
            EngineKind: "mcp_planned",
            DisplayName: displayName,
            AuthType: "mcp_oauth",
            Status: Statuses.Planned,
            StatusDetail: detail,
            KeyHint: null,
            DefaultModelId: null,
            Models: [],
            CanConfigureApiKey: false,
            CanProbe: false,
            IsPlanned: true);

    private static HttpRequestMessage CreateBearerProbe(string path, string key)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return req;
    }

    private static HttpRequestMessage CreateHeaderProbe(string path, string headerName, string key)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Add(headerName, key);
        return req;
    }

    private static HttpRequestMessage CreateAnthropicProbe(string key)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/v1/models");
        req.Headers.Add("x-api-key", key);
        req.Headers.Add("anthropic-version", "2023-06-01");
        return req;
    }
}
