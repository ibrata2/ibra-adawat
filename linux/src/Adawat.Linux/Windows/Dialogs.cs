using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Adawat.Core;

namespace Adawat.Linux.Windows;

/// <summary>The desktop's own open and save dialogs (the XDG portal on GNOME and KDE), for tool windows.</summary>
public static class Dialogs
{
    public static readonly string[] ImagePatterns = ["*.jpg", "*.jpeg", "*.png", "*.heic", "*.webp", "*.avif", "*.tif", "*.tiff", "*.bmp", "*.gif", "*.svg"];

    public static async Task<string?> OpenImage(Window owner, string title)
    {
        var picked = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L10n.T(title), AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(L10n.T("Images")) { Patterns = ImagePatterns }],
        });
        return picked.FirstOrDefault()?.TryGetLocalPath();
    }

    public static async Task<string?> Save(Window owner, string title, string suggestedName, string extension, string typeName)
    {
        var picked = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L10n.T(title), SuggestedFileName = suggestedName, DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(typeName) { Patterns = [$"*.{extension}"] }],
        });
        return picked?.TryGetLocalPath();
    }
}
