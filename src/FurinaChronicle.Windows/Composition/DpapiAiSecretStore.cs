// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.Windows.Composition;

public sealed class DpapiAiSecretStore(string filePath) : IAiSecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FurinaChronicle.Ai.Keys.v1");
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<string?> GetAsync(string slot, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try { return (await ReadAsync(cancellationToken)).GetValueOrDefault(slot); }
        finally { gate.Release(); }
    }
    public async Task SetAsync(string slot, string? value, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        string temporary = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var entries = await ReadAsync(cancellationToken);
            if (value is null) entries.Remove(slot); else entries[slot] = value;
            byte[] plain = JsonSerializer.SerializeToUtf8Bytes(entries);
            byte[] encrypted;
            try { encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
            await File.WriteAllBytesAsync(temporary, encrypted, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); File.Move(temporary, filePath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
        { throw new AiAnalysisException("无法保存 AI 密钥；请检查本机安全存储。"); }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } finally { gate.Release(); } }
    }
    private async Task<Dictionary<string, string>> ReadAsync(CancellationToken token)
    {
        if (!File.Exists(filePath)) return new();
        try
        {
            if (new FileInfo(filePath).Length > 131072) throw new AiAnalysisException("AI 安全存储无效。");
            byte[] encrypted = await File.ReadAllBytesAsync(filePath, token);
            byte[] plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            try { return JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? new(); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        { throw new AiAnalysisException("无法读取 AI 密钥；请重新输入并保存。"); }
    }
}
