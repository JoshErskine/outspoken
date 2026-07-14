using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Outspoken.Core.Cleanup;

/// <summary>
/// Stores the Anthropic API key encrypted with Windows DPAPI (CurrentUser scope) —
/// only this Windows user on this machine can decrypt it (ADR-001 §3). Never plaintext,
/// never in the repo. The key exists on disk only as a DPAPI blob and in memory only
/// for the duration of a cleanup call.
/// </summary>
public static class ApiKeyStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Outspoken.CleanupKey.v1");

    /// <summary>The real key location. Tests pass their own path so they never touch this.</summary>
    public static string DefaultKeyFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Outspoken", "apikey.dpapi");

    // Kept for callers/log messages that reference the real path.
    public static string KeyFilePath => DefaultKeyFilePath;

    public static bool Exists => File.Exists(DefaultKeyFilePath);

    public static bool ExistsAt(string path) => File.Exists(path);

    /// <summary>Encrypts and persists the key. Overwrites any existing key.</summary>
    public static void Save(string apiKey, string? keyFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key is empty.", nameof(apiKey));

        var path = keyFilePath ?? DefaultKeyFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(apiKey.Trim()), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, encrypted);
    }

    /// <summary>Decrypts the stored key, or null if none is stored.</summary>
    public static string? TryLoad(string? keyFilePath = null)
    {
        var path = keyFilePath ?? DefaultKeyFilePath;
        if (!File.Exists(path))
            return null;
        try
        {
            var encrypted = File.ReadAllBytes(path);
            var decrypted = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (CryptographicException)
        {
            // Blob unreadable (different user/machine, or corrupt) — treat as no key.
            return null;
        }
    }
}
