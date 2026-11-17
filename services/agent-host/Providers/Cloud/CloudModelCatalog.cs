namespace Guildhall.AgentHost.Providers.Cloud;

public sealed record CloudModelDescriptor(string Id, string DisplayName, bool SupportsVision);

public static class CloudModelCatalog
{
    public static IReadOnlyList<CloudModelDescriptor> GetModels(string providerId) => providerId.ToLowerInvariant() switch
    {
        "openai-api" =>
        [
            new("gpt-4.1", "GPT-4.1", true),
            new("gpt-4.1-mini", "GPT-4.1 mini", true),
            new("o4-mini", "o4-mini", true)
        ],
        "gemini-api" =>
        [
            new("gemini-2.5-pro", "Gemini 2.5 Pro", true),
            new("gemini-2.5-flash", "Gemini 2.5 Flash", true)
        ],
        "anthropic-api" =>
        [
            new("claude-sonnet-4-20250514", "Claude Sonnet 4", true),
            new("claude-3-7-sonnet-20250219", "Claude 3.7 Sonnet", true),
            new("claude-3-5-haiku-20241022", "Claude 3.5 Haiku", true)
        ],
        _ => []
    };
}
