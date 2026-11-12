// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using FurinaChronicle.Presentation.Management;

namespace FurinaChronicle.Windows.Composition;

public sealed class WindowsProcessIdentityPort : IProcessIdentityPort
{
    private readonly Lazy<int> currentId = new(() => Environment.ProcessId);
    private readonly Lazy<DateTimeOffset> currentStart = new(() =>
        Process.GetCurrentProcess().StartTime.ToUniversalTime());

    public int CurrentProcessId => currentId.Value;

    public DateTimeOffset CurrentProcessStartedUtc => currentStart.Value;

    public string MachineName => Environment.MachineName;

    public bool IsProcessAlive(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public DateTimeOffset? GetProcessStartedUtc(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
