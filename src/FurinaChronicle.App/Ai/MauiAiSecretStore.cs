// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.App.Ai;

public sealed class MauiAiSecretStore(ISecureStorage storage) : IAiSecretStore
{
    public async Task<string?> GetAsync(string slot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { return await storage.GetAsync("furina.ai." + slot); }
        catch (Exception) { throw new AiAnalysisException("无法读取 AI 密钥；请重新输入并保存。"); }
    }
    public async Task SetAsync(string slot, string? value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (value is null) storage.Remove("furina.ai." + slot);
            else await storage.SetAsync("furina.ai." + slot, value);
        }
        catch (Exception) { throw new AiAnalysisException("无法保存 AI 密钥；请检查本机安全存储。"); }
    }
}
