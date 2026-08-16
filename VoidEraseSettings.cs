using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VoidErase;

internal static class VoidEraseSettings
{
    private const string KeyPath = @"Software\VoidErase";

    public static bool AskBeforeDeletion
    {
        get => GetBool(nameof(AskBeforeDeletion), true);
        set => SetBool(nameof(AskBeforeDeletion), value);
    }

    public static bool ProtectSystemPaths
    {
        get => GetBool(nameof(ProtectSystemPaths), true);
        set => SetBool(nameof(ProtectSystemPaths), value);
    }

    public static bool ProtectSystemDrive
    {
        get => GetBool(nameof(ProtectSystemDrive), true);
        set => SetBool(nameof(ProtectSystemDrive), value);
    }

    public static bool SkipReparsePoints
    {
        get => GetBool(nameof(SkipReparsePoints), false);
        set => SetBool(nameof(SkipReparsePoints), value);
    }

    public static bool CheckUpdatesOnStartup
    {
        get => GetBool(nameof(CheckUpdatesOnStartup), true);
        set => SetBool(nameof(CheckUpdatesOnStartup), value);
    }

    public static bool KeepLogs
    {
        get => GetBool(nameof(KeepLogs), true);
        set => SetBool(nameof(KeepLogs), value);
    }

    public static IReadOnlyList<string> ProtectedPaths
    {
        get => GetProtectedPaths();
    }

    public static void SetProtectedPaths(IEnumerable<string> paths)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);

            if (key == null)
                return;

            string[] normalized = paths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(NormalizePath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            key.SetValue(
                nameof(ProtectedPaths),
                normalized,
                RegistryValueKind.MultiString);
        }
        catch
        {
        }
    }

    private static IReadOnlyList<string> GetProtectedPaths()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);

            if (key?.GetValue(nameof(ProtectedPaths)) is string[] paths)
            {
                return paths
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(NormalizePath)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
        catch
        {
        }

        return Array.Empty<string>();
    }

    public static bool IsProtectedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string candidate = NormalizePath(path);

        foreach (string protectedPath in ProtectedPaths)
        {
            if (candidate.Equals(protectedPath, StringComparison.OrdinalIgnoreCase))
                return true;

            if (candidate.StartsWith(
                    protectedPath + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string NormalizePath(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);

            return full.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }
    }

    private static bool GetBool(string name, bool fallback)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(name) is int i ? i != 0 : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static void SetBool(string name, bool value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key?.SetValue(
                name,
                value ? 1 : 0,
                RegistryValueKind.DWord);
        }
        catch
        {
        }
    }
}