using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

internal sealed class AesGcmCompat : IDisposable
{
    private const string BCRYPT_AES_ALGORITHM = "AES";
    private const string BCRYPT_CHAINING_MODE = "ChainingMode";
    private const string BCRYPT_CHAIN_MODE_GCM = "ChainingModeGCM";
    private const int BCRYPT_SUCCESS = 0;

    private readonly IntPtr algorithmHandle;
    private readonly IntPtr keyHandle;
    private byte[] key;
    private byte[] keyObject;
    private bool disposed;

    [StructLayout(LayoutKind.Sequential)]
    private struct BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO
    {
        public int cbSize;
        public int dwInfoVersion;
        public IntPtr pbNonce;
        public int cbNonce;
        public IntPtr pbAuthData;
        public int cbAuthData;
        public IntPtr pbTag;
        public int cbTag;
        public IntPtr pbMacContext;
        public int cbMacContext;
        public int cbAAD;
        public long cbData;
        public int dwFlags;
    }

    [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
    private static extern int BCryptOpenAlgorithmProvider(
        out IntPtr phAlgorithm,
        string pszAlgId,
        string pszImplementation,
        int dwFlags);

    [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
    private static extern int BCryptSetProperty(
        IntPtr hObject,
        string pszProperty,
        byte[] pbInput,
        int cbInput,
        int dwFlags);

    [DllImport("bcrypt.dll")]
    private static extern int BCryptGetProperty(
        IntPtr hObject,
        string pszProperty,
        byte[] pbOutput,
        int cbOutput,
        out int pcbResult,
        int dwFlags);

    [DllImport("bcrypt.dll")]
    private static extern int BCryptGenerateSymmetricKey(
        IntPtr hAlgorithm,
        out IntPtr phKey,
        IntPtr pbKeyObject,
        int cbKeyObject,
        byte[] pbSecret,
        int cbSecret,
        int dwFlags);

    [DllImport("bcrypt.dll")]
    private static extern int BCryptEncrypt(
        IntPtr hKey,
        byte[] pbInput,
        int cbInput,
        ref BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO pPaddingInfo,
        byte[] pbIV,
        int cbIV,
        byte[] pbOutput,
        int cbOutput,
        out int pcbResult,
        int dwFlags);

    [DllImport("bcrypt.dll")]
    private static extern int BCryptDecrypt(
        IntPtr hKey,
        byte[] pbInput,
        int cbInput,
        ref BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO pPaddingInfo,
        byte[] pbIV,
        int cbIV,
        byte[] pbOutput,
        int cbOutput,
        out int pcbResult,
        int dwFlags);

    [DllImport("bcrypt.dll")]
    private static extern int BCryptDestroyKey(IntPtr hKey);

    [DllImport("bcrypt.dll")]
    private static extern int BCryptCloseAlgorithmProvider(IntPtr hAlgorithm, int dwFlags);

    public AesGcmCompat(byte[] key)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        if (key.Length != 16 && key.Length != 24 && key.Length != 32)
            throw new ArgumentException("AES key must be 128, 192, or 256 bits.", nameof(key));

        this.key = (byte[])key.Clone();

        int status = BCryptOpenAlgorithmProvider(
            out algorithmHandle,
            BCRYPT_AES_ALGORITHM,
            null,
            0);
        Check(status, "BCryptOpenAlgorithmProvider");

        try
        {
            // Some Windows 10 CNG providers return STATUS_NOT_SUPPORTED
            // (0xC00000BB) for the ObjectLength property. Microsoft documents
            // that BCryptGenerateSymmetricKey can allocate the key object when
            // pbKeyObject is NULL and cbKeyObject is zero (Windows 7+).
            byte[] mode = System.Text.Encoding.Unicode.GetBytes(BCRYPT_CHAIN_MODE_GCM + "\0");
            status = BCryptSetProperty(algorithmHandle, BCRYPT_CHAINING_MODE, mode, mode.Length, 0);
            Check(status, "BCryptSetProperty(ChainingModeGCM)");

            status = BCryptGenerateSymmetricKey(
                algorithmHandle,
                out keyHandle,
                IntPtr.Zero,
                0,
                this.key,
                this.key.Length,
                0);
            Check(status, "BCryptGenerateSymmetricKey");
        }
        catch
        {
            BCryptCloseAlgorithmProvider(algorithmHandle, 0);
            algorithmHandle = IntPtr.Zero;
            CryptoCompat.ZeroMemory(this.key);
            this.key = null;
            CryptoCompat.ZeroMemory(this.keyObject);
            this.keyObject = null;
            throw;
        }
    }

    // Compatibility overload used by the existing PDS1 container code.
    // The offsets are intentionally honored without changing the on-disk format.
    internal void Encrypt(byte[] nonce, byte[] plaintext, int plaintextOffset, int plaintextCount,
        byte[] ciphertext, int ciphertextOffset, byte[] tag)
    {
        ThrowIfDisposed();
        if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
        if (ciphertext == null) throw new ArgumentNullException(nameof(ciphertext));
        if (plaintextOffset < 0 || plaintextCount < 0 || plaintextOffset > plaintext.Length - plaintextCount)
            throw new ArgumentOutOfRangeException(nameof(plaintextOffset));
        if (ciphertextOffset < 0 || ciphertextOffset > ciphertext.Length - plaintextCount)
            throw new ArgumentOutOfRangeException(nameof(ciphertextOffset));
        if (tag == null) throw new ArgumentNullException(nameof(tag));

        byte[] input = new byte[plaintextCount];
        byte[] output = new byte[plaintextCount];
        try
        {
            Buffer.BlockCopy(plaintext, plaintextOffset, input, 0, plaintextCount);
            Encrypt(nonce, input, output, tag, null);
            Buffer.BlockCopy(output, 0, ciphertext, ciphertextOffset, plaintextCount);
        }
        finally
        {
            CryptoCompat.ZeroMemory(input);
            CryptoCompat.ZeroMemory(output);
        }
    }

    internal void Encrypt(byte[] nonce, byte[] plaintext, byte[] ciphertext, byte[] tag, byte[] associatedData = null)
    {
        ThrowIfDisposed();
        Validate(nonce, plaintext, ciphertext, tag);
        if (tag.Length != 16) throw new ArgumentException("GCM tag must be 16 bytes.", nameof(tag));

        Execute(true, nonce, plaintext, ciphertext, tag, associatedData);
    }

    // Compatibility overload used by the existing PDS1 container code.
    internal void Decrypt(byte[] nonce, byte[] ciphertext, int ciphertextOffset, int ciphertextCount,
        byte[] tag, byte[] plaintext, int plaintextOffset)
    {
        ThrowIfDisposed();
        if (ciphertext == null) throw new ArgumentNullException(nameof(ciphertext));
        if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
        if (ciphertextOffset < 0 || ciphertextCount < 0 || ciphertextOffset > ciphertext.Length - ciphertextCount)
            throw new ArgumentOutOfRangeException(nameof(ciphertextOffset));
        if (plaintextOffset < 0 || plaintextOffset > plaintext.Length - ciphertextCount)
            throw new ArgumentOutOfRangeException(nameof(plaintextOffset));
        if (tag == null) throw new ArgumentNullException(nameof(tag));

        byte[] input = new byte[ciphertextCount];
        byte[] output = new byte[ciphertextCount];
        try
        {
            Buffer.BlockCopy(ciphertext, ciphertextOffset, input, 0, ciphertextCount);
            Decrypt(nonce, input, tag, output, null);
            Buffer.BlockCopy(output, 0, plaintext, plaintextOffset, ciphertextCount);
        }
        finally
        {
            CryptoCompat.ZeroMemory(input);
            CryptoCompat.ZeroMemory(output);
        }
    }

    internal void Decrypt(byte[] nonce, byte[] ciphertext, byte[] tag, byte[] plaintext, byte[] associatedData = null)
    {
        ThrowIfDisposed();
        Validate(nonce, ciphertext, plaintext, tag);
        if (tag.Length != 16) throw new ArgumentException("GCM tag must be 16 bytes.", nameof(tag));

        Execute(false, nonce, ciphertext, plaintext, tag, associatedData);
    }

    private void Execute(bool encrypt, byte[] nonce, byte[] input, byte[] output, byte[] tag, byte[] associatedData)
    {
        GCHandle nonceHandle = default(GCHandle);
        GCHandle tagHandle = default(GCHandle);
        GCHandle aadHandle = default(GCHandle);
        bool noncePinned = false, tagPinned = false, aadPinned = false;

        try
        {
            nonceHandle = GCHandle.Alloc(nonce, GCHandleType.Pinned);
            noncePinned = true;
            tagHandle = GCHandle.Alloc(tag, GCHandleType.Pinned);
            tagPinned = true;

            IntPtr aadPtr = IntPtr.Zero;
            if (associatedData != null && associatedData.Length > 0)
            {
                aadHandle = GCHandle.Alloc(associatedData, GCHandleType.Pinned);
                aadPinned = true;
                aadPtr = aadHandle.AddrOfPinnedObject();
            }

            BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO info = new BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO
            {
                cbSize = Marshal.SizeOf(typeof(BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO)),
                dwInfoVersion = 1,
                pbNonce = nonceHandle.AddrOfPinnedObject(),
                cbNonce = nonce.Length,
                pbAuthData = aadPtr,
                cbAuthData = associatedData == null ? 0 : associatedData.Length,
                pbTag = tagHandle.AddrOfPinnedObject(),
                cbTag = tag.Length,
                pbMacContext = IntPtr.Zero,
                cbMacContext = 0,
                cbAAD = 0,
                cbData = input.LongLength,
                dwFlags = 0
            };

            int result;
            int status;
            if (encrypt)
            {
                status = BCryptEncrypt(
                    keyHandle, input, input.Length, ref info,
                    null, 0, output, output.Length, out result, 0);
            }
            else
            {
                status = BCryptDecrypt(
                    keyHandle, input, input.Length, ref info,
                    null, 0, output, output.Length, out result, 0);
            }

            if (status != BCRYPT_SUCCESS)
            {
                if (!encrypt)
                    CryptoCompat.ZeroMemory(output);
                throw new CryptographicExceptionCompat(
                    (encrypt ? "AES-GCM encryption" : "AES-GCM authentication/decryption") +
                    " failed. BCrypt status: 0x" + status.ToString("X8"));
            }

            if (result != output.Length)
                throw new CryptographicExceptionCompat("Unexpected AES-GCM output length.");
        }
        finally
        {
            if (aadPinned) aadHandle.Free();
            if (tagPinned) tagHandle.Free();
            if (noncePinned) nonceHandle.Free();
        }
    }

    private static void Validate(byte[] nonce, byte[] input, byte[] output, byte[] tag)
    {
        if (nonce == null) throw new ArgumentNullException(nameof(nonce));
        if (input == null) throw new ArgumentNullException(nameof(input));
        if (output == null) throw new ArgumentNullException(nameof(output));
        if (tag == null) throw new ArgumentNullException(nameof(tag));
        if (nonce.Length != 12) throw new ArgumentException("AES-GCM nonce must be 12 bytes.", nameof(nonce));
        if (output.Length != input.Length) throw new ArgumentException("Input and output lengths must match.", nameof(output));
    }

    private static void Check(int status, string operation)
    {
        if (status != BCRYPT_SUCCESS)
            throw new CryptographicExceptionCompat(operation + " failed. BCrypt status: 0x" + status.ToString("X8"));
    }

    private void ThrowIfDisposed()
    {
        if (disposed) throw new ObjectDisposedException(nameof(AesGcmCompat));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        if (keyHandle != IntPtr.Zero)
            BCryptDestroyKey(keyHandle);
        if (algorithmHandle != IntPtr.Zero)
            BCryptCloseAlgorithmProvider(algorithmHandle, 0);

        if (key != null)
        {
            CryptoCompat.ZeroMemory(key);
            key = null;
        }
        if (keyObject != null)
        {
            CryptoCompat.ZeroMemory(keyObject);
            keyObject = null;
        }
    }

    private sealed class CryptographicExceptionCompat : Exception
    {
        internal CryptographicExceptionCompat(string message) : base(message) { }
    }
}
