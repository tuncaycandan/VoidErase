using System;
using System.IO;
using System.Security.Cryptography;
using VoidErase;

internal static class DeletionEngineV2
{
    // V2 is a file-level logical sanitization engine.
    // It deliberately does NOT claim physical-media sanitization on SSD/flash/virtual media.
    // The destructive phase is non-cancellable once started so an interrupted operation
    // cannot intentionally leave a half-wiped file behind.
    private const int BufferSize = 1024 * 1024;

    internal static void DestroyFile(string path, IProgressReporter reporter)
    {
        if (reporter == null)
            throw new ArgumentNullException(nameof(reporter));

        PrepareTarget(path);
        path = Path.GetFullPath(path);
        reporter.ThrowIfCancellationRequested();

        long length;
        using (FileStream probe = OpenTarget(path, FileAccess.Read))
            length = probe.Length;

        // Once this point is reached the operation is destructive and must finish.
        using (FileStream stream = OpenTarget(path, FileAccess.ReadWrite))
        {
            long totalWork = SafeMultiply(length, 3);
            DateTime started = DateTime.UtcNow;

            OverwriteRandom(stream, length, reporter, 0, totalWork, started);
            OverwriteZeros(stream, length, reporter, length, totalWork, started);
            VerifyZeros(stream, length, reporter, SafeMultiply(length, 2), totalWork, started);

            stream.SetLength(0);
            stream.Flush(true);
        }

        reporter.ReportFinalizing();
        FinalizeDeletion(path);

        VerificationResult verification = SanitizationVerification.VerifyPathAbsent(path);
        if (verification.Status != VerificationStatus.Verified)
        {
            throw new IOException(
                L.T(
                    "V2 son doğrulaması başarısız oldu: kaynak yol hâlâ mevcut.",
                    "V2 final verification failed: the source path is still present."));
        }
    }

    internal static void DestroyFileSilent(string path)
    {
        PrepareTarget(path);
        path = Path.GetFullPath(path);

        long length;
        using (FileStream probe = OpenTarget(path, FileAccess.Read))
            length = probe.Length;

        using (FileStream stream = OpenTarget(path, FileAccess.ReadWrite))
        {
            byte[] buffer = new byte[BufferSize];
            try
            {
                using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
                {
                    WritePass(stream, length, buffer, rng, false);
                }

                Array.Clear(buffer, 0, buffer.Length);
                WritePass(stream, length, buffer, null, true);
                stream.SetLength(0);
                stream.Flush(true);
            }
            finally
            {
                CryptoCompat.ZeroMemory(buffer);
            }
        }

        FinalizeDeletion(path);

        VerificationResult verification = SanitizationVerification.VerifyPathAbsent(path);
        if (verification.Status != VerificationStatus.Verified)
            throw new IOException(L.T("V2 silme doğrulaması başarısız oldu.", "V2 deletion verification failed."));
    }

    private static void PrepareTarget(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException(L.T("Dosya bulunamadı.", "File not found."), path);

        string fullPath = Path.GetFullPath(path);
        if (VoidEraseSafety.IsProtectedPath(fullPath))
            throw new InvalidOperationException(
                L.T("Korumalı sistem yolu üzerinde işlem yapılmıyor.", "Protected system paths are not processed."));

        if (VoidEraseSafety.IsSameAsExecutable(fullPath))
            throw new InvalidOperationException(
                L.T("Uygulamanın kendi dosyası üzerinde işlem yapılmıyor.", "The application executable is not processed."));

        FileAttributes attributes = File.GetAttributes(fullPath);
        if ((attributes & FileAttributes.System) != 0)
            throw new InvalidOperationException(
                L.T("Sistem dosyaları üzerinde işlem yapılmıyor.", "System files are not processed."));

        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException(
                L.T(
                    "Sembolik bağlantı veya reparse point olan dosyalar üzerinde işlem yapılmıyor.",
                    "Symbolic links and reparse-point files are not processed."));

        if ((attributes & FileAttributes.Hidden) != 0 ||
            (attributes & FileAttributes.ReadOnly) != 0)
        {
            FileAttributes writable = attributes & ~(FileAttributes.Hidden | FileAttributes.ReadOnly);
            File.SetAttributes(path, writable);
        }
    }

    private static FileStream OpenTarget(string path, FileAccess access)
    {
        return new FileStream(
            path,
            FileMode.Open,
            access,
            FileShare.None,
            BufferSize,
            FileOptions.SequentialScan | FileOptions.WriteThrough);
    }

    private static void OverwriteRandom(
        FileStream stream,
        long length,
        IProgressReporter reporter,
        long offset,
        long totalWork,
        DateTime started)
    {
        byte[] buffer = new byte[BufferSize];
        try
        {
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                WritePass(stream, length, buffer, rng, false, reporter, offset, totalWork, started);
            }
        }
        finally
        {
            CryptoCompat.ZeroMemory(buffer);
        }
    }

    private static void OverwriteZeros(
        FileStream stream,
        long length,
        IProgressReporter reporter,
        long offset,
        long totalWork,
        DateTime started)
    {
        byte[] buffer = new byte[BufferSize];
        try
        {
            WritePass(stream, length, buffer, null, true, reporter, offset, totalWork, started);
        }
        finally
        {
            CryptoCompat.ZeroMemory(buffer);
        }
    }

    private static void VerifyZeros(
        FileStream stream,
        long length,
        IProgressReporter reporter,
        long offset,
        long totalWork,
        DateTime started)
    {
        stream.Flush(true);
        stream.Position = 0;

        byte[] buffer = new byte[BufferSize];
        long processed = 0;
        try
        {
            while (processed < length)
            {
                int wanted = (int)Math.Min(buffer.Length, length - processed);
                int read = ReadExactlyOrThrow(stream, buffer, 0, wanted);
                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] != 0)
                    {
                        throw new IOException(
                            L.T(
                                "V2 son sıfır doğrulaması başarısız oldu.",
                                "V2 final zero-pass verification failed."));
                    }
                }

                processed += read;
                Report(reporter, offset + processed, totalWork, started, true);
            }
        }
        finally
        {
            CryptoCompat.ZeroMemory(buffer);
        }
    }

    private static void WritePass(
        FileStream stream,
        long length,
        byte[] buffer,
        RandomNumberGenerator rng,
        bool zeros,
        IProgressReporter reporter = null,
        long offset = 0,
        long totalWork = 1,
        DateTime started = default(DateTime))
    {
        stream.Position = 0;
        long processed = 0;

        while (processed < length)
        {
            int count = (int)Math.Min(buffer.Length, length - processed);
            if (zeros)
                Array.Clear(buffer, 0, count);
            else
                rng.GetBytes(buffer);

            stream.Write(buffer, 0, count);
            processed += count;

            if (reporter != null)
                Report(reporter, offset + processed, totalWork, started, false);
        }

        stream.Flush(true);
    }

    private static int ReadExactlyOrThrow(Stream stream, byte[] buffer, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read = stream.Read(buffer, offset + total, count - total);
            if (read == 0)
                throw new EndOfStreamException();
            total += read;
        }
        return total;
    }

    private static void Report(
        IProgressReporter reporter,
        long processed,
        long totalWork,
        DateTime started,
        bool validation)
    {
        long safeTotal = Math.Max(totalWork, 1);
        TimeSpan elapsed = DateTime.UtcNow - started;

        if (validation)
        {
            long current = Math.Min(processed, safeTotal);
            reporter.ReportValidation(current, safeTotal, elapsed);
        }
        else
        {
            reporter.ReportProgress(Math.Min(processed, safeTotal), safeTotal, elapsed);
        }
    }

    private static void FinalizeDeletion(string path)
    {
        string directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
            throw new IOException(L.T("Dosya yolu çözümlenemedi.", "The file directory could not be resolved."));

        string renamed = Path.Combine(
            directory,
            ".ve2." + Guid.NewGuid().ToString("N") + ".wiped");

        // Rename the already-wiped inode/file entry before deletion. This removes the
        // original user-visible name even if the final delete encounters a transient error.
        File.Move(path, renamed);

        try
        {
            File.Delete(renamed);
        }
        finally
        {
            if (File.Exists(renamed))
            {
                try { File.Delete(renamed); } catch { }
            }
        }

        if (File.Exists(path) || File.Exists(renamed))
            throw new IOException(
                L.T(
                    "V2 dosya girdisi kaldırılamadı.",
                    "V2 file entry could not be removed."));
    }

    private static long SafeMultiply(long value, int multiplier)
    {
        if (value <= 0) return 1;
        if (value > long.MaxValue / multiplier) return long.MaxValue;
        return value * multiplier;
    }
}
