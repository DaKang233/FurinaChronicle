// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using Android.App;
using Android.OS;
using Android.Util;
using Android.Widget;

namespace FurinaChronicle.Phase8CAndroidProbe;

[Activity(Name = "com.companyname.furinachronicle.validation.phase8c7probe.ProbeActivity",
    Exported = true, Permission = "android.permission.DUMP")]
public sealed class ProbeActivity : Activity
{
    private const string Tag = "Furina8C7Probe";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(new TextView(this) { Text = "Phase 8C.7 synthetic restore probe" });
        string? command = Intent?.GetStringExtra("command");
        string? runId = Intent?.GetStringExtra("run");
        if (command is null || !Guid.TryParseExact(runId, "N", out _) ||
            FilesDir?.AbsolutePath is not string root)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await RestoreInterruptionProbe.RunAsync(
                    command, root, runId!, "android", message => Log.Info(Tag, message));
                Log.Info(Tag, $"run={runId} COMPLETE command={command}");
            }
            catch (Exception exception)
            {
                Log.Error(Tag, $"run={runId} FAILED {exception}");
            }
        });
    }
}
