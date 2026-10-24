// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;

namespace FurinaChronicle.Infrastructure.Gacha.Portable;

internal abstract class HashingStream(Stream inner) : Stream
{
    private readonly IncrementalHash hash =
        IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private bool completed;

    protected Stream Inner { get; } = inner;

    public long ByteCount { get; private set; }

    public override bool CanSeek => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public string CompleteHash()
    {
        if (completed)
        {
            throw new InvalidOperationException("The hash has already been completed.");
        }

        completed = true;
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    protected void Append(ReadOnlySpan<byte> buffer)
    {
        if (completed)
        {
            throw new InvalidOperationException("The hash has already been completed.");
        }

        hash.AppendData(buffer);
        ByteCount += buffer.Length;
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            hash.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class HashingWriteStream(Stream inner) : HashingStream(inner)
{
    public override bool CanRead => false;

    public override bool CanWrite => true;

    public override void Flush() => Inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        Inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        Inner.Write(buffer, offset, count);
        Append(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Inner.Write(buffer);
        Append(buffer);
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        await Inner.WriteAsync(buffer, cancellationToken);
        Append(buffer.Span);
    }
}

internal sealed class HashingReadStream(Stream inner) : HashingStream(inner)
{
    public override bool CanRead => true;

    public override bool CanWrite => false;

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = Inner.Read(buffer, offset, count);
        Append(buffer.AsSpan(offset, read));
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        int read = Inner.Read(buffer);
        Append(buffer[..read]);
        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        int read = await Inner.ReadAsync(buffer, cancellationToken);
        Append(buffer.Span[..read]);
        return read;
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}
