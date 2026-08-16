using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace VoidErase;

internal static class CryptoCompat
{
    public static byte[] RandomBytes(int length)
    {
        byte[] data = new byte[length];

        using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            rng.GetBytes(data);

        return data;
    }

    public static void ZeroMemory(byte[] data)
    {
        if (data != null)
            Array.Clear(data, 0, data.Length);
    }

    public static bool FixedTimeEquals(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
            return false;

        int diff = 0;

        for (int i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];

        return diff == 0;
    }

    public static void WriteAll(Stream stream, byte[] data)
    {
        stream.Write(data, 0, data.Length);
    }

    public static int Clamp(int value, int min, int max)
    {
        if (value < min)
            return min;

        if (value > max)
            return max;

        return value;
    }

    public static long Clamp(long value, long min, long max)
    {
        if (value < min)
            return min;

        if (value > max)
            return max;

        return value;
    }

    public static string ToHexString(byte[] data)
    {
        char[] chars = new char[data.Length * 2];
        const string hex = "0123456789ABCDEF";

        for (int i = 0; i < data.Length; i++)
        {
            chars[i * 2] = hex[data[i] >> 4];
            chars[i * 2 + 1] = hex[data[i] & 15];
        }

        return new string(chars);
    }
}

internal sealed class AesGcmCompat : IDisposable
{
    private const string BCRYPT_AES_ALGORITHM = "AES";
    private const string BCRYPT_CHAINING_MODE = "ChainingMode";
    private const string BCRYPT_CHAIN_MODE_GCM = "ChainingModeGCM";

    private const uint BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO_VERSION = 1;

    private IntPtr _algorithmHandle;
    private IntPtr _keyHandle;
    private IntPtr _keyObject;

    private bool _disposed;

    public AesGcmCompat(byte[] key)
    {
        if (key == null || key.Length != 32)
            throw new ArgumentException(
                "AES-256 requires a 32-byte key.",
                nameof(key));

        int status;

        status = BCryptOpenAlgorithmProvider(
            out _algorithmHandle,
            BCRYPT_AES_ALGORITHM,
            null,
            0);

        CheckStatus(status, "BCryptOpenAlgorithmProvider");

        try
        {
            byte[] mode = System.Text.Encoding.Unicode.GetBytes(
                BCRYPT_CHAIN_MODE_GCM + "\0");

            status = BCryptSetProperty(
                _algorithmHandle,
                BCRYPT_CHAINING_MODE,
                mode,
                mode.Length,
                0);

            CheckStatus(status, "BCryptSetProperty");
            mode = null;

            uint objectLength = 0;
            uint resultLength = 0;

            status = BCryptGetProperty(
                _algorithmHandle,
                "ObjectLength",
                ref objectLength,
                sizeof(uint),
                out resultLength,
                0);

            CheckStatus(status, "BCryptGetProperty");

            _keyObject = Marshal.AllocHGlobal(
                checked((int)objectLength));

            status = BCryptGenerateSymmetricKey(
                _algorithmHandle,
                out _keyHandle,
                _keyObject,
                objectLength,
                key,
                checked((uint)key.Length),
                0);

            CheckStatus(status, "BCryptGenerateSymmetricKey");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Encrypt(
    byte[] nonce,
    byte[] plaintext,
    int plainOffset,
    int length,
    byte[] ciphertext,
    int cipherOffset,
    byte[] tag)
{
    Ensure();

    ValidateBuffers(
        nonce,
        plaintext,
        plainOffset,
        length,
        ciphertext,
        cipherOffset,
        tag);

    byte[] iv = (byte[])nonce.Clone();
    byte[] input = new byte[length];
    byte[] output = new byte[length];

    Buffer.BlockCopy(
        plaintext,
        plainOffset,
        input,
        0,
        length);

    GCHandle ivHandle = default(GCHandle);
    GCHandle tagHandle = default(GCHandle);

    IntPtr authInfoPtr = IntPtr.Zero;

    try
    {
        ivHandle = GCHandle.Alloc(iv, GCHandleType.Pinned);
        tagHandle = GCHandle.Alloc(tag, GCHandleType.Pinned);

        BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO authInfo =
            new BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO();

        authInfo.cbSize = checked((uint)Marshal.SizeOf(
            typeof(BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO)));

        authInfo.dwInfoVersion =
            BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO_VERSION;

        authInfo.pbNonce = ivHandle.AddrOfPinnedObject();
        authInfo.cbNonce = checked((uint)iv.Length);

        authInfo.pbAuthData = IntPtr.Zero;
        authInfo.cbAuthData = 0;

        authInfo.pbTag = tagHandle.AddrOfPinnedObject();
        authInfo.cbTag = 16;

        authInfo.pbMacContext = IntPtr.Zero;
        authInfo.cbMacContext = 0;
        authInfo.cbAAD = 0;
        authInfo.cbData = 0;
        authInfo.dwFlags = 0;

        authInfoPtr = Marshal.AllocHGlobal(
            Marshal.SizeOf(typeof(
                BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO)));

        Marshal.StructureToPtr(
            authInfo,
            authInfoPtr,
            false);

        uint resultLength = 0;

        int status = BCryptEncrypt(
            _keyHandle,
            input,
            checked((uint)input.Length),
            authInfoPtr,
            iv,
            checked((uint)iv.Length),
            output,
            checked((uint)output.Length),
            out resultLength,
            0);

        CheckStatus(status, "BCryptEncrypt");

        if (resultLength != (uint)length)
        {
            throw new CryptographicException(
                "AES-GCM encryption returned an unexpected length. " +
                "Expected=" + length +
                ", Actual=" + resultLength);
        }

        Buffer.BlockCopy(
            output,
            0,
            ciphertext,
            cipherOffset,
            length);
    }
    finally
    {
        if (authInfoPtr != IntPtr.Zero)
            Marshal.FreeHGlobal(authInfoPtr);

        if (tagHandle.IsAllocated)
            tagHandle.Free();

        if (ivHandle.IsAllocated)
            ivHandle.Free();

        CryptoCompat.ZeroMemory(input);
        CryptoCompat.ZeroMemory(output);
        CryptoCompat.ZeroMemory(iv);
    }
}

    public void Decrypt(
        byte[] nonce,
        byte[] ciphertext,
        int cipherOffset,
        int length,
        byte[] tag,
        byte[] plaintext,
        int plainOffset)
    {
        Ensure();

        ValidateBuffers(
            nonce,
            ciphertext,
            cipherOffset,
            length,
            plaintext,
            plainOffset,
            tag);

        byte[] iv = (byte[])nonce.Clone();
        byte[] input = new byte[length];

        Buffer.BlockCopy(
            ciphertext,
            cipherOffset,
            input,
            0,
            length);

        byte[] output = new byte[length];

        BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO authInfo =
            CreateAuthInfo(
                iv,
                tag,
                null);

        IntPtr authInfoPtr = IntPtr.Zero;

        try
        {
            authInfoPtr = Marshal.AllocHGlobal(
                Marshal.SizeOf(typeof(
                    BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO)));

            Marshal.StructureToPtr(
                authInfo,
                authInfoPtr,
                false);

            uint resultLength = 0;

            int status = BCryptDecrypt(
                _keyHandle,
                input,
                checked((uint)input.Length),
                authInfoPtr,
                iv,
                checked((uint)iv.Length),
                output,
                checked((uint)output.Length),
                out resultLength,
                0);

            if (status != STATUS_SUCCESS)
            {
                if (status == STATUS_AUTH_TAG_MISMATCH)
                {
                    throw new CryptographicException(
                        "AES-GCM authentication tag mismatch.");
                }

                CheckStatus(status, "BCryptDecrypt");
            }

            if (resultLength != length)
                throw new CryptographicException(
                    "AES-GCM decryption returned an unexpected length.");

            Buffer.BlockCopy(
                output,
                0,
                plaintext,
                plainOffset,
                length);
        }
        finally
        {
            if (authInfoPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(authInfoPtr);

            CryptoCompat.ZeroMemory(input);
            CryptoCompat.ZeroMemory(output);
            CryptoCompat.ZeroMemory(iv);
        }
    }

    private static BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO CreateAuthInfo(
        byte[] nonce,
        byte[] tag,
        byte[] aad)
    {
        return new BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO
        {
            cbSize = checked((uint)Marshal.SizeOf(
                typeof(BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO))),

            dwInfoVersion =
                BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO_VERSION,

            pbNonce = Marshal.UnsafeAddrOfPinnedArrayElement(
                nonce,
                0),

            cbNonce = checked((uint)nonce.Length),

            pbAuthData = aad == null
                ? IntPtr.Zero
                : Marshal.UnsafeAddrOfPinnedArrayElement(
                    aad,
                    0),

            cbAuthData = aad == null
                ? 0
                : checked((uint)aad.Length),

            pbTag = Marshal.UnsafeAddrOfPinnedArrayElement(
                tag,
                0),

            cbTag = checked((uint)tag.Length),

            pbMacContext = IntPtr.Zero,

            cbMacContext = 0,

            cbAAD = 0,

            cbData = 0,

            dwFlags = 0
        };
    }

    private static void ValidateBuffers(
        byte[] nonce,
        byte[] input,
        int inputOffset,
        int length,
        byte[] output,
        int outputOffset,
        byte[] tag)
    {
        if (nonce == null || nonce.Length != 12)
            throw new ArgumentException(
                "GCM nonce must be 12 bytes.");

        if (tag == null || tag.Length < 16)
            throw new ArgumentException(
                "GCM tag must be 16 bytes.");

        if (input == null)
            throw new ArgumentNullException(
                nameof(input));

        if (output == null)
            throw new ArgumentNullException(
                nameof(output));

        if (inputOffset < 0 ||
            length < 0 ||
            inputOffset > input.Length - length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inputOffset));
        }

        if (outputOffset < 0 ||
            length < 0 ||
            outputOffset > output.Length - length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outputOffset));
        }
    }

private static void CheckStatus(
    int status,
    string operation)
{
    if (status != STATUS_SUCCESS)
    {
        throw new CryptographicException(
            operation +
            " failed. NTSTATUS=0x" +
            ((uint)status).ToString("X8"));
    }
}

    private void Ensure()
    {
        if (_disposed)
            throw new ObjectDisposedException(
                nameof(AesGcmCompat));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_keyHandle != IntPtr.Zero)
        {
            BCryptDestroyKey(_keyHandle);
            _keyHandle = IntPtr.Zero;
        }

        if (_algorithmHandle != IntPtr.Zero)
        {
            BCryptCloseAlgorithmProvider(
                _algorithmHandle,
                0);

            _algorithmHandle = IntPtr.Zero;
        }

        if (_keyObject != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_keyObject);
            _keyObject = IntPtr.Zero;
        }
    }

    private const int STATUS_SUCCESS = 0;
    private const int STATUS_AUTH_TAG_MISMATCH =
        unchecked((int)0xC000A002);

  

    [StructLayout(LayoutKind.Sequential)]
    private struct BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO
    {
        public uint cbSize;
        public uint dwInfoVersion;

        public IntPtr pbNonce;
        public uint cbNonce;

        public IntPtr pbAuthData;
        public uint cbAuthData;

        public IntPtr pbTag;
        public uint cbTag;

        public IntPtr pbMacContext;
        public uint cbMacContext;

        public uint cbAAD;
        public ulong cbData;

        public uint dwFlags;
    }

    [DllImport(
        "bcrypt.dll",
        CharSet = CharSet.Unicode)]
    private static extern int BCryptOpenAlgorithmProvider(
        out IntPtr phAlgorithm,
        string pszAlgId,
        string pszImplementation,
        uint dwFlags);

    [DllImport(
        "bcrypt.dll",
        CharSet = CharSet.Unicode)]
    private static extern int  BCryptSetProperty(
        IntPtr hObject,
        string pszProperty,
        byte[] pbInput,
        int cbInput,
        uint dwFlags);

    [DllImport(
        "bcrypt.dll",
        CharSet = CharSet.Unicode)]
    private static extern int BCryptGetProperty(
        IntPtr hObject,
        string pszProperty,
        ref uint pbOutput,
        int cbOutput,
        out uint pcbResult,
        uint dwFlags);

    [DllImport("bcrypt.dll")]
    private static extern int BCryptGenerateSymmetricKey(
        IntPtr hAlgorithm,
        out IntPtr phKey,
        IntPtr pbKeyObject,
        uint cbKeyObject,
        byte[] pbSecret,
        uint cbSecret,
        uint dwFlags);

    [DllImport("bcrypt.dll")]
    private static extern int BCryptEncrypt(
        IntPtr hKey,
    byte[] pbInput,
    uint cbInput,
    IntPtr pPaddingInfo,
    byte[] pbIV,
    uint cbIV,
    byte[] pbOutput,
    uint cbOutput,
    out uint pcbResult,
    uint dwFlags);

    [DllImport("bcrypt.dll")]
    private static extern int BCryptDecrypt(
        IntPtr hKey,
        byte[] pbInput,
        uint cbInput,
        IntPtr pPaddingInfo,
        byte[] pbIV,
        uint cbIV,
        byte[] pbOutput,
        uint cbOutput,
        out uint pcbResult,
        uint dwFlags);

    [DllImport("bcrypt.dll")]
    private static extern int BCryptDestroyKey(
        IntPtr hKey);

    [DllImport("bcrypt.dll")]
    private static extern int BCryptCloseAlgorithmProvider(
        IntPtr hAlgorithm,
        uint dwFlags);
}