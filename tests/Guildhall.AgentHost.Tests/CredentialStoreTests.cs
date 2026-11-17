using System.Collections;
using Guildhall.AgentHost.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Guildhall.AgentHost.Tests;

public sealed class CredentialStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "guildhall-cred-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task DpapiRoundTripEncryptsOnDiskAndMasksHint()
    {
        if (!OperatingSystem.IsWindows()) return;

        var store = new DpapiCredentialStore(NullLogger<DpapiCredentialStore>.Instance, _tempDir);
        const string secret = "sk-test-1234567890abcdef";

        await store.SetApiKeyAsync("openai", secret);

        var filePath = Path.Combine(_tempDir, "openai.dpapi");
        Assert.True(File.Exists(filePath));
        var rawDiskText = await File.ReadAllTextAsync(filePath);
        Assert.DoesNotContain("1234567890", rawDiskText);

        var loaded = await store.GetApiKeyAsync("openai");
        Assert.Equal(secret, loaded);

        var status = await store.GetStatusAsync("openai");
        Assert.True(status.HasKey);
        Assert.Equal("••••cdef", status.MaskedHint);

        await store.DeleteApiKeyAsync("openai");
        Assert.Null(await store.GetApiKeyAsync("openai"));
        Assert.False((await store.GetStatusAsync("openai")).HasKey);
    }

    [Fact]
    public async Task CorruptDpapiFileFailsClosed()
    {
        if (!OperatingSystem.IsWindows()) return;

        Directory.CreateDirectory(_tempDir);
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "gemini.dpapi"), [1, 2, 3, 4, 5]);
        var store = new DpapiCredentialStore(NullLogger<DpapiCredentialStore>.Instance, _tempDir);

        Assert.Null(await store.GetApiKeyAsync("gemini"));
        Assert.False((await store.GetStatusAsync("gemini")).HasKey);
    }

    [Fact]
    public void EnvironmentScrubberStripsHostSecretsAndApiKeys()
    {
        var scrubber = new EnvironmentScrubber();
        var source = new Hashtable
        {
            ["PATH"] = @"C:\Windows\System32",
            ["USERPROFILE"] = @"C:\Users\tester",
            ["GUILDHALL_SESSION_TOKEN"] = "super-secret-token",
            ["ConnectionStrings__Guildhall"] = "Server=localhost;Password=pw;",
            ["OPENAI_API_KEY"] = "sk-live-secret",
            ["GEMINI_API_KEY"] = "AIza-secret",
            ["ANTHROPIC_API_KEY"] = "sk-ant-secret",
            ["DOTNET_ROOT"] = @"C:\Program Files\dotnet"
        };

        var scrubbed = scrubber.BuildScrubbedEnvironment(source);

        Assert.True(scrubbed.ContainsKey("PATH"));
        Assert.True(scrubbed.ContainsKey("USERPROFILE"));
        Assert.False(scrubbed.ContainsKey("GUILDHALL_SESSION_TOKEN"));
        Assert.False(scrubbed.ContainsKey("ConnectionStrings__Guildhall"));
        Assert.False(scrubbed.ContainsKey("OPENAI_API_KEY"));
        Assert.False(scrubbed.ContainsKey("GEMINI_API_KEY"));
        Assert.False(scrubbed.ContainsKey("ANTHROPIC_API_KEY"));
        Assert.False(scrubbed.ContainsKey("DOTNET_ROOT"));
    }
}
