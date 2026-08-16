
using System;
using System.IO;
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
        if (data != null) Array.Clear(data, 0, data.Length);
    }

    public static bool FixedTimeEquals(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    public static void WriteAll(Stream stream, byte[] data)
    {
        stream.Write(data, 0, data.Length);
    }

    public static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    public static long Clamp(long value, long min, long max)
    {
        if (value < min) return min;
        if (value > max) return max;
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

/// <summary>
/// AES-256-GCM compatibility implementation for .NET Framework 4.8.
/// The file format used by VoidErase remains PDS1 with 12-byte nonces and 16-byte tags.
/// </summary>
internal sealed class AesGcmCompat : IDisposable
{
    private readonly Aes _aes;
    private readonly ICryptoTransform _aesEncrypt;
    private readonly byte[] _h;
    private bool _disposed;

    public AesGcmCompat(byte[] key)
    {
        if (key == null || key.Length != 32)
            throw new ArgumentException("AES-256 requires a 32-byte key.", nameof(key));

        Aes aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = (byte[])key.Clone();

        _aesEncrypt = aes.CreateEncryptor();
        _h = new byte[16];
        _aesEncrypt.TransformBlock(new byte[16], 0, 16, _h, 0);

        _aes = aes;
    }

    public void Encrypt(byte[] nonce, byte[] plaintext, int plainOffset, int length,
        byte[] ciphertext, int cipherOffset, byte[] tag)
    {
        Ensure();
        if (nonce == null || nonce.Length != 12) throw new ArgumentException("GCM nonce must be 12 bytes.");
        if (tag == null || tag.Length < 16) throw new ArgumentException("GCM tag must be 16 bytes.");

        byte[] j0 = new byte[16];
        Buffer.BlockCopy(nonce, 0, j0, 0, 12);
        j0[15] = 1;

        int counter = 1;
        int pos = 0;
        byte[] stream = new byte[16];

        while (pos < length)
        {
            counter++;
            SetCounter(j0, counter);
            _aesEncrypt.TransformBlock(j0, 0, 16, stream, 0);

            int take = Math.Min(16, length - pos);
            for (int i = 0; i < take; i++)
                ciphertext[cipherOffset + pos + i] = (byte)(plaintext[plainOffset + pos + i] ^ stream[i]);
            pos += take;
        }

        byte[] s = GHash(ciphertext, cipherOffset, length);
        byte[] eJ0 = new byte[16];
        _aesEncrypt.TransformBlock(j0, 0, 16, eJ0, 0);
        for (int i = 0; i < 16; i++) tag[i] = (byte)(eJ0[i] ^ s[i]);
    }

    public void Decrypt(byte[] nonce, byte[] ciphertext, int cipherOffset, int length,
        byte[] tag, byte[] plaintext, int plainOffset)
    {
        Ensure();
        if (nonce == null || nonce.Length != 12) throw new ArgumentException("GCM nonce must be 12 bytes.");
        if (tag == null || tag.Length < 16) throw new ArgumentException("GCM tag must be 16 bytes.");

        byte[] j0 = new byte[16];
        Buffer.BlockCopy(nonce, 0, j0, 0, 12);
        j0[15] = 1;

        byte[] s = GHash(ciphertext, cipherOffset, length);
        byte[] eJ0 = new byte[16];
        _aesEncrypt.TransformBlock(j0, 0, 16, eJ0, 0);

        byte[] expected = new byte[16];
        for (int i = 0; i < 16; i++) expected[i] = (byte)(eJ0[i] ^ s[i]);

        if (!CryptoCompat.FixedTimeEquals(expected, tag))
            throw new CryptographicException("AES-GCM authentication tag mismatch.");

        int counter = 1;
        int pos = 0;
        byte[] stream = new byte[16];

        while (pos < length)
        {
            counter++;
            SetCounter(j0, counter);
            _aesEncrypt.TransformBlock(j0, 0, 16, stream, 0);

            int take = Math.Min(16, length - pos);
            for (int i = 0; i < take; i++)
                plaintext[plainOffset + pos + i] = (byte)(ciphertext[cipherOffset + pos + i] ^ stream[i]);
            pos += take;
        }
    }

    private byte[] GHash(byte[] data, int offset, int length)
    {
        byte[] y = new byte[16];

        int pos = 0;
        while (pos < length)
        {
            byte[] block = new byte[16];
            int take = Math.Min(16, length - pos);
            Buffer.BlockCopy(data, offset + pos, block, 0, take);
            Xor(y, block);
            y = Multiply(y, _h);
            pos += take;
        }

        // GHASH length block: AAD length = 0, ciphertext length in bits.
        byte[] lenBlock = new byte[16];
        ulong bits = checked((ulong)length * 8UL);
        for (int i = 0; i < 8; i++)
            lenBlock[15 - i] = (byte)(bits >> (8 * i));

        Xor(y, lenBlock);
        return Multiply(y, _h);
    }


    private static byte[] MultiplySlow(byte[] x, byte[] h)
    {
        byte[] z = new byte[16];
        byte[] v = (byte[])h.Clone();

        for (int i = 0; i < 16; i++)
        {
            int value = x[i];
            for (int bit = 7; bit >= 0; bit--)
            {
                if (((value >> bit) & 1) != 0) Xor(z, v);
                bool lsb = (v[15] & 1) != 0;
                for (int j = 15; j > 0; j--)
                    v[j] = (byte)((v[j] >> 1) | ((v[j - 1] & 1) << 7));
                v[0] = (byte)(v[0] >> 1);
                if (lsb) v[0] ^= 0xE1;
            }
        }
        return z;
    }

    private byte[] Multiply(byte[] x, byte[] h)
    {
        return MultiplySlow(x, h);
    }

    private static void Xor(byte[] a, byte[] b)
    {
        for (int i = 0; i < 16; i++) a[i] ^= b[i];
    }

    private static void SetCounter(byte[] block, int value)
    {
        block[12] = (byte)(value >> 24);
        block[13] = (byte)(value >> 16);
        block[14] = (byte)(value >> 8);
        block[15] = (byte)value;
    }

    private void Ensure()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AesGcmCompat));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _aesEncrypt.Dispose();
        _aes.Dispose();
        CryptoCompat.ZeroMemory(_h);
    }
}
