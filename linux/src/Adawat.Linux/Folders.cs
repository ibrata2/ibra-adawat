using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Adawat.Core;

namespace Adawat.Linux;

/// <summary>Asks for a folder with the desktop's own dialog (the XDG portal on GNOME and KDE).</summary>
public static class Folders
{
    public static async Task<string?> Choose(IReadOnlyList<string> files)
    {
        var owner = new Window { Width = 1, Height = 1, ShowInTaskbar = false, WindowDecorations = WindowDecorations.None, Opacity = 0, Title = "Adawat" };
        owner.Show();
        try
        {
            var name = files.Count == 1 ? Path.GetFileName(files[0]) : L10n.T($"{files.Count} items");
            var picked = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L10n.T($"Choose where to move {name}"), AllowMultiple = false });
            return picked.FirstOrDefault()?.TryGetLocalPath();
        }
        finally { owner.Close(); }
    }
}
