using Android.App;
using Android.Content.PM;
using Android.OS;

namespace FurinaChronicle.App
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        private static int nextRequestCode = 7400;

        internal static event EventHandler<ActivityResultEventArgs>?
            ActivityResultReceived;

        internal static int NextFileSaveRequestCode()
        {
            int requestCode =
                Interlocked.Increment(ref nextRequestCode);
            if (requestCode < 32000)
            {
                return requestCode;
            }

            Interlocked.Exchange(ref nextRequestCode, 7400);
            return Interlocked.Increment(ref nextRequestCode);
        }

        protected override void OnActivityResult(
            int requestCode,
            Result resultCode,
            Android.Content.Intent? data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            ActivityResultReceived?.Invoke(
                this,
                new ActivityResultEventArgs(
                    requestCode,
                    resultCode,
                    data));
        }
    }

    internal sealed record ActivityResultEventArgs(
        int RequestCode,
        Result ResultCode,
        Android.Content.Intent? Data);
}
