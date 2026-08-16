using Microsoft.Win32;

namespace VoidErase;

internal static class VoidEraseSettings
{
    private const string KeyPath = @"Software\VoidErase";
    public static bool AskBeforeDeletion { get => GetBool(nameof(AskBeforeDeletion), true); set => SetBool(nameof(AskBeforeDeletion), value); }
    public static bool ProtectSystemPaths { get => GetBool(nameof(ProtectSystemPaths), true); set => SetBool(nameof(ProtectSystemPaths), value); }
    public static bool CheckUpdatesOnStartup { get => GetBool(nameof(CheckUpdatesOnStartup), true); set => SetBool(nameof(CheckUpdatesOnStartup), value); }
    public static bool KeepLogs { get => GetBool(nameof(KeepLogs), true); set => SetBool(nameof(KeepLogs), value); }
    private static bool GetBool(string name, bool fallback) { try { using var key = Registry.CurrentUser.OpenSubKey(KeyPath); return key?.GetValue(name) is int i ? i != 0 : fallback; } catch { return fallback; } }
    private static void SetBool(string name, bool value) { try { using var key = Registry.CurrentUser.CreateSubKey(KeyPath); key?.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord); } catch { } }
}
