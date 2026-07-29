using DotNext;
using Meziantou.Framework;
using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;

namespace TheOmenDen.PixelForge.Services;

/// <summary>
/// File and folder pickers.
/// <para>
/// <c>Microsoft.Windows.Storage.Pickers</c>, not <c>Windows.Storage.Pickers</c>. The legacy WinRT
/// pickers need <c>WinRT.Interop.InitializeWithWindow</c> and then silently display no dialog at
/// all in a packaged build even when that call succeeds — the classic "save button does nothing
/// once installed" bug. The WinAppSDK replacement takes a <see cref="WindowId"/> and behaves
/// identically packaged and unpackaged.
/// </para>
/// <para>
/// Results come back as plain filesystem paths, so everything downstream is
/// <see cref="FullPath"/> and <c>System.IO</c> — no <c>StorageFile</c> round trip.
/// </para>
/// </summary>
public sealed class PickerService
{
    /// <summary>
    /// Set once, from <c>MainWindow</c>'s constructor. ViewModels have no XAML sender to pull a
    /// <c>XamlRoot</c> from, so the id is cached rather than passed at every call site.
    /// </summary>
    public WindowId WindowId { get; set; }

    public async Task<Optional<FullPath>> PickFolderAsync()
    {
        var picker = new FolderPicker(WindowId)
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };

        var result = await picker.PickSingleFolderAsync();

        return result is null ? Optional<FullPath>.None : FullPath.FromPath(result.Path);
    }

    public async Task<Optional<FullPath>> PickOpenFileAsync(string extension)
    {
        var picker = new FileOpenPicker(WindowId)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };

        // FileTypeFilter must have at least one entry or the dialog throws.
        picker.FileTypeFilter.Add(extension);

        var result = await picker.PickSingleFileAsync();

        return result is null ? Optional<FullPath>.None : FullPath.FromPath(result.Path);
    }

    public async Task<Optional<FullPath>> PickSaveFileAsync(
        string suggestedName,
        string extension,
        string filterName)
    {
        var picker = new FileSavePicker(WindowId)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName,
        };

        picker.FileTypeChoices.Add(filterName, [extension]);

        var result = await picker.PickSaveFileAsync();

        return result is null ? Optional<FullPath>.None : FullPath.FromPath(result.Path);
    }
}
