// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FurinaChronicle.Windows.Composition;

// Window-associated system file dialogs. Every page uses this port instead of re-implementing
// picker initialization; the owner window is set once by MainWindow.
public interface IFilePickerService
{
    void SetOwner(Window owner);

    Task<StorageFile?> PickOpenFileAsync(string extension);

    Task<StorageFile?> PickSaveFileAsync(string suggestedName, string typeLabel, string extension);

    Task<StorageFolder?> PickFolderAsync();
}

public sealed class WindowsFilePickerService : IFilePickerService
{
    private Window? owner;

    public void SetOwner(Window window) => owner = window;

    public async Task<StorageFile?> PickOpenFileAsync(string extension)
    {
        if (owner is null) return null;
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List
        };
        picker.FileTypeFilter.Add(extension);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(owner));
        return await picker.PickSingleFileAsync();
    }

    public async Task<StorageFile?> PickSaveFileAsync(
        string suggestedName,
        string typeLabel,
        string extension)
    {
        if (owner is null) return null;
        var picker = new FileSavePicker { SuggestedFileName = suggestedName };
        picker.FileTypeChoices.Add(typeLabel, new List<string> { extension });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(owner));
        return await picker.PickSaveFileAsync();
    }

    public async Task<StorageFolder?> PickFolderAsync()
    {
        if (owner is null) return null;
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(owner));
        return await picker.PickSingleFolderAsync();
    }
}
