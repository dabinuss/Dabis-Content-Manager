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

        File.WriteAllBytes(path, payload);
        return Task.CompletedTask;
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
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<T> GetAsync<T>(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Task.FromResult(default(T)!);
        }

        var path = GetPath(key);
        if (!File.Exists(path))
        {
            return Task.FromResult(default(T)!);
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            var json = TryUnprotect(bytes) ?? Utf8NoBom.GetString(bytes);
            var value = JsonConvert.DeserializeObject<T>(json);
            if (value is null)
            {
                return Task.FromResult(default(T)!);
            }

            // Falls wir eine alte Klartext-Datei geladen haben, umgehend verschlüsselt speichern.
            if (OperatingSystem.IsWindows())
            {
                _ = StoreAsync(key, value);
            }

            return Task.FromResult(value);
        }
        catch
        {
            return Task.FromResult(default(T)!);
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
            catch
            {
                // Ignore cleanup failures.
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

        return Task.FromResult(File.Exists(GetPath(key)));
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
        catch
        {
            return null;
        }
    }
}
