using System.IO;
using System.Security.Cryptography;
using System.Text;
using Google.Apis.Util.Store;
using Newtonsoft.Json;

namespace DCM.YouTube;

internal sealed class EncryptedFileDataStore : IDataStore
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly string _folder;
    private readonly DataProtectionScope _scope;
    private readonly object _migrationLock = new object();

    public EncryptedFileDataStore(string folder)
    {
        _folder = folder ?? throw new ArgumentNullException(nameof(folder));
#pragma warning disable CA1416
        _scope = DataProtectionScope.CurrentUser;
#pragma warning restore CA1416
        Directory.CreateDirectory(_folder);
    }

    public Task StoreAsync<T>(string key, T value)
    {
        StoreSync(key, value);
        return Task.CompletedTask;
    }

    private void StoreSync<T>(string key, T value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key must not be empty.", nameof(key));
        }

        var path = GetPath(key);
        var json = JsonConvert.SerializeObject(value);
        var payload = Utf8NoBom.GetBytes(json);

        if (OperatingSystem.IsWindows())
        {
            payload = ProtectedData.Protect(payload, optionalEntropy: null, _scope);
        }

        // Atomic write: write to temp file, then move
        var tempPath = path + ".tmp";
        File.WriteAllBytes(tempPath, payload);
        File.Move(tempPath, path, overwrite: true);
    }

    public Task DeleteAsync<T>(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Task.CompletedTask;
        }

        var path = GetPath(key);
        if (File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // File might be in use or already deleted
            }
        }

        return Task.CompletedTask;
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return default(T);
        }

        var path = GetPath(key);
        var legacyPath = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".json");

        // Migrate from legacy plaintext file if it exists (thread-safe)
        if (File.Exists(legacyPath))
        {
            lock (_migrationLock)
            {
                // Double-check after acquiring lock
                if (File.Exists(legacyPath))
                {
                    try
                    {
                        var json = File.ReadAllText(legacyPath, Utf8NoBom);
                        var value = JsonConvert.DeserializeObject<T>(json);
                        if (value is not null)
                        {
                            // Store in new format (encrypted on Windows)
                            StoreSync(key, value);

                            // Delete legacy file after successful migration
                            try
                            {
                                File.Delete(legacyPath);
                            }
                            catch (IOException)
                            {
                                // Already deleted by another thread, that's fine
                            }

                            return value;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                    {
                        // Ignore migration errors and proceed to the default path
                    }
                }
            }
        }

        if (!File.Exists(path))
        {
            return default(T);
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var json = TryUnprotect(bytes);

            if (json is null)
            {
                // File is plaintext
                json = Utf8NoBom.GetString(bytes);
            }

            var value = JsonConvert.DeserializeObject<T>(json);
            if (value is null)
            {
                return default(T);
            }

            // If we loaded a plaintext file from the primary path, encrypt it in-place
            if (TryUnprotect(bytes) is null && OperatingSystem.IsWindows())
            {
                await StoreAsync(key, value);
            }

            return value;
        }
        catch (Exception ex) when (ex is IOException or JsonException or CryptographicException or UnauthorizedAccessException)
        {
            return default(T);
        }
    }

    public Task ClearAsync()
    {
        if (!Directory.Exists(_folder))
        {
            return Task.CompletedTask;
        }

        foreach (var file in Directory.EnumerateFiles(_folder))
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // File might be in use, ignore
            }
            catch (UnauthorizedAccessException)
            {
                // No permissions, ignore
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> ContainsKeyAsync<T>(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Task.FromResult(false);
        }

        var path = GetPath(key);
        if (File.Exists(path))
        {
            return Task.FromResult(true);
        }

        // Also check legacy path for completeness
        var legacyPath = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".json");
        return Task.FromResult(File.Exists(legacyPath));
    }

    private string GetPath(string key)
    {
        var safeKey = key.Trim();
        if (safeKey.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
        {
            throw new ArgumentException("Key contains invalid path characters.", nameof(key));
        }
        return Path.Combine(_folder, $"{safeKey}.bin");
    }

    private string? TryUnprotect(byte[] payload)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var raw = ProtectedData.Unprotect(payload, optionalEntropy: null, _scope);
            return Utf8NoBom.GetString(raw);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}