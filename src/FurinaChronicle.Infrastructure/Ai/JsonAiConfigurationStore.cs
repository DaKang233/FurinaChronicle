// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.Infrastructure.Ai;

/// <summary>Device settings only. The provider key is never serialized in this file.</summary>
public sealed class JsonAiConfigurationStore(string filePath, IAiSecretStore secrets) : IAiConfigurationStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<AiServiceSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(filePath)) return new();
            if (new FileInfo(filePath).Length > 16384) throw new AiAnalysisException("AI 配置文件无效，请重新保存配置。");
            var settings = JsonSerializer.Deserialize<AiServiceSettings>(await File.ReadAllTextAsync(filePath, cancellationToken), AiJson.Options) ?? new();
            settings.Validate(requireModel: false); return settings;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        { throw new AiAnalysisException("无法读取 AI 配置，请重新保存配置。"); }
        finally { gate.Release(); }
    }
    public async Task SaveAsync(AiServiceSettings settings, CancellationToken cancellationToken = default)
    {
        settings.Validate();
        await gate.WaitAsync(cancellationToken);
        string temporary = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings, AiJson.Options), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, filePath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new AiAnalysisException("无法保存 AI 配置，请检查本地存储。"); }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } finally { gate.Release(); } }
    }
    public Task<string?> GetApiKeyAsync(AiServiceSettings settings, CancellationToken cancellationToken = default) =>
        secrets.GetAsync(Slot(settings), cancellationToken);
    public Task SetApiKeyAsync(AiServiceSettings settings, string? key, CancellationToken cancellationToken = default)
    {
        if (key?.Any(char.IsControl) == true || key?.Length > 4096) throw new AiAnalysisException("密钥格式无效。");
        return secrets.SetAsync(Slot(settings), string.IsNullOrWhiteSpace(key) ? null : key.Trim(), cancellationToken);
    }
    private static string Slot(AiServiceSettings settings) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes($"{settings.Protocol}:{settings.Endpoint.AbsoluteUri}")));
}
