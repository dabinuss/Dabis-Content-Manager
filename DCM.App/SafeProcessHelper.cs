using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace DCM.App;

/// <summary>
/// Provides secure methods for starting processes with validated paths and URLs.
/// Prevents command injection attacks by validating inputs before using UseShellExecute.
/// </summary>
public static class SafeProcessHelper
{
    private static readonly Regex DangerousPathCharsRegex = new(
        @"[<>|&;`$(){}[\]!]",
        RegexOptions.Compiled);

    private static readonly string[] AllowedUrlSchemes = ["http", "https"];

    /// <summary>
    /// Safely opens a URL in the default browser after validating it.
    /// Only allows http and https URLs to prevent command injection.
    /// </summary>
    /// <param name="url">The URL to open.</param>
    /// <returns>True if the URL was opened successfully, false otherwise.</returns>
    public static bool TryOpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return TryOpenUrl(uri);
    }

    /// <summary>
    /// Safely opens a URI in the default browser after validating it.
    /// Only allows http and https schemes to prevent command injection.
    /// </summary>
    /// <param name="uri">The URI to open.</param>
    /// <returns>True if the URI was opened successfully, false otherwise.</returns>
    public static bool TryOpenUrl(Uri? uri)
    {
        if (uri is null)
        {
            return false;
        }

        // Only allow safe URL schemes
        if (!IsAllowedUrlScheme(uri.Scheme))
        {
            return false;
        }

        // Additional validation: ensure no dangerous characters in the URL
        var urlString = uri.ToString();
        if (ContainsDangerousCharacters(urlString))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(urlString)
            {
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Safely opens a local file in its associated application after validating the path.
    /// Validates that the path exists and doesn't contain dangerous characters.
    /// </summary>
    /// <param name="filePath">The file path to open.</param>
    /// <returns>True if the file was opened successfully, false otherwise.</returns>
    public static bool TryOpenFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        // Normalize the path to prevent path traversal
        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(filePath);
        }
        catch
        {
            return false;
        }

        // Check for dangerous shell metacharacters
        if (ContainsDangerousCharacters(normalizedPath))
        {
            return false;
        }

        // Verify the file actually exists
        if (!File.Exists(normalizedPath))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(normalizedPath)
            {
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAllowedUrlScheme(string scheme)
    {
        foreach (var allowed in AllowedUrlSchemes)
        {
            if (string.Equals(scheme, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsDangerousCharacters(string input)
    {
        // Check for shell metacharacters that could enable command injection
        return DangerousPathCharsRegex.IsMatch(input);
    }
}
