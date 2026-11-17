using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Guildhall.AgentHost.Configuration;

/// <summary>
/// Encrypts and stores provider API keys per Windows user using DPAPI (CryptProtectData).
/// Keys are never stored in plaintext, logged, or exposed to the frontend or child processes.
/// </summary>
public sealed class DpapiCredentialStore(
    ILogger<DpapiCredentialStore> logger,
    string? directoryOverride = null) : ICredentialStore
{
    private const int CryptProtectUiForbidden = 0x1;
    private static readonly byte[] EntropyBytes = "Guildhall-Phase2-ApiKey-v1"u8.ToArray();

    private readonly string _directory = string.IsNullOrWhiteSpace(directoryOverride)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guildhall", "credentials")
        : directoryOverride;

    public async ValueTask<string?> GetApiKeyAsync(string providerId, CancellationToken ct = default)
    {
        var path = GetPath(providerId);
        if (!File.Exists(path)) return null;

        try
        {
            var encrypted = await File.ReadAllBytesAsync(path, ct);
            if (encrypted.Length == 0) return null;
            var plainBytes = Unprotect(encrypted);
            var key = Encoding.UTF8.GetString(plainBytes).Trim();
            Array.Clear(plainBytes);
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Could not decrypt DPAPI credential for provider {ProviderId}; failing closed.", SanitizeProviderId(providerId));
            return null;
        }
    }

    public async ValueTask SetApiKeyAsync(string providerId, string apiKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        Directory.CreateDirectory(_directory);
        var path = GetPath(providerId);
        var plainBytes = Encoding.UTF8.GetBytes(apiKey.Trim());
        try
        {
            var encrypted = Protect(plainBytes);
            var tempPath = path + ".tmp";
            await File.WriteAllBytesAsync(tempPath, encrypted, ct);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            Array.Clear(plainBytes);
        }
    }

    public ValueTask DeleteApiKeyAsync(string providerId, CancellationToken ct = default)
    {
        var path = GetPath(providerId);
        if (File.Exists(path))
            File.Delete(path);
        return ValueTask.CompletedTask;
    }

    public async ValueTask<CredentialStatus> GetStatusAsync(string providerId, CancellationToken ct = default)
    {
        var key = await GetApiKeyAsync(providerId, ct);
        if (string.IsNullOrWhiteSpace(key))
            return new CredentialStatus(false, null);

        var hint = key.Length >= 8 ? $"••••{key[^4..]}" : "Configured";
        return new CredentialStatus(true, hint);
    }

    private string GetPath(string providerId) =>
        Path.Combine(_directory, $"{SanitizeProviderId(providerId)}.dpapi");

    private static string SanitizeProviderId(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        var clean = new string(providerId.Trim().ToLowerInvariant().Where(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_').ToArray());
        if (string.IsNullOrWhiteSpace(clean))
            throw new ArgumentException("Invalid provider identifier.", nameof(providerId));
        return clean;
    }

    private static byte[] Protect(byte[] plainBytes)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows DPAPI is only supported on Windows.");

        var plainHandle = GCHandle.Alloc(plainBytes, GCHandleType.Pinned);
        var entropyHandle = GCHandle.Alloc(EntropyBytes, GCHandleType.Pinned);
        try
        {
            var dataIn = new DataBlob { Size = (uint)plainBytes.Length, Data = plainHandle.AddrOfPinnedObject() };
            var entropy = new DataBlob { Size = (uint)EntropyBytes.Length, Data = entropyHandle.AddrOfPinnedObject() };
            if (!CryptProtectData(ref dataIn, null, ref entropy, nint.Zero, nint.Zero, CryptProtectUiForbidden, out var dataOut))
                throw new InvalidOperationException($"DPAPI CryptProtectData failed with Win32 error {Marshal.GetLastWin32Error()}.");

            try
            {
                var result = new byte[dataOut.Size];
                Marshal.Copy(dataOut.Data, result, 0, (int)dataOut.Size);
                return result;
            }
            finally
            {
                if (dataOut.Data != nint.Zero)
                    LocalFree(dataOut.Data);
            }
        }
        finally
        {
            plainHandle.Free();
            entropyHandle.Free();
        }
    }

    private static byte[] Unprotect(byte[] encryptedBytes)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows DPAPI is only supported on Windows.");

        var encryptedHandle = GCHandle.Alloc(encryptedBytes, GCHandleType.Pinned);
        var entropyHandle = GCHandle.Alloc(EntropyBytes, GCHandleType.Pinned);
        try
        {
            var dataIn = new DataBlob { Size = (uint)encryptedBytes.Length, Data = encryptedHandle.AddrOfPinnedObject() };
            var entropy = new DataBlob { Size = (uint)EntropyBytes.Length, Data = entropyHandle.AddrOfPinnedObject() };
            if (!CryptUnprotectData(ref dataIn, nint.Zero, ref entropy, nint.Zero, nint.Zero, CryptProtectUiForbidden, out var dataOut))
                throw new InvalidOperationException($"DPAPI CryptUnprotectData failed with Win32 error {Marshal.GetLastWin32Error()}.");

            try
            {
                var result = new byte[dataOut.Size];
                Marshal.Copy(dataOut.Data, result, 0, (int)dataOut.Size);
                return result;
            }
            finally
            {
                if (dataOut.Data != nint.Zero)
                    LocalFree(dataOut.Data);
            }
        }
        finally
        {
            encryptedHandle.Free();
            entropyHandle.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public uint Size;
        public nint Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn,
        string? szDataDescr,
        ref DataBlob pOptionalEntropy,
        nint pvReserved,
        nint pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        nint ppszDataDescr,
        ref DataBlob pOptionalEntropy,
        nint pvReserved,
        nint pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint hMem);
}
