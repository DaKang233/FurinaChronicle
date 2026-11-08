// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

#if WINDOWS && (PHASE8C_VALIDATION || PHASE8B_VALIDATION)
using System.Collections.Concurrent;

namespace FurinaChronicle.App.Persistence;

// Validation settings are page/session conveniences. Portable authoritative settings stay in SQLite.
internal sealed class ValidationPreferences : IPreferences
{
    private readonly ConcurrentDictionary<(string? Container, string Key), object> values = new();
    public bool ContainsKey(string key, string? sharedName = null) => values.ContainsKey((sharedName, key));
    public void Remove(string key, string? sharedName = null) => values.TryRemove((sharedName, key), out _);
    public void Clear(string? sharedName = null)
    {
        foreach (var key in values.Keys.Where(k => k.Container == sharedName)) values.TryRemove(key, out _);
    }
    public void Set<T>(string key, T value, string? sharedName = null) => values[(sharedName, key)] = value!;
    public T Get<T>(string key, T defaultValue, string? sharedName = null) =>
        values.TryGetValue((sharedName, key), out object? value) && value is T typed ? typed : defaultValue;
}

// The regression host has no login requirement. Never fall back to real OS credential storage.
internal sealed class DisabledValidationSecureStorage : ISecureStorage
{
    public Task<string?> GetAsync(string key) => Task.FromResult<string?>(null);
    public Task SetAsync(string key, string value) =>
        throw new NotSupportedException("Credential storage is disabled in this validation host.");
    public bool Remove(string key) => false;
    public void RemoveAll() { }
}
#endif
