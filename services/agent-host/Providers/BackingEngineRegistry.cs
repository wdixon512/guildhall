namespace Guildhall.AgentHost.Providers;

public sealed class BackingEngineRegistry(IEnumerable<IBackingEngine> engines) : IBackingEngineRegistry
{
    private readonly Dictionary<string, IBackingEngine> _engines = engines
        .ToDictionary(x => x.ProviderId, StringComparer.OrdinalIgnoreCase);

    public IBackingEngine? Resolve(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return _engines.GetValueOrDefault("ollama");

        return _engines.GetValueOrDefault(providerId.Trim());
    }

    public IReadOnlyList<IBackingEngine> GetAll() => _engines.Values.ToList();
}
