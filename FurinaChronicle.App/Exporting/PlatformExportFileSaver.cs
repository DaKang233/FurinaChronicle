// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

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
            ".json" => "JSON 数据",
            ".md" => "Markdown 文档",
            ".csv" => "CSV 表格",
            ".xlsx" => "Excel 工作簿",
            ".fca" => "Furina 逻辑 Archive",
            _ => "导出文件"
        };
#elif ANDROID
    // DocumentsContract.Document.COLUMN_RELATIVE_PATH (API 29) has no binding
    // constant in .NET for Android, so the provider column name is literal.
    private const string RelativePathColumn = "relative_path";

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
                DescribeSavedLocation(activity, uri, fileName));
        }
        finally
        {
            MainActivity.ActivityResultReceived -= handler;
        }
    }

    // The Android save dialog hands back a content:// URI. That URI is not a
    // location a user can act on, so report the display name plus the folder
    // when the provider exposes it (D2). Windows keeps the real path.
    private static string DescribeSavedLocation(
        MainActivity activity,
        Android.Net.Uri uri,
        string fileName)
    {
        try
        {
            Android.Content.ContentResolver? resolver =
                activity.ContentResolver;
            if (resolver is null)
            {
                return fileName;
            }

            string displayName =
                QueryDisplayName(resolver, uri) ?? fileName;
            string? folder = DescribeDocumentFolder(resolver, uri);
            if (string.IsNullOrWhiteSpace(folder) &&
                string.Equals(
                    uri.Authority,
                    "com.android.providers.downloads.documents",
                    StringComparison.Ordinal))
            {
                // The Android Downloads provider only stores under the public
                // Download directory, even when it does not expose the
                // document's relative path (observed on API 36).
                folder = "下载目录";
            }

            return string.IsNullOrWhiteSpace(folder)
                ? displayName
                : $"{folder} / {displayName}";
        }
        catch (Exception)
        {
            // The friendly location is presentation only: a saved file must
            // never be reported as failed because a provider answered oddly.
            return fileName;
        }
    }

    private static string? QueryDisplayName(
        Android.Content.ContentResolver resolver,
        Android.Net.Uri uri)
    {
        string[] projection = [Android.Provider.IOpenableColumns.DisplayName];
        using Android.Database.ICursor? cursor =
            resolver.Query(uri, projection, null, null, null);
        if (cursor is null || !cursor.MoveToFirst())
        {
            return null;
        }

        int index = cursor.GetColumnIndex(
            Android.Provider.IOpenableColumns.DisplayName);
        if (index < 0 || cursor.IsNull(index))
        {
            return null;
        }

        string? name = cursor.GetString(index);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string? DescribeDocumentFolder(
        Android.Content.ContentResolver resolver,
        Android.Net.Uri uri)
    {
        // A documents provider that supports relative_path answers the
        // question directly (Downloads and external storage do on 29+).
        string? relativeFolder = QueryDocumentRelativePath(resolver, uri);
        if (!string.IsNullOrWhiteSpace(relativeFolder))
        {
            return DescribeRelativeFolder(relativeFolder);
        }

        string? documentId = null;
        try
        {
            documentId = Android.Provider.DocumentsContract
                .GetDocumentId(uri);
        }
        catch (Exception)
        {
            // Not a documents-provider URI; fall through to no folder.
        }

        if (documentId is not null &&
            documentId.StartsWith("raw:", StringComparison.Ordinal))
        {
            return DescribePrimaryStorageFolder(documentId.Substring(4));
        }

        if (documentId is not null &&
            documentId.StartsWith("msf:", StringComparison.Ordinal))
        {
            documentId = documentId.Substring(4);
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(29) &&
            long.TryParse(
                documentId,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out long downloadId))
        {
            return DescribeDownloadFolder(resolver, downloadId);
        }

        return null;
    }

    private static string? QueryDocumentRelativePath(
        Android.Content.ContentResolver resolver,
        Android.Net.Uri uri)
    {
        try
        {
            string[] projection =
                [RelativePathColumn];
            using Android.Database.ICursor? cursor =
                resolver.Query(uri, projection, null, null, null);
            if (cursor is null || !cursor.MoveToFirst())
            {
                return null;
            }

            int index = cursor.GetColumnIndex(
                RelativePathColumn);
            if (index < 0 || cursor.IsNull(index))
            {
                return null;
            }

            return cursor.GetString(index);
        }
        catch (Exception)
        {
            return null;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    private static string? DescribeDownloadFolder(
        Android.Content.ContentResolver resolver,
        long downloadId)
    {
        try
        {
            string[] projection = [RelativePathColumn];
            using Android.Database.ICursor? cursor = resolver.Query(
                Android.Provider.MediaStore.Downloads.ExternalContentUri,
                projection,
                "_id = ?",
                [
                    downloadId.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                ],
                null);
            if (cursor is null || !cursor.MoveToFirst())
            {
                return null;
            }

            int index = cursor.GetColumnIndex(RelativePathColumn);
            if (index < 0 || cursor.IsNull(index))
            {
                return null;
            }

            return DescribeRelativeFolder(cursor.GetString(index));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? DescribePrimaryStorageFolder(string path)
    {
        const string primaryStorage = "/storage/emulated/0/";
        if (!path.StartsWith(primaryStorage, StringComparison.Ordinal))
        {
            return null;
        }

        string relative = path.Substring(primaryStorage.Length);
        int separator = relative.LastIndexOf('/');
        if (separator <= 0)
        {
            return "内部存储";
        }

        return DescribeRelativeFolder(relative.Substring(0, separator + 1));
    }

    private static string? DescribeRelativeFolder(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        string normalized = relativePath
            .Replace('\\', '/')
            .Trim('/');
        if (normalized.Length == 0)
        {
            return "内部存储";
        }

        const string downloads = "Download";
        if (string.Equals(
                normalized,
                downloads,
                StringComparison.OrdinalIgnoreCase))
        {
            return "下载目录";
        }

        if (normalized.StartsWith(
                downloads + "/",
                StringComparison.OrdinalIgnoreCase))
        {
            return $"下载目录 / {normalized.Substring(downloads.Length + 1)}";
        }

        return $"内部存储 / {normalized}";
    }

    private static string GetMimeType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".json" => "application/json",
            ".md" => "text/markdown",
            ".csv" => "text/csv",
            ".xlsx" =>
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".fca" => "application/zip",
            _ => "application/octet-stream"
        };
#else
#error PlatformExportFileSaver supports only Windows and Android.
#endif
}
