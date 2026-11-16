using Guildhall.AgentHost.Api;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Persistence;
using Guildhall.AgentHost.Providers.Ollama;
using Guildhall.AgentHost.Chat;
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
builder.Services.AddHttpClient<OllamaDiscovery>((services, http) => {
    http.BaseAddress = LocalConfiguration.LoopbackUri(services.GetRequiredService<IConfiguration>()["GUILDHALL_OLLAMA_URL"] ?? "http://127.0.0.1:11434");
    http.Timeout = Timeout.InfiniteTimeSpan;
})
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false });
builder.Services.AddHttpClient("ollama-chat", (services, http) => { http.BaseAddress = LocalConfiguration.LoopbackUri(services.GetRequiredService<IConfiguration>()["GUILDHALL_OLLAMA_URL"] ?? "http://127.0.0.1:11434"); http.Timeout = TimeSpan.FromMinutes(5); }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false });
builder.Services.AddSingleton<ChatRunner>();
builder.Services.AddHostedService(services => services.GetRequiredService<ChatRunner>());
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
app.MapChat();
app.Run();

public partial class Program;
