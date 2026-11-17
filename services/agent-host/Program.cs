using Guildhall.AgentHost.Api;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Health;
using Guildhall.AgentHost.Mcp;
using Guildhall.AgentHost.Mcp.Providers;
using Guildhall.AgentHost.Persistence;
using Guildhall.AgentHost.Providers;
using Guildhall.AgentHost.Providers.Cli;
using Guildhall.AgentHost.Providers.Cloud;
using Guildhall.AgentHost.Providers.Ollama;
using Guildhall.AgentHost.Chat;
using Guildhall.AgentHost.Scheduling;
using Guildhall.AgentHost.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddTransient(typeof(Lazy<>), typeof(Lazier<>));
builder.Services.AddOptions<PreambleCacheOptions>()
    .BindConfiguration("PreambleCache")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IPreambleCacheService, PreambleCacheService>();
builder.Services.AddOptions<CrossTurnBudgetOptions>()
    .BindConfiguration("CrossTurnBudget")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IContextBuilder, ContextBuilder>();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 54 * 1024 * 1024);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 53 * 1024 * 1024);
// Avoid framework request/SQL logging accidentally recording sensitive configuration.
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var allowedOrigins = LocalConfiguration.AllowedOrigins(builder.Configuration["GUILDHALL_FRONTEND_URL"]);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins).WithMethods("GET", "POST", "PATCH", "PUT", "DELETE").WithHeaders("Authorization", "Content-Type")));
builder.Services.AddDbContextFactory<GuildhallDbContext>((services, options) =>
    options.UseSqlServer(LocalConfiguration.RuntimeConnection(services.GetRequiredService<IConfiguration>().GetConnectionString("Guildhall")), sql => sql.CommandTimeout(10)));
builder.Services.AddSingleton<IDatabaseReadiness>(services => new DatabaseReadiness(LocalConfiguration.RuntimeConnection(
    services.GetRequiredService<IConfiguration>().GetConnectionString("Guildhall"))));
static IHttpClientBuilder ConfigureHardenedHandler(IHttpClientBuilder httpBuilder) =>
    httpBuilder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false });

ConfigureHardenedHandler(builder.Services.AddHttpClient<OllamaDiscovery>((services, http) =>
{
    http.BaseAddress = LocalConfiguration.LoopbackUri(services.GetRequiredService<IConfiguration>()["GUILDHALL_OLLAMA_URL"] ?? "http://127.0.0.1:11434");
    http.Timeout = Timeout.InfiniteTimeSpan;
}));
ConfigureHardenedHandler(builder.Services.AddHttpClient("ollama-chat", (services, http) => { http.BaseAddress = LocalConfiguration.LoopbackUri(services.GetRequiredService<IConfiguration>()["GUILDHALL_OLLAMA_URL"] ?? "http://127.0.0.1:11434"); http.Timeout = TimeSpan.FromMinutes(5); }));
ConfigureHardenedHandler(builder.Services.AddHttpClient("openai-api", http => { http.BaseAddress = new Uri("https://api.openai.com"); http.Timeout = TimeSpan.FromMinutes(5); }));
ConfigureHardenedHandler(builder.Services.AddHttpClient("gemini-api", http => { http.BaseAddress = new Uri("https://generativelanguage.googleapis.com"); http.Timeout = TimeSpan.FromMinutes(5); }));
ConfigureHardenedHandler(builder.Services.AddHttpClient("anthropic-api", http => { http.BaseAddress = new Uri("https://api.anthropic.com"); http.Timeout = TimeSpan.FromMinutes(5); }));
ConfigureHardenedHandler(builder.Services.AddHttpClient("mcp-http", http => { http.Timeout = TimeSpan.FromSeconds(15); }));
ConfigureHardenedHandler(builder.Services.AddHttpClient("github-api", http => { http.BaseAddress = new Uri("https://api.github.com"); http.Timeout = TimeSpan.FromSeconds(15); }));
ConfigureHardenedHandler(builder.Services.AddHttpClient("google-drive-api", http => { http.BaseAddress = new Uri("https://www.googleapis.com"); http.Timeout = TimeSpan.FromSeconds(15); }));
ConfigureHardenedHandler(builder.Services.AddHttpClient("gmail-api", http => { http.BaseAddress = new Uri("https://gmail.googleapis.com"); http.Timeout = TimeSpan.FromSeconds(15); }));
builder.Services.AddSingleton<ICredentialStore, DpapiCredentialStore>();
builder.Services.AddSingleton<EnvironmentScrubber>();
builder.Services.AddSingleton<WorkspacePathGuard>();
builder.Services.AddSingleton<ExfiltrationGuard>();
builder.Services.AddSingleton<PrivateNetworkGuard>();
builder.Services.AddSingleton<ToolPermissionGate>();
builder.Services.AddSingleton<ICliProcessRunner, CliProcessRunner>();
builder.Services.AddSingleton<McpHealthSupervisor>();
builder.Services.AddSingleton<PrivateNetworkProbeService>();
builder.Services.AddSingleton<IMcpJsonRpcClient, McpJsonRpcClient>();
builder.Services.AddSingleton<SqlReadOnlyGuard>();
builder.Services.AddSingleton<IMcpToolProvider, LocalFilesystemMcpProvider>();
builder.Services.AddSingleton<IMcpToolProvider, LocalGitMcpProvider>();
builder.Services.AddSingleton<IMcpToolProvider, LocalCommandMcpProvider>();
builder.Services.AddSingleton<IMcpToolProvider, GitHubMcpProvider>();
builder.Services.AddSingleton<IMcpToolProvider, GoogleDriveMcpProvider>();
builder.Services.AddSingleton<IMcpToolProvider, GmailMcpProvider>();
builder.Services.AddSingleton<IMcpToolProvider, SqlServerMcpProvider>();
builder.Services.AddSingleton<McpRegistry>();
builder.Services.AddSingleton<Guildhall.AgentHost.Workspaces.ProjectWorkspaceService>();
builder.Services.AddSingleton<Guildhall.AgentHost.Workspaces.ToolRollbackService>();
builder.Services.AddSingleton<IBackingEngine, OllamaBackingEngine>();
builder.Services.AddSingleton<IBackingEngine, OpenAiCloudEngine>();
builder.Services.AddSingleton<IBackingEngine, GeminiCloudEngine>();
builder.Services.AddSingleton<IBackingEngine, AnthropicCloudEngine>();
builder.Services.AddSingleton<IBackingEngine, AgyCliEngine>();
builder.Services.AddSingleton<IBackingEngine, CodexCliEngine>();
builder.Services.AddSingleton<IBackingEngine, ClaudeCliEngine>();
builder.Services.AddSingleton<IBackingEngine, CursorCliEngine>();
builder.Services.AddSingleton<IBackingEngine, GeminiCliEngine>();
builder.Services.AddSingleton<IBackingEngineRegistry, BackingEngineRegistry>();
builder.Services.AddSingleton<ConnectionCatalogService>();
builder.Services.AddSingleton<ChatRecoveryService>();
builder.Services.AddSingleton<ChatRunner>();
builder.Services.AddHostedService(services => services.GetRequiredService<ChatRunner>());
builder.Services.AddSingleton<DurableTaskSchedulerService>();
builder.Services.AddHostedService(services => services.GetRequiredService<DurableTaskSchedulerService>());
builder.Services.AddSingleton<FiveTierHealthSupervisor>();
builder.Services.AddHostedService(services => services.GetRequiredService<FiveTierHealthSupervisor>());
var app = builder.Build();
var token = app.Configuration["GUILDHALL_SESSION_TOKEN"];
if (string.IsNullOrWhiteSpace(token) || token.Length < 32 || token.Length > 128 || token.Any(char.IsWhiteSpace))
    throw new InvalidOperationException("A session token of 32–128 non-whitespace characters is required.");
var hostUri = LocalConfiguration.LoopbackUri(app.Configuration["GUILDHALL_HOST_URL"] ?? "http://127.0.0.1:43120");
_ = LocalConfiguration.LoopbackUri(app.Configuration["GUILDHALL_OLLAMA_URL"] ?? "http://127.0.0.1:11434");
_ = LocalConfiguration.RuntimeConnection(app.Configuration.GetConnectionString("Guildhall"));
app.Urls.Clear();
app.Urls.Add(hostUri.AbsoluteUri);
app.UseLocalBoundary(hostUri, token);
app.MapGet("/health", () => new HealthResponse());
app.MapGet("/ready", async (IDatabaseReadiness readiness, CancellationToken cancellationToken) =>
{
    var result = await readiness.CheckAsync(cancellationToken);
    return Results.Json(result, statusCode: result.Status == "ready" ? 200 : 503);
});
app.MapGet("/models", (OllamaDiscovery discovery, CancellationToken cancellationToken) => discovery.DiscoverAsync(cancellationToken));
app.MapConnections();
app.MapMcpEndpoints();
app.MapSqlMcpEndpoints();
app.MapMcpGatewayEndpoints();
app.MapProjectEndpoints();
app.MapHealthEndpoints();
app.MapScheduleEndpoints();
app.MapChat();
app.Run();

public partial class Program;
