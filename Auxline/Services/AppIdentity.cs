using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Auxline.Services;

/// <summary>
/// Bridges GSMTC app IDs and Core Audio process names.
/// First asks the Windows apps registry (shell:AppsFolder) for the app's exe, which handles
/// hashed IDs used by Firefox-based browsers (e.g. "4155BD7E5BB5DDC" → zen.exe).
/// Falls back to parsing the ID itself ("Spotify.exe", "...!Spotify").
/// </summary>
public static class AppIdentity
{
    private const string ExeSuffix = ".exe";
    private const string TargetPathProperty = "System.Link.TargetParsingPath";

    private static readonly ConcurrentDictionary<string, string> ProcessNameCache = new();

    public static string ToProcessName(string appUserModelId) =>
        ProcessNameCache.GetOrAdd(appUserModelId, id => ResolveFromAppsFolder(id) ?? ParseFromId(id));

    private static string? ResolveFromAppsFolder(string appUserModelId)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return null;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic? app = shell.NameSpace("shell:AppsFolder").ParseName(appUserModelId);
            if (app is null) return null;

            string? targetPath = app.ExtendedProperty(TargetPathProperty) as string;
            return string.IsNullOrEmpty(targetPath) ? null : Path.GetFileNameWithoutExtension(targetPath);
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
