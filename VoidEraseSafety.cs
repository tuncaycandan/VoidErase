namespace VoidErase;

internal static class VoidEraseSafety
{
    public static bool IsProtectedPath(string path)
    {
        if (!VoidEraseSettings.ProtectSystemPaths) return false;
        try
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            var windows = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).TrimEnd(Path.DirectorySeparatorChar);
            var programFiles = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)).TrimEnd(Path.DirectorySeparatorChar);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            return full.Equals(windows, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(windows + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || full.Equals(programFiles, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(programFiles + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(programFilesX86) && (full.Equals(programFilesX86, StringComparison.OrdinalIgnoreCase) || full.StartsWith(programFilesX86 + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
        }
        catch { return true; }
    }

    public static bool IsSameAsExecutable(string path)
    {
        try
        {
            var exe = Environment.ProcessPath;
            return exe != null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
