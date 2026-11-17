using System.Text;
using System.Text.Json;

namespace Guildhall.AgentHost.Chat;

public static class ChatSerializationHelpers
{
    public static HashSet<string> ParseToolIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var items = JsonSerializer.Deserialize<string[]>(json);
            return items is null ? [] : new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return [];
        }
    }

    public static string? DetectImageContentType(ReadOnlySpan<byte> content)
    {
        if (content.Length < 12) return null;
        if (content.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (content[0] == 0xFF && content[1] == 0xD8 && content[^2] == 0xFF && content[^1] == 0xD9) return "image/jpeg";
        if (Encoding.ASCII.GetString(content[..4]) == "RIFF" && Encoding.ASCII.GetString(content.Slice(8, 4)) == "WEBP") return "image/webp";
        if (Encoding.ASCII.GetString(content[..3]) == "GIF") return "image/gif";
        return null;
    }

    public static bool IsSupportedImage(ReadOnlySpan<byte> content) => DetectImageContentType(content) is not null;
}
