using System;
using System.IO;
using System.Security.Cryptography;

internal static class CryptoCompat
{
    internal static byte[] RandomBytes(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        byte[] buffer = new byte[count];
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(buffer);
        }

        return buffer;
    }

    internal static void ZeroMemory(byte[] buffer)
    {
        if (buffer == null)
            return;

        Array.Clear(buffer, 0, buffer.Length);
    }

    internal static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        if (left == null || right == null)
            return left == right;

        if (left.Length != right.Length)
            return false;

        int diff = 0;
        for (int i = 0; i < left.Length; i++)
            diff |= left[i] ^ right[i];

        return diff == 0;
    }

    internal static void WriteAll(Stream stream, byte[] buffer)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer));

        int offset = 0;
        while (offset < buffer.Length)
        {
            int before = offset;
            stream.Write(buffer, offset, buffer.Length - offset);
            offset = buffer.Length;

            // FileStream.Write normally writes the full requested range.
            // Keep the guard to avoid an accidental infinite loop with a custom Stream.
            if (offset == before)
                throw new IOException("The stream did not accept any data.");
        }
    }

    internal static string ToHexString(byte[] bytes)
    {
        if (bytes == null)
            throw new ArgumentNullException(nameof(bytes));

        char[] chars = new char[bytes.Length * 2];
        const string hex = "0123456789ABCDEF";

        for (int i = 0; i < bytes.Length; i++)
        {
            chars[i * 2] = hex[bytes[i] >> 4];
            chars[i * 2 + 1] = hex[bytes[i] & 0x0F];
        }

        return new string(chars);
    }

    internal static int Clamp(int value, int min, int max)
    {
        if (min > max)
            throw new ArgumentException("min must not be greater than max.");

        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    internal static long Clamp(long value, long min, long max)
    {
        if (min > max)
            throw new ArgumentException("min must not be greater than max.");

        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
