#if WINDOWS
using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
#elif ANDROID
using Android.App;
using Android.Content;
#endif

namespace FurinaChronicle.App.Exporting;

public sealed record ExportSaveResult(
    bool IsSuccessful,
    bool IsCanceled,
    string? FilePath,
    Exception? Exception = null);

public sealed class PlatformExportFileSaver
{
    public static PlatformExportFileSaver Default { get; } = new();

    private PlatformExportFileSaver()
    {
    }

    public async Task<ExportSaveResult> SaveAsync(
        string fileName,
        Stream source,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException(
                "文件名不能为空。",
                nameof(fileName));
        }
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException(
                "导出内容不可读。",
                nameof(source));
        }

        try
        {
            source.Position = 0;
            return await SavePlatformAsync(
                fileName,
                source,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new ExportSaveResult(false, true, null);
        }
        catch (Exception exception)
        {
            return new ExportSaveResult(
                false,
                false,
                null,
                exception);
        }
    }

#if WINDOWS
    private static async Task<ExportSaveResult> SavePlatformAsync(
        string fileName,
        Stream source,
        CancellationToken cancellationToken)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName =
                Path.GetFileNameWithoutExtension(fileName)
        };
        string extension = Path.GetExtension(fileName);
        picker.FileTypeChoices.Add(
            GetFileTypeDescription(extension),
            new List<string> { extension });

        Microsoft.Maui.Controls.Window? mauiWindow =
            Microsoft.Maui.Controls.Application.Current?
                .Windows.FirstOrDefault();
        if (mauiWindow?.Handler?.PlatformView is not
            Microsoft.UI.Xaml.Window nativeWindow)
        {
            throw new InvalidOperationException(
                "无法获取当前 Windows 窗口。");
        }

        IntPtr windowHandle =
            WindowNative.GetWindowHandle(nativeWindow);
        InitializeWithWindow.Initialize(picker, windowHandle);

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return new ExportSaveResult(false, true, null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await using Stream output =
            await file.OpenStreamForWriteAsync();
        output.SetLength(0);
        await source.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
        return new ExportSaveResult(true, false, file.Path);
    }

    private static string GetFileTypeDescription(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".json" => "UIGF JSON",
            ".csv" => "CSV 表格",
            ".xlsx" => "Excel 工作簿",
            _ => "导出文件"
        };
#elif ANDROID
    private static async Task<ExportSaveResult> SavePlatformAsync(
        string fileName,
        Stream source,
        CancellationToken cancellationToken)
    {
        MainActivity activity =
            Platform.CurrentActivity as MainActivity
            ?? throw new InvalidOperationException(
                "无法获取当前 Android Activity。");
        int requestCode = MainActivity.NextFileSaveRequestCode();
        var completion =
            new TaskCompletionSource<Android.Net.Uri?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<global::FurinaChronicle.App.ActivityResultEventArgs>?
            handler =
            null;
        handler = (_, args) =>
        {
            if (args.RequestCode != requestCode)
            {
                return;
            }

            completion.TrySetResult(
                args.ResultCode == Result.Ok
                    ? args.Data?.Data
                    : null);
        };
        MainActivity.ActivityResultReceived += handler;

        try
        {
            using var intent = new Intent(Intent.ActionCreateDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType(GetMimeType(fileName));
            intent.PutExtra(Intent.ExtraTitle, fileName);
            activity.StartActivityForResult(intent, requestCode);

            using CancellationTokenRegistration registration =
                cancellationToken.Register(
                    () => completion.TrySetCanceled(cancellationToken));
            Android.Net.Uri? uri = await completion.Task;
            if (uri is null)
            {
                return new ExportSaveResult(false, true, null);
            }

            await using Stream output =
                activity.ContentResolver?.OpenOutputStream(uri, "wt")
                ?? throw new IOException(
                    "系统没有返回可写的文件流。");
            await source.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
            return new ExportSaveResult(
                true,
                false,
                uri.ToString());
        }
        finally
        {
            MainActivity.ActivityResultReceived -= handler;
        }
    }

    private static string GetMimeType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".json" => "application/json",
            ".csv" => "text/csv",
            ".xlsx" =>
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };
#else
    private static async Task<ExportSaveResult> SavePlatformAsync(
        string fileName,
        Stream source,
        CancellationToken cancellationToken)
    {
        string filePath = Path.Combine(
            FileSystem.CacheDirectory,
            fileName);
        await using (FileStream output = File.Create(filePath))
        {
            await source.CopyToAsync(output, cancellationToken);
        }

        await Share.Default.RequestAsync(
            new ShareFileRequest(
                "导出祈愿记录",
                new ShareFile(filePath)));
        return new ExportSaveResult(true, false, filePath);
    }
#endif
}
