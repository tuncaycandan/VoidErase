using System;
using System.Collections.Generic;

namespace VoidErase;

internal static class SecureBufferPool
{
    private static readonly object Sync = new object();
    private static readonly Stack<byte[]> Pool = new Stack<byte[]>();
    private const int MaximumRetainedBuffers = 4;

    public static byte[] Rent(int minimumLength)
    {
        lock (Sync)
        {
            while (Pool.Count > 0)
            {
                byte[] candidate = Pool.Pop();
                if (candidate.Length >= minimumLength)
                    return candidate;
            }
        }

        return new byte[minimumLength];
    }

    public static void Return(byte[] buffer)
    {
        if (buffer == null)
            return;

        CryptoCompat.ZeroMemory(buffer);
        lock (Sync)
        {
            if (Pool.Count < MaximumRetainedBuffers)
                Pool.Push(buffer);
        }
    }
}

internal sealed class SecureRentedBuffer : IDisposable
{
    private byte[]? buffer;

    private SecureRentedBuffer(byte[] rented)
    {
        buffer = rented;
    }

    public byte[] Buffer => buffer ?? throw new ObjectDisposedException(nameof(SecureRentedBuffer));

    public static SecureRentedBuffer Rent(int minimumLength)
    {
        return new SecureRentedBuffer(SecureBufferPool.Rent(minimumLength));
    }

    public void Dispose()
    {
        byte[]? rented = buffer;
        if (rented == null)
            return;

        buffer = null;
        SecureBufferPool.Return(rented);
    }
}
