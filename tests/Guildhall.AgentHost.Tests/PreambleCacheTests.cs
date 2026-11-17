using System.Net;
using System.Net.Http.Json;
using System.Text;
using Guildhall.AgentHost.Api;
using Guildhall.AgentHost.Chat;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Guildhall.AgentHost.Tests;

public sealed class PreambleCacheTests
{
    [Fact]
    public void Loads_Version_Controlled_Preambles_And_Parses_Frontmatter()
    {
        var tempDir = CreateTempPreamblesDir();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "global-guidelines.md"), "# Global\nStay grounded.");
            Directory.CreateDirectory(Path.Combine(tempDir, "members"));
            File.WriteAllText(Path.Combine(tempDir, "members", "maren.md"), """
                ---
                name: Maren
                role: Steward
                reasoning_effort: balanced
                ---
                You are Maren.
                """);

            using var service = CreateService(tempDir, enableWatcher: false);
            var global = service.GetGlobalGuidelines();
            var maren = service.GetMemberPreamble("Maren");

            Assert.NotNull(global);
            Assert.Contains("Stay grounded.", global.Body, StringComparison.Ordinal);
            Assert.NotNull(maren);
            Assert.Equal("You are Maren.", maren.Body);
            Assert.Equal("Steward", maren.Metadata["role"]);
            Assert.Equal("balanced", maren.Metadata["reasoning_effort"]);
            Assert.Equal(2, service.Count);
            Assert.True(service.CurrentCacheBytes > 0);
            Assert.Equal(PreambleCacheOptions.DefaultCapBytes, service.MaxCacheBytes);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Clamps_MaxCacheBytes_To_50_To_100_MB_Range()
    {
        var tempDir = CreateTempPreamblesDir();
        try
        {
            using var low = CreateService(tempDir, maxBytes: 1024, enableWatcher: false);
            Assert.Equal(PreambleCacheOptions.MinCapBytes, low.MaxCacheBytes);

            using var high = CreateService(tempDir, maxBytes: 500L * 1024 * 1024, enableWatcher: false);
            Assert.Equal(PreambleCacheOptions.MaxCapBytes, high.MaxCacheBytes);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Enforces_Hard_Cap_By_Evicting_Oldest_Entries()
    {
        var tempDir = CreateTempPreamblesDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "members"));
            // Each entry counts key + raw + body + metadata (~2x raw size).
            // Two ~15 MB files => ~30 MB each => ~60 MB total, exceeding 50 MB cap.
            var payloadA = new string('A', 15 * 1024 * 1024);
            var payloadB = new string('B', 15 * 1024 * 1024);
            File.WriteAllText(Path.Combine(tempDir, "members", "alpha.md"), payloadA);
            var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
            using var service = CreateService(tempDir, maxBytes: PreambleCacheOptions.MinCapBytes, enableWatcher: false, timeProvider: time);
            Assert.NotNull(service.GetMemberPreamble("alpha"));

            time.Advance(TimeSpan.FromMinutes(1));
            File.WriteAllText(Path.Combine(tempDir, "members", "beta.md"), payloadB);
            service.ReloadAll();

            Assert.True(service.CurrentCacheBytes <= PreambleCacheOptions.MinCapBytes);
            Assert.NotNull(service.GetMemberPreamble("beta"));
            Assert.Null(service.GetMemberPreamble("alpha"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task FileSystemWatcher_HotReloads_Changed_Created_And_Deleted_Preambles()
    {
        var tempDir = CreateTempPreamblesDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "members"));
            var marenPath = Path.Combine(tempDir, "members", "maren.md");
            await File.WriteAllTextAsync(marenPath, "Initial instructions.");

            using var service = CreateService(tempDir, enableWatcher: true);
            Assert.Equal("Initial instructions.", service.GetMemberPreamble("maren")?.Body);

            await File.WriteAllTextAsync(marenPath, "Updated instructions v2.");
            await WaitUntilAsync(() => service.GetMemberPreamble("maren")?.Body == "Updated instructions v2.");
            Assert.Equal("Updated instructions v2.", service.GetMemberPreamble("maren")?.Body);

            var scholarPath = Path.Combine(tempDir, "members", "scholar.md");
            await File.WriteAllTextAsync(scholarPath, "Scholar instructions.");
            await WaitUntilAsync(() => service.GetMemberPreamble("The Scholar")?.Body == "Scholar instructions.");

            File.Delete(scholarPath);
            await WaitUntilAsync(() => service.GetMemberPreamble("The Scholar") is null);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Preambles_Endpoint_Returns_Memoized_Preambles_With_Auth()
    {
        const string token = "0123456789abcdef0123456789abcdef";
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GUILDHALL_SESSION_TOKEN"] = token,
                    ["GUILDHALL_HOST_URL"] = "http://127.0.0.1:43120",
                    ["GUILDHALL_OLLAMA_URL"] = "http://127.0.0.1:11434"
                });
            });
        });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = "127.0.0.1:43120";
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/preambles");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PreambleCacheSnapshotResponse>();
        Assert.NotNull(payload);
        Assert.True(payload.MaxCacheBytes >= PreambleCacheOptions.MinCapBytes);
        Assert.True(payload.MaxCacheBytes <= PreambleCacheOptions.MaxCapBytes);
        Assert.Contains(payload.Entries, x => x.Key == "global-guidelines");
        Assert.Contains(payload.Entries, x => x.Key == "members/maren");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(40);
        }
        Assert.True(condition(), "Timed out waiting for FileSystemWatcher hot-reload.");
    }

    private static string CreateTempPreamblesDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "guildhall-preambles-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static PreambleCacheService CreateService(
        string directory,
        long maxBytes = PreambleCacheOptions.DefaultCapBytes,
        bool enableWatcher = false,
        TimeProvider? timeProvider = null)
    {
        var options = new StaticOptionsMonitor<PreambleCacheOptions>(new PreambleCacheOptions
        {
            DirectoryPath = directory,
            MaxCacheBytes = maxBytes,
            EnableFileWatcher = enableWatcher
        });
        return new PreambleCacheService(
            options,
            timeProvider ?? TimeProvider.System,
            new StubHostEnvironment(directory),
            NullLogger<PreambleCacheService>.Instance);
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;
        public T Get(string? name) => currentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class StubHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Guildhall.AgentHost.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
