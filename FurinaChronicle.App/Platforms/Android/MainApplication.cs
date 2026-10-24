// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using Android.App;
using Android.Runtime;

namespace FurinaChronicle.App
{
    [Application]
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}
