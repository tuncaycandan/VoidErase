using VoidErase;
using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;


internal interface IProgressReporter
{
    void ReportProgress(long processed, long total, TimeSpan elapsed);
    void ReportValidation(long current, long total, TimeSpan elapsed);
    void ReportFinalizing();
    void ThrowIfCancellationRequested();
}


internal static class L
{
    private static bool _english;

    public static bool English => _english;

    private const string KeyPath = @"Software\VoidErase";
    private const string ValueName = "Language";
    private const string ConfirmValue = "ConfirmBeforeErase";
    private const string AutoUpdateValue = "AutoUpdate";

    public static bool ConfirmBeforeErase { get; private set; } = true;
    public static bool AutoUpdate { get; private set; } = true;

    public static bool Turkish { get; private set; }

    static L()
    {
        Load();
    }



    
    public static void Load()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath);
            string? value = key?.GetValue(ValueName) as string;

            if (string.Equals(value, "tr", StringComparison.OrdinalIgnoreCase))
                Turkish = true;
            else if (string.Equals(value, "en", StringComparison.OrdinalIgnoreCase))
                Turkish = false;
            else
                Turkish = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                    .Equals("tr", StringComparison.OrdinalIgnoreCase);

            ConfirmBeforeErase = ReadBool(key, ConfirmValue, true);
            AutoUpdate = ReadBool(key, AutoUpdateValue, true);
            _english = !Turkish;
        }
        catch
        {
            Turkish = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                .Equals("tr", StringComparison.OrdinalIgnoreCase);
            ConfirmBeforeErase = true;
            AutoUpdate = true;
            _english = !Turkish;
        }
    }

    private static bool ReadBool(RegistryKey? key, string name, bool fallback)
    {
        object? value = key?.GetValue(name);
        return value is int i ? i != 0 : fallback;
    }

    public static void SaveSettings(bool confirmBeforeErase, bool autoUpdate)
    {
        ConfirmBeforeErase = confirmBeforeErase;
        AutoUpdate = autoUpdate;
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath, true)
            ?? throw new InvalidOperationException("Settings could not be saved.");
        key.SetValue(ConfirmValue, confirmBeforeErase ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(AutoUpdateValue, autoUpdate ? 1 : 0, RegistryValueKind.DWord);
    }

    public static void SetLanguage(bool turkish)
    {
        Turkish = turkish;
        _english = !turkish;

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath, true)
            ?? throw new InvalidOperationException("Language preference could not be saved.");

        key.SetValue(ValueName, turkish ? "tr" : "en", RegistryValueKind.String);
    }

    public static string T(string tr, string en) => Turkish ? tr : en;

    public static void UseTurkish() => SetLanguage(true);
    public static void UseEnglish() => SetLanguage(false);
}

internal static class Program
{
    internal static string AppVersion =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.0.0";

    internal static string DisplayVersion => "v" + AppVersion;


    internal const string MenuKey = @"Software\Classes\*\shell\VoidErase";
    internal const string DirectoryMenuKey = @"Software\Classes\Directory\shell\VoidErase";
    // Eski sürümlerde kullanılan anahtarlar. Eski kurulumların da tamamen kaldırılması için tutulur.
    internal const string LegacyMenuKey = @"Software\Classes\*\shell\PermanentDestroy";
    internal const string LegacyDirectoryMenuKey = @"Software\Classes\Directory\shell\PermanentDestroy";
    private const string CommandKey = MenuKey + @"\command";
    private const string DirectoryCommandKey = DirectoryMenuKey + @"\command";
    private const int ChunkSize = 16 * 1024 * 1024;

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        L.Load();

        string? file = null;
        bool install = false;
        bool uninstall = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--install": install = true; break;
                case "--uninstall": uninstall = true; break;
                case "--destroy":
                    if (i + 1 < args.Length) file = args[++i].Trim('"');
                    break;
                default:
                    if (!args[i].StartsWith("--", StringComparison.Ordinal))
                        file ??= args[i].Trim('"');
                    break;
            }
        }

        try
        {
            if (install)
            {
                bool ok = InstallContextMenu(false);
                MessageBox.Show(
                    L.T("Sağ tık menüsü başarıyla etkinleştirildi.", "Context menu enabled successfully."),
                    "VoidErase",
                    MessageBoxButtons.OK,
                    ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                return;
            }

            if (uninstall)
            {
                bool ok = UninstallContextMenu();
                MessageBox.Show(
                    L.T("Sağ tık menüsü kaldırıldı.", "Context menu removed."),
                    "VoidErase",
                    MessageBoxButtons.OK,
                    ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                return;
            }

            // Explorer sağ tık çağrısı: ana arayüzü açma.
            // --destroy parametresi yalnızca onay + işlem modunu başlatır.
            if (file != null && args.Any(a =>
                a.Equals("--destroy", StringComparison.OrdinalIgnoreCase)))
            {
                Application.Run(new ShellDestroyForm(file));
                return;
            }

            Application.Run(new MainForm(null));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "VoidErase",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    internal static string GetExePath()
    {
        return Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException(L.T("Çalışan EXE yolu alınamadı.", "The executable path could not be determined."));
    }

    internal static bool InstallContextMenu(bool showMessage)
    {
        string exe = GetExePath();
        string command = $"\"{exe}\" --destroy \"%1\"";

        // Eski ve yeni kayıtların çakışmasını önle.
        Registry.CurrentUser.DeleteSubKeyTree(LegacyMenuKey, false);
        Registry.CurrentUser.DeleteSubKeyTree(LegacyDirectoryMenuKey, false);

        CreateContextMenuEntry(MenuKey, CommandKey, exe, command);
        CreateContextMenuEntry(DirectoryMenuKey, DirectoryCommandKey, exe, command);

        bool fileOk = VerifyContextCommand(CommandKey, command);
        bool directoryOk = VerifyContextCommand(DirectoryCommandKey, command);

        ShellRefresh.Notify();
        return fileOk && directoryOk;
    }

    private static void CreateContextMenuEntry(string menuKey, string commandKey, string exe, string command)
    {
        using (RegistryKey menu = Registry.CurrentUser.CreateSubKey(menuKey, true)
            ?? throw new InvalidOperationException(L.T("Registry menü anahtarı oluşturulamadı.", "The Registry menu key could not be created.")))
        {
            menu.SetValue("", L.T("Kalıcı Olarak Yok Et", "Permanent Delete"), RegistryValueKind.String);
            menu.SetValue("Icon", exe, RegistryValueKind.String);
            menu.SetValue("Position", "Bottom", RegistryValueKind.String);
        }

        using (RegistryKey cmd = Registry.CurrentUser.CreateSubKey(commandKey, true)
            ?? throw new InvalidOperationException(L.T("Registry command anahtarı oluşturulamadı.", "The Registry command key could not be created.")))
        {
            cmd.SetValue("", command, RegistryValueKind.String);
        }
    }

    private static bool VerifyContextCommand(string commandKey, string expected)
    {
        using RegistryKey? verify = Registry.CurrentUser.OpenSubKey(commandKey);
        return string.Equals(verify?.GetValue("") as string, expected, StringComparison.Ordinal);
    }

    internal static void UpdateContextMenuLanguage()
    {
        foreach (string keyName in new[] { MenuKey, DirectoryMenuKey })
        {
            using RegistryKey? menu = Registry.CurrentUser.OpenSubKey(keyName, writable: true);
            menu?.SetValue("", L.T("Kalıcı Olarak Yok Et", "Permanent Delete"), RegistryValueKind.String);
        }
        ShellRefresh.Notify();
    }

    internal static bool UninstallContextMenu()
    {
        // Güncel ve eski sürümlerin hem dosya hem klasör kayıtlarını kaldır.
        Registry.CurrentUser.DeleteSubKeyTree(MenuKey, false);
        Registry.CurrentUser.DeleteSubKeyTree(DirectoryMenuKey, false);
        Registry.CurrentUser.DeleteSubKeyTree(LegacyMenuKey, false);
        Registry.CurrentUser.DeleteSubKeyTree(LegacyDirectoryMenuKey, false);
        ShellRefresh.Notify();

        return Registry.CurrentUser.OpenSubKey(MenuKey) == null
            && Registry.CurrentUser.OpenSubKey(DirectoryMenuKey) == null
            && Registry.CurrentUser.OpenSubKey(LegacyMenuKey) == null
            && Registry.CurrentUser.OpenSubKey(LegacyDirectoryMenuKey) == null;
    }


    internal static void DestroyFileSilent(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(L.T("Dosya bulunamadı.", "File not found."), path);

        FileInfo info = new(path);
        if ((info.Attributes & FileAttributes.System) != 0)
            throw new InvalidOperationException(L.T("Sistem dosyaları üzerinde işlem yapılmıyor.", "System files are not processed."));

        string temp = Path.Combine(info.DirectoryName!,
            "." + info.Name + "." + Guid.NewGuid().ToString("N") + ".destroying");

        byte[] key = CryptoCompat.RandomBytes(32);
        byte[] headerNonce = CryptoCompat.RandomBytes(12);

        try
        {
            EncryptChunksSilent(path, temp, key, headerNonce);
            ValidateContainerSilent(temp, key, headerNonce);
            File.Delete(path);
            File.Delete(temp);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
        finally
        {
            CryptoCompat.ZeroMemory(key);
            CryptoCompat.ZeroMemory(headerNonce);
        }
    }

    private static void EncryptChunksSilent(
        string source, string destination, byte[] key, byte[] headerNonce)
    {
        FileInfo info = new(source);
        long total = info.Length;
        long chunks = total == 0 ? 0 : (total + ChunkSize - 1) / ChunkSize;

        using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan);
        using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 1024 * 1024, FileOptions.SequentialScan);

        CryptoCompat.WriteAll(output, new byte[] { (byte)'P', (byte)'D', (byte)'S', (byte)'1' });
        output.WriteByte(1);
        CryptoCompat.WriteAll(output, BitConverter.GetBytes(ChunkSize));
        CryptoCompat.WriteAll(output, BitConverter.GetBytes(total));
        CryptoCompat.WriteAll(output, BitConverter.GetBytes(chunks));
        CryptoCompat.WriteAll(output, headerNonce);

        byte[] plain = new byte[ChunkSize];
        byte[] cipher = new byte[ChunkSize];
        byte[] tag = new byte[16];
        using IncrementalHash sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        using AesGcmCompat aes = new AesGcmCompat(key);
        long index = 0;

        while (true)
        {
            int read = ReadChunk(input, plain);
            if (read == 0) break;

            byte[] nonce = MakeNonce(headerNonce, index);
            sourceHash.AppendData(plain, 0, read);

            aes.Encrypt(nonce, plain, 0, read,
                cipher, 0, tag);

            CryptoCompat.WriteAll(output, BitConverter.GetBytes(read));
            CryptoCompat.WriteAll(output, nonce);
            CryptoCompat.WriteAll(output, tag);
            output.Write(cipher, 0, read);

            CryptoCompat.ZeroMemory(nonce);
            CryptoCompat.ZeroMemory(tag);
            tag = new byte[16];
            index++;
        }

        byte[] sourceDigest = sourceHash.GetHashAndReset();
        CryptoCompat.WriteAll(output, sourceDigest);
        CryptoCompat.ZeroMemory(sourceDigest);
        output.Flush(true);
        CryptoCompat.ZeroMemory(plain);
        CryptoCompat.ZeroMemory(cipher);
        CryptoCompat.ZeroMemory(tag);
    }

    private static void ValidateContainerSilent(
        string path, byte[] key, byte[] expectedHeaderNonce)
    {
        using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        byte[] magic = new byte[4];
        ReadExactly(fs, magic);
        if (magic[0] != 'P' || magic[1] != 'D' || magic[2] != 'S' || magic[3] != '1')
            throw new InvalidDataException(L.T("Container başlığı geçersiz.", "Invalid container header."));

        int version = fs.ReadByte();
        if (version != 1) throw new InvalidDataException(L.T("Container sürümü geçersiz.", "Invalid container version."));

        byte[] b4 = new byte[4];
        byte[] b8 = new byte[8];

        ReadExactly(fs, b4);
        int chunkSize = BitConverter.ToInt32(b4, 0);
        ReadExactly(fs, b8);
        long total = BitConverter.ToInt64(b8, 0);
        ReadExactly(fs, b8);
        long chunks = BitConverter.ToInt64(b8, 0);

        byte[] headerNonce = new byte[12];
        ReadExactly(fs, headerNonce);

        if (!CryptoCompat.FixedTimeEquals(headerNonce, expectedHeaderNonce))
            throw new CryptographicException(L.T("Nonce doğrulaması başarısız.", "Nonce validation failed."));

        if (chunkSize != ChunkSize || total < 0 || chunks < 0)
            throw new InvalidDataException(L.T("Container bilgileri geçersiz.", "Invalid container information."));

        byte[] cipher = new byte[ChunkSize];
        byte[] plain = new byte[ChunkSize];
        byte[] tag = new byte[16];

        using AesGcmCompat aes = new AesGcmCompat(key);
        using IncrementalHash plainHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long counted = 0;

        for (long i = 0; i < chunks; i++)
        {
            ReadExactly(fs, b4);
            int length = BitConverter.ToInt32(b4, 0);
            if (length < 0 || length > ChunkSize)
                throw new InvalidDataException(L.T("Chunk uzunluğu geçersiz.", "Invalid chunk length."));

            byte[] nonce = new byte[12];
            ReadExactly(fs, nonce);
            ReadExactly(fs, tag);
            ReadExactly(fs, cipher, 0, length);

            aes.Decrypt(nonce, cipher, 0, length,
                tag, plain, 0);

            plainHash.AppendData(plain, 0, length);
            counted += length;

            CryptoCompat.ZeroMemory(nonce);
            CryptoCompat.ZeroMemory(tag);
            tag = new byte[16];
        }

        byte[] expectedDigest = new byte[32];
        ReadExactly(fs, expectedDigest);
        byte[] actualDigest = plainHash.GetHashAndReset();

        if (counted != total || fs.Position != fs.Length ||
            !CryptoCompat.FixedTimeEquals(expectedDigest, actualDigest))
            throw new InvalidDataException(L.T("Container doğrulaması başarısız.", "Container validation failed."));

        CryptoCompat.ZeroMemory(expectedDigest);
        CryptoCompat.ZeroMemory(actualDigest);
        CryptoCompat.ZeroMemory(cipher);
        CryptoCompat.ZeroMemory(plain);
        CryptoCompat.ZeroMemory(tag);
    }

    internal static void DestroyPath(string path, IProgressReporter form)
    {
        if (Directory.Exists(path))
        {
            DestroyDirectory(path, form);
            return;
        }

        DestroyFile(path, form);
    }

    private static void DestroyDirectory(string directory, IProgressReporter form)
    {
        string[] files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToArray();
        long totalBytes = 0;
        foreach (string file in files)
        {
            form.ThrowIfCancellationRequested();
            try { totalBytes += new FileInfo(file).Length; }
            catch { }
        }

        if (files.Length == 0)
        {
            Directory.Delete(directory, false);
            return;
        }

        long completedBytes = 0;
        Stopwatch overall = Stopwatch.StartNew();
        for (int i = 0; i < files.Length; i++)
        {
            form.ThrowIfCancellationRequested();
            string file = files[i];
            long fileSize = 0;
            try { fileSize = new FileInfo(file).Length; } catch { }

            form.ReportProgress(completedBytes, Math.Max(totalBytes, 1), overall.Elapsed);
            DestroyFile(file, new OffsetProgressReporter(form, completedBytes, fileSize, totalBytes));
            completedBytes += fileSize;
            form.ReportProgress(completedBytes, Math.Max(totalBytes, 1), overall.Elapsed);
        }

        // Dosyalar kaldırıldıktan sonra klasör ağacını alttan üste sil.
        string[] directories = Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories)
            .OrderByDescending(d => d.Length).ToArray();
        foreach (string subdir in directories)
        {
            form.ThrowIfCancellationRequested();
            if (Directory.Exists(subdir) && !Directory.EnumerateFileSystemEntries(subdir).Any())
                Directory.Delete(subdir, false);
        }

        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            Directory.Delete(directory, false);
    }

    private sealed class OffsetProgressReporter : IProgressReporter
    {
        private readonly IProgressReporter inner;
        private readonly long offset;
        private readonly long fileTotal;
        private readonly long overallTotal;

        public OffsetProgressReporter(IProgressReporter inner, long offset, long fileTotal, long overallTotal)
        {
            this.inner = inner;
            this.offset = offset;
            this.fileTotal = fileTotal;
            this.overallTotal = Math.Max(overallTotal, 1);
        }

        public void ReportProgress(long processed, long total, TimeSpan elapsed)
        {
            long scaled = offset + Math.Min(processed, fileTotal);
            inner.ReportProgress(scaled, overallTotal, elapsed);
        }

        public void ReportValidation(long current, long total, TimeSpan elapsed)
        {
            inner.ReportValidation(current, total, elapsed);
        }

        public void ReportFinalizing() => inner.ReportFinalizing();
        public void ThrowIfCancellationRequested() => inner.ThrowIfCancellationRequested();
    }

    internal static void DestroyFile(string path, IProgressReporter form)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(L.T("Dosya bulunamadı.", "File not found."), path);

        FileInfo info = new(path);

        if ((info.Attributes & FileAttributes.System) != 0)
            throw new InvalidOperationException(L.T("Sistem dosyaları üzerinde işlem yapılmıyor.", "System files are not processed."));

        string temp = Path.Combine(
            info.DirectoryName!,
            "." + info.Name + "." + Guid.NewGuid().ToString("N") + ".destroying");

        byte[] key = CryptoCompat.RandomBytes(32);
        byte[] headerNonce = CryptoCompat.RandomBytes(12);

        try
        {
            EncryptChunks(path, temp, key, headerNonce, form);
            ValidateContainer(temp, key, headerNonce, form);

            form.ThrowIfCancellationRequested();

            form.ReportFinalizing();

            File.Delete(path);
            File.Delete(temp);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
        finally
        {
            CryptoCompat.ZeroMemory(key);
            CryptoCompat.ZeroMemory(headerNonce);
        }
    }

    private static void EncryptChunks(
        string source, string destination,
        byte[] key, byte[] headerNonce, IProgressReporter form)
    {
        FileInfo info = new(source);
        long total = info.Length;
        long chunks = total == 0 ? 0 : (total + ChunkSize - 1) / ChunkSize;

        using FileStream input = new(
            source, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan);

        using FileStream output = new(
            destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            1024 * 1024, FileOptions.SequentialScan);

        CryptoCompat.WriteAll(output, new byte[] { (byte)'P', (byte)'D', (byte)'S', (byte)'1' });
        output.WriteByte(1);
        CryptoCompat.WriteAll(output, BitConverter.GetBytes(ChunkSize));
        CryptoCompat.WriteAll(output, BitConverter.GetBytes(total));
        CryptoCompat.WriteAll(output, BitConverter.GetBytes(chunks));
        CryptoCompat.WriteAll(output, headerNonce);

        byte[] plain = new byte[ChunkSize];
        byte[] cipher = new byte[ChunkSize];
        byte[] tag = new byte[16];
        using IncrementalHash sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        using AesGcmCompat aes = new AesGcmCompat(key);
        Stopwatch timer = Stopwatch.StartNew();
        long processed = 0;
        long index = 0;

        while (true)
        {
            form.ThrowIfCancellationRequested();

            int read = ReadChunk(input, plain);
            if (read == 0) break;

            byte[] nonce = MakeNonce(headerNonce, index);
            sourceHash.AppendData(plain, 0, read);

            aes.Encrypt(nonce,
                plain, 0, read,
                cipher, 0, tag);

            CryptoCompat.WriteAll(output, BitConverter.GetBytes(read));
            CryptoCompat.WriteAll(output, nonce);
            CryptoCompat.WriteAll(output, tag);
            output.Write(cipher, 0, read);

            processed += read;
            index++;

            form.ReportProgress(processed, total, timer.Elapsed);

            CryptoCompat.ZeroMemory(nonce);
            CryptoCompat.ZeroMemory(tag);
            tag = new byte[16];
        }

        byte[] sourceDigest = sourceHash.GetHashAndReset();
        CryptoCompat.WriteAll(output, sourceDigest);
        CryptoCompat.ZeroMemory(sourceDigest);
        output.Flush(true);

        CryptoCompat.ZeroMemory(plain);
        CryptoCompat.ZeroMemory(cipher);
        CryptoCompat.ZeroMemory(tag);
    }

    private static void ValidateContainer(
        string path, byte[] key, byte[] expectedHeaderNonce, IProgressReporter form)
    {
        using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        byte[] magic = new byte[4];
        ReadExactly(fs, magic);

        if (magic[0] != 'P' || magic[1] != 'D' || magic[2] != 'S' || magic[3] != '1')
            throw new InvalidDataException(L.T("Container başlığı geçersiz.", "Invalid container header."));

        int version = fs.ReadByte();
        if (version != 1)
            throw new InvalidDataException(L.T("Container sürümü geçersiz.", "Invalid container version."));

        byte[] b4 = new byte[4];
        byte[] b8 = new byte[8];

        ReadExactly(fs, b4);
        int chunkSize = BitConverter.ToInt32(b4, 0);

        ReadExactly(fs, b8);
        long total = BitConverter.ToInt64(b8, 0);

        ReadExactly(fs, b8);
        long chunks = BitConverter.ToInt64(b8, 0);

        byte[] headerNonce = new byte[12];
        ReadExactly(fs, headerNonce);

        if (!CryptoCompat.FixedTimeEquals(headerNonce, expectedHeaderNonce))
            throw new CryptographicException(L.T("Nonce doğrulaması başarısız.", "Nonce validation failed."));

        if (chunkSize != ChunkSize || total < 0 || chunks < 0)
            throw new InvalidDataException(L.T("Container bilgileri geçersiz.", "Invalid container information."));

        byte[] cipher = new byte[ChunkSize];
        byte[] plain = new byte[ChunkSize];
        byte[] tag = new byte[16];

        using AesGcmCompat aes = new AesGcmCompat(key);
        using IncrementalHash plainHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        long counted = 0;
        Stopwatch timer = Stopwatch.StartNew();

        for (long i = 0; i < chunks; i++)
        {
            form.ThrowIfCancellationRequested();

            ReadExactly(fs, b4);
            int length = BitConverter.ToInt32(b4, 0);

            if (length < 0 || length > ChunkSize)
                throw new InvalidDataException(L.T("Chunk uzunluğu geçersiz.", "Invalid chunk length."));

            byte[] nonce = new byte[12];
            ReadExactly(fs, nonce);
            ReadExactly(fs, tag);
            ReadExactly(fs, cipher, 0, length);

            aes.Decrypt(nonce,
                cipher, 0, length,
                tag,
                plain, 0);

            plainHash.AppendData(plain, 0, length);
            counted += length;
            form.ReportValidation(i + 1, chunks, timer.Elapsed);

            CryptoCompat.ZeroMemory(nonce);
            CryptoCompat.ZeroMemory(tag);
            tag = new byte[16];
        }

        byte[] expectedDigest = new byte[32];
        ReadExactly(fs, expectedDigest);
        byte[] actualDigest = plainHash.GetHashAndReset();

        if (counted != total || fs.Position != fs.Length ||
            !CryptoCompat.FixedTimeEquals(expectedDigest, actualDigest))
            throw new InvalidDataException(L.T("Container doğrulaması başarısız.", "Container validation failed."));

        CryptoCompat.ZeroMemory(expectedDigest);
        CryptoCompat.ZeroMemory(actualDigest);
        CryptoCompat.ZeroMemory(cipher);
        CryptoCompat.ZeroMemory(plain);
        CryptoCompat.ZeroMemory(tag);
    }

    private static byte[] MakeNonce(byte[] headerNonce, long index)
    {
        byte[] nonce = new byte[12];
        Buffer.BlockCopy(headerNonce, 0, nonce, 0, 12);

        byte[] idx = BitConverter.GetBytes(index);
        for (int i = 0; i < 8; i++)
            nonce[4 + i] ^= idx[i];

        return nonce;
    }

    private static int ReadChunk(FileStream fs, byte[] buffer)
    {
        int offset = 0;

        while (offset < buffer.Length)
        {
            int n = fs.Read(buffer, offset, buffer.Length - offset);
            if (n == 0) break;
            offset += n;
        }

        return offset;
    }

    private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
    {
        int readOffset = offset;
        int end = offset + count;

        while (readOffset < end)
        {
            int n = stream.Read(buffer, readOffset, end - readOffset);
            if (n == 0) throw new EndOfStreamException();
            readOffset += n;
        }
    }

    private static void ReadExactly(Stream stream, byte[] buffer)
    {
        int offset = 0;

        while (offset < buffer.Length)
        {
            int n = stream.Read(buffer, offset, buffer.Length - offset);
            if (n == 0) throw new EndOfStreamException();
            offset += n;
        }
    }

}


internal sealed class ShellDestroyForm : Form, IProgressReporter
{
    private readonly string file;
    private bool started;
    private ProgressBar progress = null!;
    private Label status = null!;
    private Label detail = null!;
    private Button cancel = null!;
    private CancellationTokenSource? cts;

    public ShellDestroyForm(string file)
    {
        L.Load();
        this.file = file;

        Text = L.T("Kalıcı Olarak Yok Et", "Permanent Delete");
        Width = 520;
        Height = 220;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        Font = new Font("Segoe UI", 9F);

        status = new Label { Left = 22, Top = 22, Width = 455, Height = 25 };
        progress = new ProgressBar { Left = 22, Top = 58, Width = 455, Height = 25, Minimum = 0, Maximum = 100 };
        detail = new Label { Left = 22, Top = 92, Width = 455, Height = 45 };
        cancel = new Button { Left = 355, Top = 145, Width = 122, Height = 32, Text = L.T("İptal", "Cancel") };

        Controls.AddRange(new Control[] { status, progress, detail, cancel });
        cancel.Click += (_, _) => cts?.Cancel();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (started) return;
        started = true;

        TopMost = true;
        Activate();
        BringToFront();

        if (!File.Exists(file) && !Directory.Exists(file))
        {
            MessageBox.Show(this,
                L.T("Dosya veya klasör bulunamadı:\n\n" + file,
                    "File or folder not found:\n\n" + file),
                L.T("Kalıcı Olarak Yok Et", "Permanent Delete"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        bool isDirectory = Directory.Exists(file);
        string itemName = Path.GetFileName(file.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string itemTypeTr = isDirectory ? "klasörü" : "dosyayı";
        string itemTypeEn = isDirectory ? "folder" : "file";

        DialogResult answer = MessageBox.Show(
            this,
            L.T($"Bu {itemTypeTr} kalıcı olarak silmek istediğinizden emin misiniz?\n\n" + itemName +
                "\n\nBu işlem geri alınamaz.",
                $"Are you sure you want to permanently delete this {itemTypeEn}?\n\n" + itemName +
                "\n\nThis operation cannot be undone."),
            L.T("Kalıcı Olarak Yok Et", "Permanent Delete"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes)
        {
            Close();
            return;
        }

        // Onaydan sonra aynı küçük pencerede gerçek ilerleme gösterilir.
        Show();
        WindowState = FormWindowState.Normal;
        TopMost = true;
        Activate();
        BringToFront();

      cts = new CancellationTokenSource();
cancel.Enabled = true;
status.Text = L.T("Hazırlanıyor...", "Preparing...");
detail.Text = Path.GetFileName(file);

int totalFiles = 0;
long totalBytes = 0;

try
{
    if (isDirectory)
    {
        string[] files = Directory
            .EnumerateFiles(file, "*", SearchOption.AllDirectories)
            .ToArray();

        totalFiles = files.Length;

        foreach (string item in files)
        {
            try
            {
                totalBytes += new FileInfo(item).Length;
            }
            catch
            {
            }
        }
    }
    else
    {
        totalFiles = 1;
        totalBytes = new FileInfo(file).Length;
    }

    await Task.Run(() => Program.DestroyPath(file, this), cts.Token);

    if (!cts.IsCancellationRequested)
    {
        progress.Value = 100;
        status.Text = L.T("Tamamlandı.", "Completed.");
        detail.Text = L.T(
            isDirectory
                ? "Klasör ve içeriği başarıyla kalıcı olarak silindi."
                : "Dosya başarıyla kalıcı olarak silindi.",
            isDirectory
                ? "Folder and its contents were permanently deleted successfully."
                : "File was permanently deleted successfully.");

        cancel.Enabled = false;

        OperationResult operationResult = new OperationResult
        {
            TotalFiles = totalFiles,
            TotalBytes = totalBytes,
            Successful = totalFiles,
            Failed = 0,
            Verified = totalFiles,
            Cancelled = false
        };

        using (OperationSummaryForm summary =
            new OperationSummaryForm(operationResult, L.English))
        {
            summary.ShowDialog(this);
        }

        Close();
    }
}
catch (OperationCanceledException)
{
	status.Text = L.T("İptal edildi.", "Cancelled.");
            detail.Text = L.T("Orijinal dosya korunmuştur.", "The original file was preserved.");
        }
        catch (Exception ex)
        {
            status.Text = L.T("İşlem başarısız.", "Operation failed.");
            detail.Text = L.T("Orijinal dosya korunmuş olabilir.", "The original file may have been preserved.");

            MessageBox.Show(this,
                L.T("İşlem başarısız oldu.\n\n" + ex.Message,
                    "Operation failed.\n\n" + ex.Message),
                L.T("Kalıcı Olarak Yok Et", "Permanent Delete"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            cts?.Dispose();
            cts = null;
            cancel.Enabled = false;
        }
    }

    public void ReportProgress(long processed, long total, TimeSpan elapsed)
    {
        if (IsDisposed) return;

        if (InvokeRequired)
        {
            BeginInvoke(() => ReportProgress(processed, total, elapsed));
            return;
        }

        int percent = total == 0 ? 100 :
            (int)CryptoCompat.Clamp(processed * 100L / total, 0, 100);

        progress.Value = percent;

        double seconds = Math.Max(elapsed.TotalSeconds, 0.001);
        double mbps = processed / 1024d / 1024d / seconds;
        long remaining = Math.Max(0, total - processed);
        double remainingSeconds = mbps <= 0 ? 0 :
            remaining / 1024d / 1024d / mbps;

        status.Text = L.T("AES-256-GCM işleniyor... " + percent + "%", "Processing AES-256-GCM... " + percent + "%");
        detail.Text = L.T(
            $"{FormatSize(processed)} / {FormatSize(total)}   •   {mbps:0.0} MB/s   •   Kalan: {FormatTime(remainingSeconds)}",
            $"{FormatSize(processed)} / {FormatSize(total)}   •   {mbps:0.0} MB/s   •   Remaining: {FormatTime(remainingSeconds)}");

        TopMost = true;
    }

    public void ReportValidation(long current, long total, TimeSpan elapsed)
    {
        if (IsDisposed) return;

        if (InvokeRequired)
        {
            BeginInvoke(() => ReportValidation(current, total, elapsed));
            return;
        }

        int percent = total == 0 ? 100 :
            (int)CryptoCompat.Clamp(current * 100L / total, 0, 100);

        progress.Value = percent;
        status.Text = L.T("Şifreli veri doğrulanıyor... " + percent + "%", "Verifying encrypted data... " + percent + "%");
        detail.Text = L.T($"{current:N0} / {total:N0} parça doğrulanıyor...", $"{current:N0} / {total:N0} chunks verifying...");
        TopMost = true;
    }

    public void ReportFinalizing()
    {
        if (InvokeRequired)
        {
            BeginInvoke(ReportFinalizing);
            return;
        }

        progress.Value = 100;
        status.Text = L.T("Sonlandırılıyor...", "Finalizing...");
        detail.Text = L.T("Doğrulama tamamlandı.", "Verification completed.");
        TopMost = true;
    }

    public void ThrowIfCancellationRequested()
    {
        cts?.Token.ThrowIfCancellationRequested();
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int i = 0;
        while (value >= 1024 && i < units.Length - 1)
        {
            value /= 1024;
            i++;
        }
        return $"{value:0.##} {units[i]}";
    }

    private static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "--";
        TimeSpan t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} sa {t.Minutes:00} dk";
        if (t.TotalMinutes >= 1) return $"{t.Minutes} dk {t.Seconds:00} sn";
        return L.T($"{t.Seconds} sn", $"{t.Seconds} sec");
    }
}

internal sealed class MainForm : Form, IProgressReporter
{
    private readonly Label fileLabel = new();
    private readonly Label sizeLabel = new();
    private readonly Label statusLabel = new();
    private readonly Label detailLabel = new();
    private readonly Label registryLabel = new();
    private readonly Label titleLabel = new();
    private readonly Label subtitleLabel = new();
    private readonly Label versionLabel = new();
    private readonly LinkLabel websiteLink = new();
    private readonly Button selectFileButton = new();
    private readonly Button selectFolderButton = new();
    private readonly Button destroyButton = new();
    private readonly Button cancelButton = new();
    private readonly Button registryButton = new();
    private readonly Button languageButton = new();
    private readonly Button updateButton = new();
    private readonly Button settingsButton = new();
    private readonly PictureBox logo = new();
    private readonly Panel fileCard = new();
    private readonly Panel processCard = new();
    private readonly Panel progressTrack = new();
    private readonly Panel progressFill = new();
    private readonly Panel footerLine = new();
    private readonly ToolTip registryToolTip = new();

    private readonly List<string> selectedItems = new();
    private CancellationTokenSource? cts;
    private bool running;
    private bool updateCheckRunning;

    private static readonly Color BackgroundColor = Color.FromArgb(244, 247, 250);
    private static readonly Color CardColor = Color.White;
    private static readonly Color CardBorder = Color.FromArgb(214, 222, 231);
    private static readonly Color TextPrimary = Color.FromArgb(31, 42, 52);
    private static readonly Color TextSecondary = Color.FromArgb(101, 115, 130);
    private static readonly Color Accent = Color.FromArgb(25, 150, 220);
    private static readonly Color AccentDark = Color.FromArgb(18, 112, 168);
    private static readonly Color Danger = Color.FromArgb(211, 63, 63);
    private static readonly Color DangerHover = Color.FromArgb(226, 78, 78);

    internal bool IsCancellationRequested => cts?.IsCancellationRequested == true;

    public MainForm(string? initialFile)
    {
        Text = "VoidErase";
        ClientSize = new Size(720, 500);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        BackColor = BackgroundColor;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9F);

        try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        BuildHeader();
        BuildFileCard();
        BuildProcessCard();
        BuildActions();
        BuildFooter();

        if (!string.IsNullOrWhiteSpace(initialFile))
            SetSelection(new[] { initialFile });
        else
            SetIdle();

        UpdateRegistryStatus();

        Shown += async (_, _) =>
        {
            if (L.AutoUpdate)
                await CheckForUpdatesAsync(false);
        };
    }

    private void BuildHeader()
    {
        logo.SetBounds(24, 20, 54, 54);
        logo.SizeMode = PictureBoxSizeMode.Zoom;
        logo.BackColor = Color.Transparent;
        try
        {
            using Icon? appIcon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            logo.Image = appIcon?.ToBitmap();
        }
        catch { }

        titleLabel.SetBounds(88, 18, 280, 34);
        titleLabel.Text = "VoidErase";
        titleLabel.Font = new Font("Segoe UI", 20F, FontStyle.Bold);
        titleLabel.ForeColor = TextPrimary;

        subtitleLabel.SetBounds(90, 51, 340, 22);
        subtitleLabel.Text = L.T("Dosyalarınızı kalıcı olarak silin.", "Permanently erase your files.");
        subtitleLabel.ForeColor = TextSecondary;

        settingsButton.SetBounds(446, 22, 34, 34);
        settingsButton.Text = "⚙";
        settingsButton.Font = new Font("Segoe UI Symbol", 12F);
        StyleButton(settingsButton, CardColor, TextPrimary, false);
        settingsButton.FlatAppearance.BorderColor = CardBorder;
        settingsButton.Click += (_, _) => OpenSettings();
        registryToolTip.SetToolTip(settingsButton, L.T("Ayarlar", "Settings"));

        updateButton.SetBounds(486, 22, 100, 34);
        updateButton.Text = L.T("Güncelleme", "Update");
        StyleButton(updateButton, CardColor, TextPrimary, false);
        updateButton.FlatAppearance.BorderColor = CardBorder;
        updateButton.Click += async (_, _) => await CheckForUpdatesAsync(true);

        languageButton.SetBounds(592, 22, 104, 34);
        languageButton.Text = L.T("English", "Türkçe");
        StyleButton(languageButton, Accent, Color.White, true);
        languageButton.Click += (_, _) =>
        {
            L.SetLanguage(!L.Turkish);
            Program.UpdateContextMenuLanguage();
            UpdateTexts();
        };

        Controls.AddRange(new Control[] { logo, titleLabel, subtitleLabel, settingsButton, updateButton, languageButton });
    }

    private void BuildFileCard()
    {
        fileCard.SetBounds(24, 94, 672, 116);
        fileCard.BackColor = CardColor;
        fileCard.BorderStyle = BorderStyle.FixedSingle;

        Label heading = new()
        {
            Text = L.T("DOSYA / KLASÖR", "FILE / FOLDER"),
            ForeColor = Accent,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
        };
        heading.SetBounds(18, 14, 200, 20);

        fileLabel.SetBounds(18, 39, 500, 27);
        fileLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        fileLabel.ForeColor = TextPrimary;
        fileLabel.AutoEllipsis = true;

        sizeLabel.SetBounds(18, 70, 500, 20);
        sizeLabel.ForeColor = TextSecondary;

        selectFileButton.SetBounds(532, 30, 120, 34);
        selectFileButton.Text = L.T("Dosya Seç", "Select File");
        StyleButton(selectFileButton, Accent, Color.White, true);
        selectFileButton.Click += (_, _) => ChooseFiles();

        selectFolderButton.SetBounds(532, 68, 120, 30);
        selectFolderButton.Text = L.T("Klasör Seç", "Select Folder");
        StyleButton(selectFolderButton, CardColor, TextPrimary, false);
        selectFolderButton.FlatAppearance.BorderColor = CardBorder;
        selectFolderButton.Click += (_, _) => ChooseFolder();

        fileCard.Controls.AddRange(new Control[] { heading, fileLabel, sizeLabel, selectFileButton, selectFolderButton });
        Controls.Add(fileCard);
    }

    private void BuildProcessCard()
    {
        processCard.SetBounds(24, 222, 672, 126);
        processCard.BackColor = CardColor;
        processCard.BorderStyle = BorderStyle.FixedSingle;

        Label heading = new()
        {
            Text = L.T("İŞLEM", "PROCESS"),
            ForeColor = Accent,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
        };
        heading.SetBounds(18, 14, 180, 20);

        statusLabel.SetBounds(18, 39, 630, 24);
        statusLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        statusLabel.ForeColor = TextPrimary;

        detailLabel.SetBounds(18, 65, 630, 20);
        detailLabel.ForeColor = TextSecondary;
        detailLabel.AutoEllipsis = true;

        progressTrack.SetBounds(18, 92, 630, 14);
        progressTrack.BackColor = Color.FromArgb(231, 236, 241);
        progressFill.SetBounds(0, 0, 0, 14);
        progressFill.BackColor = Accent;
        progressTrack.Controls.Add(progressFill);

        processCard.Controls.AddRange(new Control[] { heading, statusLabel, detailLabel, progressTrack });
        Controls.Add(processCard);
    }

    private void BuildActions()
    {
        destroyButton.SetBounds(24, 362, 322, 42);
        destroyButton.Text = L.T("KALICI OLARAK SİL", "PERMANENT DELETE");
        destroyButton.Enabled = false;
        StyleButton(destroyButton, Danger, Color.White, true);
        destroyButton.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        destroyButton.FlatAppearance.MouseOverBackColor = DangerHover;
        destroyButton.Click += async (_, _) => await StartDestroyAsync();

        cancelButton.SetBounds(354, 362, 342, 42);
        cancelButton.Text = L.T("İptal", "Cancel");
        StyleButton(cancelButton, CardColor, TextPrimary, false);
        cancelButton.FlatAppearance.BorderColor = CardBorder;
        cancelButton.Click += (_, _) => { if (running) cts?.Cancel(); };

        registryButton.SetBounds(24, 414, 322, 34);
        StyleButton(registryButton, CardColor, TextPrimary, false);
        registryButton.FlatAppearance.BorderColor = CardBorder;
        registryButton.Click += (_, _) => ToggleRegistry();

        Label hint = new()
        {
            Text = L.T("Güvenli silme • AES-256-GCM", "Secure erasure • AES-256-GCM"),
            ForeColor = TextSecondary,
            TextAlign = ContentAlignment.MiddleRight
        };
        hint.SetBounds(354, 414, 342, 34);

        Controls.AddRange(new Control[] { destroyButton, cancelButton, registryButton, hint });
    }

    private void BuildFooter()
    {
        footerLine.SetBounds(24, 456, 672, 1);
        footerLine.BackColor = CardBorder;

        versionLabel.SetBounds(24, 463, 140, 22);
        versionLabel.Text = Program.DisplayVersion;
        versionLabel.ForeColor = TextSecondary;
        versionLabel.TextAlign = ContentAlignment.MiddleLeft;

        websiteLink.SetBounds(556, 463, 140, 22);
        websiteLink.Text = "tuncay.net.tr";
        websiteLink.TextAlign = ContentAlignment.MiddleRight;
        websiteLink.LinkColor = Accent;
        websiteLink.ActiveLinkColor = AccentDark;
        websiteLink.VisitedLinkColor = Accent;
        websiteLink.Cursor = Cursors.Hand;
        websiteLink.LinkBehavior = LinkBehavior.HoverUnderline;
        websiteLink.LinkClicked += (_, _) => OpenWebsite();

        registryLabel.SetBounds(190, 463, 340, 22);
        registryLabel.TextAlign = ContentAlignment.MiddleCenter;
        registryLabel.ForeColor = TextSecondary;
        registryLabel.AutoEllipsis = true;
        registryToolTip.SetToolTip(registryLabel, "");

        Controls.AddRange(new Control[] { footerLine, versionLabel, registryLabel, websiteLink });
    }

    private static void StyleButton(Button button, Color backColor, Color foreColor, bool accent)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = accent ? backColor : CardBorder;
        button.FlatAppearance.MouseDownBackColor = accent ? AccentDark : Color.FromArgb(238, 242, 246);
        button.FlatAppearance.MouseOverBackColor = accent ? Color.FromArgb(48, 166, 226) : Color.FromArgb(242, 245, 248);
        button.BackColor = backColor;
        button.ForeColor = foreColor;
        button.UseVisualStyleBackColor = false;
        button.Cursor = Cursors.Hand;
    }

    private void UpdateTexts()
    {
        subtitleLabel.Text = L.T("Dosyalarınızı kalıcı olarak silin.", "Permanently erase your files.");
        languageButton.Text = L.T("English", "Türkçe");
        updateButton.Text = L.T("Güncelleme", "Update");
        destroyButton.Text = L.T("KALICI OLARAK SİL", "PERMANENT DELETE");
        cancelButton.Text = L.T("İptal", "Cancel");
        registryButton.Text = RegistryIsInstalled()
            ? L.T("Sağ Tık Menüsünü KALDIR", "REMOVE CONTEXT MENU")
            : L.T("Sağ Tık Menüsünü ETKİNLEŞTİR", "ENABLE CONTEXT MENU");
        ((Label)fileCard.Controls[0]).Text = L.T("DOSYA / KLASÖR", "FILE / FOLDER");
        selectFileButton.Text = L.T("Dosya Seç", "Select File");
        selectFolderButton.Text = L.T("Klasör Seç", "Select Folder");
        ((Label)processCard.Controls[0]).Text = L.T("İŞLEM", "PROCESS");

        if (selectedItems.Count == 0) SetIdle();
        else RefreshSelectionSummary();
        UpdateRegistryStatus();
    }

    private void SetIdle()
    {
        fileLabel.Text = L.T("Dosya veya klasör seçilmedi.", "No file or folder selected.");
        sizeLabel.Text = "";
        statusLabel.Text = L.T("Hazır", "Ready");
        detailLabel.Text = "";
        SetProgress(0);
        destroyButton.Enabled = false;
        destroyButton.ForeColor = Color.FromArgb(145, 150, 157);
    }

    private void SetSelection(IEnumerable<string> paths)
    {
        selectedItems.Clear();
        foreach (string path in paths.Where(File.Exists))
            selectedItems.Add(path);
        RefreshSelectionSummary();
    }

    private void SetFolderSelection(string folder)
    {
        selectedItems.Clear();
        if (Directory.Exists(folder)) selectedItems.Add(folder);
        RefreshSelectionSummary();
    }

    private void RefreshSelectionSummary()
    {
        if (selectedItems.Count == 0) { SetIdle(); return; }

        long total = 0;
        long count = 0;
        foreach (string item in selectedItems)
        {
            if (File.Exists(item)) { total += new FileInfo(item).Length; count++; }
            else if (Directory.Exists(item))
            {
                foreach (string file in Directory.EnumerateFiles(item, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(file).Length; count++; } catch { }
                }
            }
        }

        string name = selectedItems.Count == 1 ? Path.GetFileName(selectedItems[0].TrimEnd(Path.DirectorySeparatorChar)) : L.T($"{selectedItems.Count} öğe seçildi", $"{selectedItems.Count} items selected");
        fileLabel.Text = name;
        sizeLabel.Text = L.T($"Toplam: {count:N0} dosya • {FormatSize(total)}", $"Total: {count:N0} files • {FormatSize(total)}");
        statusLabel.Text = L.T("Yok etmeye hazır.", "Ready to erase.");
        detailLabel.Text = selectedItems.Count == 1 ? selectedItems[0] : L.T("Birden fazla öğe seçildi.", "Multiple items selected.");
        detailLabel.ForeColor = TextSecondary;
        SetProgress(0);
        destroyButton.Enabled = count > 0;
        destroyButton.ForeColor = destroyButton.Enabled ? Color.White : Color.FromArgb(145, 150, 157);
    }

    private void ChooseFiles()
    {
        if (running) return;
        using OpenFileDialog dlg = new()
        {
            Title = L.T("Kalıcı olarak yok edilecek dosyaları seçin", "Select files to permanently erase"),
            CheckFileExists = true,
            Multiselect = true,
            RestoreDirectory = true
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) SetSelection(dlg.FileNames);
    }

    private void ChooseFolder()
    {
        if (running) return;
        using FolderBrowserDialog dlg = new()
        {
            Description = L.T("Kalıcı olarak yok edilecek klasörü seçin", "Select a folder to permanently erase")
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) SetFolderSelection(dlg.SelectedPath);
    }

    private List<string> ExpandSelectedFiles()
    {
        var files = new List<string>();
        foreach (string item in selectedItems)
        {
            if (File.Exists(item)) files.Add(item);
            else if (Directory.Exists(item))
                files.AddRange(Directory.EnumerateFiles(item, "*", SearchOption.AllDirectories));
        }
        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task StartDestroyAsync()
    {
        if (running || selectedItems.Count == 0) return;

        if (L.ConfirmBeforeErase)
        {
            DialogResult answer = MessageBox.Show(
                this,
                L.T("Bu işlem geri alınamaz.\n\nSeçilen dosyalar AES-256-GCM ile işlenecek ve doğrulama tamamlandıktan sonra orijinalleri silinecektir.\n\nDevam etmek istiyor musunuz?",
                    "This operation cannot be undone.\n\nSelected files will be processed with AES-256-GCM and originals will be deleted only after verification.\n\nContinue?"),
                L.T("Kalıcı Olarak Yok Et", "Permanent Delete"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
        }

        List<string> files;
        try { files = ExpandSelectedFiles(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "VoidErase", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (files.Count == 0) { SetIdle(); return; }

        running = true;
        cts = new CancellationTokenSource();
        SetControlsRunning(true);
        DateTime startedAt = DateTime.Now;
        int success = 0;
		long totalBytes = 0;
int verified = 0;


        try
        {
            for (int i = 0; i < files.Count; i++)
            {
                cts.Token.ThrowIfCancellationRequested();
                string file = files[i];
                if (!File.Exists(file)) continue;
                long fileSize = new FileInfo(file).Length;
                statusLabel.Text = L.T($"İşleniyor... {i + 1}/{files.Count}", $"Processing... {i + 1}/{files.Count}");
                detailLabel.Text = Path.GetFileName(file);
                detailLabel.ForeColor = TextSecondary;
                SetProgress(0);
                await Task.Run(() => Program.DestroyFile(file, this), cts.Token);
               success++;
verified++;
totalBytes += fileSize;
                HistoryStore.Append(file, fileSize, "SUCCESS");
            }

            foreach (string folder in selectedItems.Where(Directory.Exists))
            {
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                    Directory.Delete(folder, false);
            }

            SetProgress(100);
            statusLabel.Text = L.T("Tamamlandı.", "Completed.");
            detailLabel.Text = L.T($"{success:N0} dosya başarıyla yok edildi.", $"{success:N0} files were successfully erased.");
            detailLabel.ForeColor = Color.FromArgb(30, 145, 88);
            selectedItems.Clear();

            OperationResult result = new OperationResult
{
    TotalFiles = files.Count,
    TotalBytes = totalBytes,
    Successful = success,
    Failed = files.Count - success,
    Verified = verified,
    Cancelled = false
};

ShowOperationSummary(result);
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = L.T("İptal edildi.", "Cancelled.");
            detailLabel.Text = L.T("Tamamlanan dosyalar işlendi; kalan dosyalar korunmuştur.", "Completed files were processed; remaining files were preserved.");
            detailLabel.ForeColor = Color.FromArgb(176, 125, 20);
            HistoryStore.AppendBatch("CANCELLED", success);
        }
        catch (Exception ex)
        {
            statusLabel.Text = L.T("Hata.", "Error.");
            detailLabel.Text = L.T("İşlem durduruldu; kalan dosyalar korunmuştur.", "Operation stopped; remaining files were preserved.");
            detailLabel.ForeColor = Color.FromArgb(190, 55, 55);
            MessageBox.Show(this, L.T("İşlem başarısız oldu.\n\n" + ex.Message, "Operation failed.\n\n" + ex.Message), "VoidErase", MessageBoxButtons.OK, MessageBoxIcon.Error);
            HistoryStore.AppendBatch("FAILED", success);
        }
        finally
        {
            cts?.Dispose(); cts = null; running = false; SetControlsRunning(false);
            if (selectedItems.Count == 0) SetIdle(); else RefreshSelectionSummary();
            UpdateRegistryStatus();
        }
    }

    private void SetControlsRunning(bool active)
    {
        selectFileButton.Enabled = !active;
        selectFolderButton.Enabled = !active;
        destroyButton.Enabled = false;
        registryButton.Enabled = !active;
        updateButton.Enabled = !active;
        settingsButton.Enabled = !active;
        languageButton.Enabled = !active;
        cancelButton.Enabled = active;
    }

    private void ToggleRegistry()
    {
        if (running) return;
        try
        {
            if (RegistryIsInstalled())
            {
                if (MessageBox.Show(this, L.T("Sağ tık menüsü kaldırılacak.\n\nDevam?", "The context-menu entry will be removed.\n\nContinue?"), L.T("Sağ Tık Menüsü", "Context Menu"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                Program.UninstallContextMenu();
            }
            else
            {
                if (!Program.InstallContextMenu(false)) throw new InvalidOperationException(L.T("Registry kaydı doğrulanamadı.", "The Registry entry could not be verified."));
                MessageBox.Show(this, L.T("Sağ tık menüsü etkinleştirildi.", "Context menu enabled."), L.T("Sağ Tık Menüsü", "Context Menu"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            UpdateRegistryStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L.T("Registry işlemi başarısız:\n\n" + ex.Message, "Registry operation failed:\n\n" + ex.Message), "VoidErase", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool RegistryIsInstalled() => IsContextMenuKeyInstalled(Program.MenuKey) || IsContextMenuKeyInstalled(Program.DirectoryMenuKey) || IsContextMenuKeyInstalled(Program.LegacyMenuKey) || IsContextMenuKeyInstalled(Program.LegacyDirectoryMenuKey);

    private static bool IsContextMenuKeyInstalled(string menuKey)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(menuKey);
        if (key == null) return false;
        using RegistryKey? commandKey = key.OpenSubKey("command");
        string? command = commandKey?.GetValue("") as string;
        return !string.IsNullOrWhiteSpace(command) && command.IndexOf("--destroy", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void UpdateRegistryStatus()
    {
        bool installed = RegistryIsInstalled();
        registryButton.Text = installed ? L.T("Sağ Tık Menüsünü KALDIR", "REMOVE CONTEXT MENU") : L.T("Sağ Tık Menüsünü ETKİNLEŞTİR", "ENABLE CONTEXT MENU");
        registryLabel.Text = installed ? L.T("✓ Sağ tık menüsü etkin.", "✓ Context menu enabled.") : L.T("Sağ tık menüsü etkin değil.", "Context menu is not enabled.");
        registryLabel.ForeColor = installed ? Color.FromArgb(30, 145, 88) : TextSecondary;
        registryToolTip.SetToolTip(registryLabel, installed ? Program.GetExePath() : L.T("Kurulu değil.", "Not installed."));
    }

    private void OpenSettings()
    {
        using SettingsForm settings = new();
        if (settings.ShowDialog(this) == DialogResult.OK)
        {
            UpdateTexts();
            if (L.AutoUpdate && !updateCheckRunning) _ = CheckForUpdatesAsync(false);
        }
    }

    private void OpenWebsite()
    {
        try { Process.Start(new ProcessStartInfo { FileName = "https://tuncay.net.tr", UseShellExecute = true }); }
        catch { }
    }

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        if (updateCheckRunning || running) return;
        updateCheckRunning = true;
        string original = updateButton.Text;
        if (interactive) { updateButton.Enabled = false; updateButton.Text = L.T("Kontrol ediliyor...", "Checking..."); }
        try
        {
            using HttpClient client = new();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("VoidErase/" + Program.AppVersion);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using HttpResponseMessage response = await client.GetAsync("https://api.github.com/repos/tuncaycandan/VoidErase/releases/latest");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                if (interactive) MessageBox.Show(this, L.T("GitHub'da henüz yayınlanmış bir sürüm bulunamadı.", "No published GitHub release was found yet."), L.T("Güncelleme", "Update"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync();
            string tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
            string latestText = tag.TrimStart('v', 'V');
            if (!Version.TryParse(latestText, out Version? latest) || !Version.TryParse(Program.AppVersion, out Version? current))
                throw new InvalidOperationException(L.T("Sürüm bilgisi geçersiz.", "Invalid version information."));
            if (latest <= current)
            {
                if (interactive) MessageBox.Show(this, L.T($"VoidErase güncel.\n\nMevcut sürüm: {Program.DisplayVersion}", $"VoidErase is up to date.\n\nCurrent version: {Program.DisplayVersion}"), L.T("Güncelleme", "Update"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string? url = null; string? digest = null;
            Match assetMatch = Regex.Match(json,
                "\"name\"\\s*:\\s*\"VoidErase\\.exe\".*?\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"(?:.*?\"digest\"\\s*:\\s*\"([^\"]+)\")?",
                RegexOptions.Singleline);
            if (assetMatch.Success)
            {
                url = assetMatch.Groups[1].Value;
                digest = assetMatch.Groups[2].Success ? assetMatch.Groups[2].Value : null;
            }
            if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException(L.T("Release içinde VoidErase.exe bulunamadı.", "VoidErase.exe was not found in the release."));
            if (!interactive) interactive = MessageBox.Show(this, L.T($"Yeni sürüm v{latest} bulundu. Güncellensin mi?", $"New version v{latest} is available. Update now?"), L.T("Güncelleme", "Update"), MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes;
            else interactive = MessageBox.Show(this, L.T($"Yeni sürüm v{latest} bulundu.\n\nŞimdi indirip yüklemek ister misiniz?", $"New version v{latest} is available.\n\nDownload and install it now?"), L.T("Güncelleme", "Update"), MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes;
            if (!interactive) return;

            updateButton.Text = L.T("İndiriliyor...", "Downloading...");
            string target = Program.GetExePath();
            string temp = target + ".update";
            if (File.Exists(temp)) File.Delete(temp);
            using HttpResponseMessage download = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            download.EnsureSuccessStatusCode();
            using Stream source = await download.Content.ReadAsStreamAsync();
            using FileStream dest = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(dest); await dest.FlushAsync();
            if (!string.IsNullOrWhiteSpace(digest) && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                using FileStream verify = new(temp, FileMode.Open, FileAccess.Read, FileShare.Read);
                string actual;
                using (SHA256 sha = SHA256.Create())
                    actual = CryptoCompat.ToHexString(sha.ComputeHash(verify));
                if (!string.Equals(actual, digest.Substring(7).Trim(), StringComparison.OrdinalIgnoreCase)) throw new CryptographicException(L.T("İndirilen dosyanın SHA-256 doğrulaması başarısız oldu.", "Downloaded file SHA-256 verification failed."));
            }
            string script = Path.Combine(Path.GetTempPath(), "VoidEraseUpdate_" + Guid.NewGuid().ToString("N") + ".cmd");
            File.WriteAllText(script, $"@echo off\r\nset TARGET={QuoteForCmd(target)}\r\nset NEWFILE={QuoteForCmd(temp)}\r\nset PID={Process.GetCurrentProcess().Id}\r\n:wait\r\ntasklist /FI \"PID eq %PID%\" | findstr /C:\"%PID%\" >NUL\r\nif not errorlevel 1 (timeout /t 1 /nobreak >NUL & goto wait)\r\n:replace\r\ndel /f /q \"%TARGET%\" >NUL 2>&1\r\nif exist \"%TARGET%\" (timeout /t 1 /nobreak >NUL & goto replace)\r\nmove /y \"%NEWFILE%\" \"%TARGET%\" >NUL 2>&1\r\nif not exist \"%TARGET%\" exit /b 1\r\nstart \"\" \"%TARGET%\"\r\ndel /f /q \"%~f0\" >NUL 2>&1\r\n");
            Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/c start \"\" /min \"{script}\"", UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            Application.Exit();
        }
        catch (Exception ex)
        {
            if (interactive) MessageBox.Show(this, L.T("Güncelleme başarısız oldu.\n\n" + ex.Message, "Update failed.\n\n" + ex.Message), L.T("Güncelleme", "Update"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            updateCheckRunning = false;
            if (!IsDisposed) { updateButton.Enabled = true; updateButton.Text = original; }
        }
    }

    private static string QuoteForCmd(string value) => value.Replace("\"", "\"\"");

    private void SetProgress(int value)
    {
        int percent = CryptoCompat.Clamp(value, 0, 100);
        progressFill.Width = (int)Math.Round(progressTrack.Width * percent / 100d);
        progressFill.Left = 0;
    }

    public void ReportProgress(long processed, long total, TimeSpan elapsed)
    {
        if (InvokeRequired) { BeginInvoke(() => ReportProgress(processed, total, elapsed)); return; }
        int percent = total == 0 ? 100 : (int)CryptoCompat.Clamp(processed * 100L / total, 0, 100);
        SetProgress(percent);
        double seconds = Math.Max(elapsed.TotalSeconds, 0.001);
        double mbps = processed / 1024d / 1024d / seconds;
        long remaining = Math.Max(0, total - processed);
        double remainingSeconds = mbps <= 0 ? 0 : remaining / 1024d / 1024d / mbps;
        statusLabel.Text = L.T($"AES-256-GCM işleniyor... {percent}%", $"Processing AES-256-GCM... {percent}%");
        detailLabel.Text = L.T($"{FormatSize(processed)} / {FormatSize(total)} • {mbps:0.0} MB/s • Kalan: {FormatTime(remainingSeconds)}", $"{FormatSize(processed)} / {FormatSize(total)} • {mbps:0.0} MB/s • Remaining: {FormatTime(remainingSeconds)}");
    }

    public void ReportValidation(long current, long total, TimeSpan elapsed)
    {
        if (InvokeRequired) { BeginInvoke(() => ReportValidation(current, total, elapsed)); return; }
        int percent = total == 0 ? 100 : (int)CryptoCompat.Clamp(current * 100L / total, 0, 100);
        SetProgress(percent);
        statusLabel.Text = L.T($"Şifreli veri doğrulanıyor... {percent}%", $"Verifying encrypted data... {percent}%");
        detailLabel.Text = L.T($"{current:N0} / {total:N0} parça doğrulandı.", $"{current:N0} / {total:N0} chunks verified.");
    }

    public void ReportFinalizing()
    {
        if (InvokeRequired) { BeginInvoke(ReportFinalizing); return; }
        SetProgress(100);
        statusLabel.Text = L.T("Sonlandırılıyor...", "Finalizing...");
        detailLabel.Text = L.T("Doğrulama tamamlandı.", "Verification completed.");
    }

    public void ThrowIfCancellationRequested() => cts?.Token.ThrowIfCancellationRequested();

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" }; double value = bytes; int i = 0;
        while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
        return $"{value:0.##} {units[i]}";
    }

    private static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "--";
        TimeSpan t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (t.TotalHours >= 1) return L.T($"{(int)t.TotalHours} sa {t.Minutes:00} dk", $"{(int)t.TotalHours} hr {t.Minutes:00} min");
        if (t.TotalMinutes >= 1) return L.T($"{t.Minutes} dk {t.Seconds:00} sn", $"{t.Minutes} min {t.Seconds:00} sec");
        return L.T($"{t.Seconds} sn", $"{t.Seconds} sec");
    }

    private void ShowOperationSummary(OperationResult result)
    {
        using var dlg = new OperationSummaryForm(result, L.English);
        dlg.ShowDialog(this);
    }
}

internal sealed class SettingsForm : Form
{
    private readonly CheckBox confirm = new();
    private readonly CheckBox autoUpdate = new();
    private readonly CheckBox protectSystem = new();
    private readonly CheckBox keepLogs = new();
    private readonly ComboBox language = new();

    public SettingsForm()
    {
        Text = L.T("Ayarlar", "Settings");
        ClientSize = new Size(390, 300);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(244, 247, 250);

        Label title = new() { Text = L.T("VoidErase Ayarları", "VoidErase Settings"), Font = new Font("Segoe UI", 14F, FontStyle.Bold), ForeColor = Color.FromArgb(31,42,52) };
        title.SetBounds(24, 20, 330, 28);
        Label langLabel = new() { Text = L.T("Dil", "Language"), ForeColor = Color.FromArgb(101,115,130) };
        langLabel.SetBounds(24, 65, 100, 22);
        language.SetBounds(145, 62, 210, 28);
        language.DropDownStyle = ComboBoxStyle.DropDownList;
        language.Items.AddRange(new object[] { "Türkçe", "English" });
        language.SelectedIndex = L.Turkish ? 0 : 1;

        confirm.Text = L.T("Silmeden önce onay iste", "Ask for confirmation before erasing");
        confirm.Checked = L.ConfirmBeforeErase; confirm.SetBounds(24, 108, 330, 24);
        autoUpdate.Text = L.T("Başlangıçta güncellemeleri kontrol et", "Check for updates at startup");
        autoUpdate.Checked = L.AutoUpdate; autoUpdate.SetBounds(24, 140, 330, 24);

        protectSystem.Text = L.T("Windows sistem klasörlerini koru", "Protect Windows system folders");
        protectSystem.Checked = VoidEraseSettings.ProtectSystemPaths; protectSystem.SetBounds(24, 172, 330, 24);
        keepLogs.Text = L.T("İşlem günlüklerini tut", "Keep operation logs");
        keepLogs.Checked = VoidEraseSettings.KeepLogs; keepLogs.SetBounds(24, 204, 330, 24);

        Button ok = new() { Text = "OK" }; ok.SetBounds(190, 250, 80, 32); ok.DialogResult = DialogResult.OK;
        Button cancel = new() { Text = L.T("İptal", "Cancel") }; cancel.SetBounds(280, 250, 80, 32); cancel.DialogResult = DialogResult.Cancel;
        AcceptButton = ok; CancelButton = cancel;
        Controls.AddRange(new Control[] { title, langLabel, language, confirm, autoUpdate, protectSystem, keepLogs, ok, cancel });
        Shown += (_, _) => { };
        FormClosing += (_, _) =>
        {
            if (DialogResult != DialogResult.OK) return;
            L.SetLanguage(language.SelectedIndex == 0);
            L.SaveSettings(confirm.Checked, autoUpdate.Checked);
            VoidEraseSettings.ProtectSystemPaths = protectSystem.Checked;
            VoidEraseSettings.KeepLogs = keepLogs.Checked;
            Program.UpdateContextMenuLanguage();
        };
    }
}

internal static class HistoryStore
{
    private static readonly object Sync = new();
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoidErase", "history.log");

    public static void Append(string path, long size, string status)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                string name = Path.GetFileName(path);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}|{status}|{name}|{size}\n");
            }
        }
        catch { }
    }

    public static void AppendBatch(string status, int count)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}|{status}|{count} files|0\n");
            }
        }
        catch { }
    }
}

internal static class ShellRefresh
{
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(
        uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    public static void Notify()
    {
        SHChangeNotify(
            0x08000000,
            0,
            IntPtr.Zero,
            IntPtr.Zero);
    }
}

