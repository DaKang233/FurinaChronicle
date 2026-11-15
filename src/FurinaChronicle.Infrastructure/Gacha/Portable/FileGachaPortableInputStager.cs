// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Buffers;
using System.Security.Cryptography;
using FurinaChronicle.Services.Gacha.Portable;

namespace FurinaChronicle.Infrastructure.Gacha.Portable;

public sealed class FileGachaPortableInputStager(
    string stagingDirectory,
    GachaPortableLimits? limits = null)
    : IGachaPortableInputStager
{
    private const int BufferSize = 64 * 1024;
    private readonly GachaPortableLimits limits = limits ?? new();

    public async Task<IGachaPortableStagedInput> StageAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException(
                "The Portable source stream is not readable.",
                nameof(source));
        }

        Directory.CreateDirectory(stagingDirectory);
        string path = Path.Combine(
            stagingDirectory,
            $"gacha-portable-{Guid.NewGuid():N}.tmp");
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);
            long length = 0;
            await using (var destination = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                while (true)
                {
                    int read = await source.ReadAsync(
                        buffer.AsMemory(0, buffer.Length),
                        cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    length = checked(length + read);
                    if (length > limits.MaximumContainerBytes)
                    {
                        throw new GachaPortableException(
                            GachaPortableErrorCode.ResourceLimitExceeded,
                            "The Portable container exceeds the staging limit.");
                    }

                    hash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(
                        buffer.AsMemory(0, read),
                        cancellationToken);
                }

                await destination.FlushAsync(cancellationToken);
            }

            return new StagedInput(
                path,
                length,
                Convert.ToHexString(hash.GetHashAndReset())
                    .ToLowerInvariant());
        }
        catch
        {
            TryDelete(path);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class StagedInput(
        string path,
        long length,
        string sha256)
        : IGachaPortableStagedInput
    {
        private int disposed;

        public long Length { get; } = length;

        public string Sha256 { get; } = sha256;

        public ValueTask<Stream> OpenReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref disposed) != 0,
                this);
            Stream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return ValueTask.FromResult(stream);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                TryDelete(path);
            }

            return ValueTask.CompletedTask;
        }
    }
}
