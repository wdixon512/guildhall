using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Guildhall.AgentHost.Chat;

public sealed class PreambleCacheOptions
{
    public const long MinCapBytes = 50L * 1024 * 1024;
    public const long MaxCapBytes = 100L * 1024 * 1024;
    public const long DefaultCapBytes = 64L * 1024 * 1024;

    public string? DirectoryPath { get; set; }

    [Range(MinCapBytes, MaxCapBytes)]
    public long MaxCacheBytes { get; set; } = DefaultCapBytes;

    public bool EnableFileWatcher { get; set; } = true;
}

public sealed record PreambleEntry(
    string Key,
    string RawMarkdown,
    string Body,
    IReadOnlyDictionary<string, string> Metadata,
    long ByteCount,
    string ContentHash,
    DateTimeOffset LoadedUtc);

public interface IPreambleCacheService
{
    PreambleEntry? Get(string key);
    PreambleEntry? GetGlobalGuidelines();
    PreambleEntry? GetMemberPreamble(string slugOrName);
    IReadOnlyCollection<PreambleEntry> ListAll();
    long CurrentCacheBytes { get; }
    long MaxCacheBytes { get; }
    int Count { get; }
    void ReloadAll();
}

public sealed class PreambleCacheService : IPreambleCacheService, IDisposable
{
    private readonly IOptionsMonitor<PreambleCacheOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PreambleCacheService> _logger;
    private readonly string _rootDirectory;
    private readonly ConcurrentDictionary<string, PreambleEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _capLock = new();
    private readonly FileSystemWatcher? _watcher;
    private long _currentBytes;
    private bool _disposed;

    public PreambleCacheService(
        IOptionsMonitor<PreambleCacheOptions> options,
        TimeProvider timeProvider,
        IHostEnvironment environment,
        ILogger<PreambleCacheService> logger)
    {
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        _rootDirectory = ResolveDirectory(options.CurrentValue.DirectoryPath, environment.ContentRootPath);

        ReloadAll();

        if (options.CurrentValue.EnableFileWatcher && Directory.Exists(_rootDirectory))
        {
            _watcher = new FileSystemWatcher(_rootDirectory, "*.md")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            _watcher.Created += OnFileChanged;
            _watcher.Changed += OnFileChanged;
            _watcher.Deleted += OnFileDeleted;
            _watcher.Renamed += OnFileRenamed;
            _watcher.Error += OnWatcherError;
            _watcher.EnableRaisingEvents = true;
        }
    }

    public long CurrentCacheBytes => Interlocked.Read(ref _currentBytes);

    public long MaxCacheBytes => Math.Clamp(
        _options.CurrentValue.MaxCacheBytes,
        PreambleCacheOptions.MinCapBytes,
        PreambleCacheOptions.MaxCapBytes);

    public int Count => _cache.Count;

    public PreambleEntry? Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        return _cache.TryGetValue(NormalizeKey(key), out var entry) ? entry : null;
    }

    public PreambleEntry? GetGlobalGuidelines() => Get("global-guidelines");

    public PreambleEntry? GetMemberPreamble(string slugOrName)
    {
        if (string.IsNullOrWhiteSpace(slugOrName)) return null;
        var slug = ToSlug(slugOrName);
        if (string.IsNullOrEmpty(slug)) return null;
        return Get($"members/{slug}");
    }

    public IReadOnlyCollection<PreambleEntry> ListAll() =>
        _cache.Values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToArray();

    public void ReloadAll()
    {
        if (_disposed) return;

        lock (_capLock)
        {
            _cache.Clear();
            Interlocked.Exchange(ref _currentBytes, 0);

            if (!Directory.Exists(_rootDirectory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(_rootDirectory, "*.md", SearchOption.AllDirectories)
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                TryLoadFileCore(file);
            }
        }
    }

    public static (IReadOnlyDictionary<string, string> Metadata, string Body) ParseMarkdownWithFrontmatter(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return (new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), string.Empty);
        }

        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return (new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), markdown.Trim());
        }

        var closingIndex = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        var closingAtEnd = false;
        if (closingIndex < 0)
        {
            if (normalized.EndsWith("\n---", StringComparison.Ordinal))
            {
                closingIndex = normalized.Length - 4;
                closingAtEnd = true;
            }
            else
            {
                return (new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), markdown.Trim());
            }
        }

        var frontmatterBlock = normalized[4..closingIndex];
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in frontmatterBlock.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim().Trim('"', '\'');
            if (!string.IsNullOrEmpty(key))
            {
                metadata[key] = value;
            }
        }

        var bodyStart = closingAtEnd ? normalized.Length : closingIndex + 5;
        var body = bodyStart < normalized.Length ? normalized[bodyStart..].Trim() : string.Empty;
        return (metadata, body);
    }

    public static string ToSlug(string value)
    {
        var trimmed = value.Trim().ToLowerInvariant();
        if (trimmed.StartsWith("the ", StringComparison.Ordinal))
        {
            trimmed = trimmed[4..].Trim();
        }
        var builder = new StringBuilder(trimmed.Length);
        var lastDash = false;
        foreach (var ch in trimmed)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastDash = false;
            }
            else if (!lastDash && builder.Length > 0)
            {
                builder.Append('-');
                lastDash = true;
            }
        }
        return builder.ToString().Trim('-');
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_disposed || !e.FullPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return;
        TryLoadFileWithRetry(e.FullPath);
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (_disposed) return;
        var key = ToRelativeKey(e.FullPath);
        if (key is null) return;

        lock (_capLock)
        {
            if (_cache.TryRemove(key, out var removed))
            {
                Interlocked.Add(ref _currentBytes, -removed.ByteCount);
            }
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (_disposed) return;
        var oldKey = ToRelativeKey(e.OldFullPath);
        lock (_capLock)
        {
            if (oldKey is not null && _cache.TryRemove(oldKey, out var removed))
            {
                Interlocked.Add(ref _currentBytes, -removed.ByteCount);
            }

            if (e.FullPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && File.Exists(e.FullPath))
            {
                TryLoadFileCore(e.FullPath);
            }
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogWarning(e.GetException(), "Preamble FileSystemWatcher reported an error; reloading cache.");
        ReloadAll();
    }

    private void TryLoadFileWithRetry(string fullPath)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                lock (_capLock)
                {
                    if (_disposed || !File.Exists(fullPath)) return;
                    TryLoadFileCore(fullPath);
                    return;
                }
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(25 * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < 3)
            {
                Thread.Sleep(25 * (attempt + 1));
            }
        }
    }

    private void TryLoadFileCore(string fullPath)
    {
        var key = ToRelativeKey(fullPath);
        if (key is null) return;

        string rawMarkdown;
        using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            rawMarkdown = reader.ReadToEnd();
        }

        var (metadata, body) = ParseMarkdownWithFrontmatter(rawMarkdown);
        var byteCount = EstimateEntryBytes(key, rawMarkdown, body, metadata);
        var cap = MaxCacheBytes;
        if (byteCount > cap)
        {
            _logger.LogWarning("Preamble '{Key}' ({Bytes} bytes) exceeds cache cap ({Cap} bytes) and was skipped.", key, byteCount, cap);
            return;
        }

        var existingBytes = _cache.TryGetValue(key, out var existing) ? existing.ByteCount : 0L;
        var projectedBytes = Interlocked.Read(ref _currentBytes) - existingBytes + byteCount;
        if (projectedBytes > cap)
        {
            EvictToFit(projectedBytes - cap, protectKey: key);
        }

        if (Interlocked.Read(ref _currentBytes) - existingBytes + byteCount > cap)
        {
            _logger.LogWarning("Preamble cache cap ({Cap} bytes) reached; '{Key}' could not be memoized.", cap, key);
            return;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawMarkdown))).ToLowerInvariant();
        var entry = new PreambleEntry(key, rawMarkdown, body, metadata, byteCount, hash, _timeProvider.GetUtcNow());

        _cache[key] = entry;
        Interlocked.Add(ref _currentBytes, byteCount - existingBytes);
    }

    private void EvictToFit(long bytesToFree, string protectKey)
    {
        var candidates = _cache.Values
            .Where(x => !string.Equals(x.Key, protectKey, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(x.Key, "global-guidelines", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.LoadedUtc)
            .ToArray();

        long freed = 0;
        foreach (var candidate in candidates)
        {
            if (freed >= bytesToFree) break;
            if (_cache.TryRemove(candidate.Key, out var removed))
            {
                Interlocked.Add(ref _currentBytes, -removed.ByteCount);
                freed += removed.ByteCount;
            }
        }
    }

    private string? ToRelativeKey(string fullPath)
    {
        var relative = Path.GetRelativePath(_rootDirectory, fullPath);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)) return null;
        return NormalizeKey(relative);
    }

    private static string NormalizeKey(string key)
    {
        var normalized = key.Replace('\\', '/').Trim().TrimStart('/');
        if (normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^3];
        }
        return normalized;
    }

    private static long EstimateEntryBytes(string key, string rawMarkdown, string body, IReadOnlyDictionary<string, string> metadata)
    {
        long bytes = Encoding.UTF8.GetByteCount(key)
                     + Encoding.UTF8.GetByteCount(rawMarkdown)
                     + Encoding.UTF8.GetByteCount(body);
        foreach (var pair in metadata)
        {
            bytes += Encoding.UTF8.GetByteCount(pair.Key) + Encoding.UTF8.GetByteCount(pair.Value);
        }
        return Math.Max(1, bytes);
    }

    private static string ResolveDirectory(string? configured, string contentRootPath)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var contentRootCandidate = Path.Combine(contentRootPath, "Preambles");
        if (Directory.Exists(contentRootCandidate))
        {
            return contentRootCandidate;
        }

        return Path.Combine(AppContext.BaseDirectory, "Preambles");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileChanged;
            _watcher.Changed -= OnFileChanged;
            _watcher.Deleted -= OnFileDeleted;
            _watcher.Renamed -= OnFileRenamed;
            _watcher.Error -= OnWatcherError;
            _watcher.Dispose();
        }
    }
}
