using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Auxline.Services;

/// <summary>
/// Bridges GSMTC app IDs to process names (for Core Audio) and display names (for the settings).
/// First asks the Windows apps registry (shell:AppsFolder), which handles hashed IDs used by
/// Firefox-based browsers (e.g. "4155BD7E5BB5DDC" → "Zen", zen.exe).
/// Falls back to parsing the ID itself ("Spotify.exe", "...!Spotify").
/// </summary>
public static class AppIdentity
{
    private const string ExeSuffix = ".exe";
    private const string TargetPathProperty = "System.Link.TargetParsingPath";

    private static readonly ConcurrentDictionary<string, string> ProcessNameCache = new();
    private static readonly ConcurrentDictionary<string, string> DisplayNameCache = new();

    public static string ToProcessName(string appUserModelId) =>
        ProcessNameCache.GetOrAdd(appUserModelId, id => ResolveProcessName(id) ?? ParseFromId(id));

    /// <summary>The name shown in the Start menu, e.g. "Zen" or "Google Chrome".</summary>
    public static string ToDisplayName(string appUserModelId) =>
        DisplayNameCache.GetOrAdd(appUserModelId, id => ResolveDisplayName(id) ?? ParseFromId(id));

    private static string? ResolveProcessName(string appUserModelId)
    {
        var app = FindInAppsFolder(appUserModelId);
        string? targetPath = app?.ExtendedProperty(TargetPathProperty) as string;
        return string.IsNullOrEmpty(targetPath) ? null : Path.GetFileNameWithoutExtension(targetPath);
    }

    private static string? ResolveDisplayName(string appUserModelId)
    {
        string? name = FindInAppsFolder(appUserModelId)?.Name as string;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <returns>The shell item for the app, or null when it isn't registered (or the shell is unavailable).</returns>
    private static dynamic? FindInAppsFolder(string appUserModelId)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return null;

            dynamic shell = Activator.CreateInstance(shellType)!;
            return shell.NameSpace("shell:AppsFolder").ParseName(appUserModelId);
        }
        catch (COMException ex)
        {
            Trace.TraceWarning($"Apps folder lookup failed for '{appUserModelId}': {ex.Message}");
            return null;
        }
    }

    private static string ParseFromId(string appUserModelId)
    {
        var appPart = appUserModelId[(appUserModelId.LastIndexOf('!') + 1)..];
        return appPart.EndsWith(ExeSuffix, StringComparison.OrdinalIgnoreCase)
            ? appPart[..^ExeSuffix.Length]
            : appPart;
    }
}
