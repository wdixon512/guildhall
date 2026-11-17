using System.Collections;

namespace Guildhall.AgentHost.Configuration;

/// <summary>
/// Builds scrubbed environment variable dictionaries for child CLI harness processes,
/// removing Guildhall host tokens, SQL connection strings, and unrelated API keys.
/// </summary>
public sealed class EnvironmentScrubber
{
    private static readonly string[] BlockedPrefixes =
    [
        "GUILDHALL_",
        "ConnectionStrings__",
        "ASPNETCORE_",
        "DOTNET_"
    ];

    private static readonly string[] BlockedSubstrings =
    [
        "API_KEY",
        "SECRET",
        "TOKEN",
        "PASSWORD",
        "CREDENTIAL"
    ];

    public Dictionary<string, string?> BuildScrubbedEnvironment(IDictionary? sourceEnvironment = null)
    {
        var source = sourceEnvironment ?? Environment.GetEnvironmentVariables();
        var clean = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (DictionaryEntry entry in source)
        {
            if (entry.Key is not string key || string.IsNullOrWhiteSpace(key))
                continue;

            if (IsSensitiveKey(key))
                continue;

            clean[key] = entry.Value?.ToString();
        }

        return clean;
    }

    public static bool IsSensitiveKey(string key)
    {
        foreach (var prefix in BlockedPrefixes)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var token in BlockedSubstrings)
        {
            if (key.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
