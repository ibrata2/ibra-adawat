using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WinRT;

namespace Adawat.App;

/// <summary>Windows' own Share sheet (Nearby Sharing, Mail, Teams and the rest), the counterpart of AirDrop on the Mac.</summary>
public static class ShareUI
{
    [ComImport, Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow(IntPtr window, ref Guid riid);
        void ShowShareUIForWindow(IntPtr window);
    }

    private static readonly Guid DataTransferManagerId = new(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);
    private static Window? _host;

    public static void Share(IReadOnlyList<string> files)
    {
        // The Share sheet belongs to a window; a tiny invisible one serves.
        _host ??= new Window { Width = 1, Height = 1, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = true, Left = -100, Top = -100, AllowsTransparency = true, Opacity = 0 };
        _host.Show();
        var handle = new WindowInteropHelper(_host).Handle;
        var interop = DataTransferManager.As<IDataTransferManagerInterop>();
        var id = DataTransferManagerId;
        var manager = MarshalInterface<DataTransferManager>.FromAbi(interop.GetForWindow(handle, ref id));
        void OnRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            sender.DataRequested -= OnRequested;
            var deferral = args.Request.GetDeferral();
            args.Request.Data.Properties.Title = files.Count == 1 ? Path.GetFileName(files[0]) : Core.L10n.T($"{files.Count} files");
            _ = Fill(args.Request.Data, files, deferral);
        }
        manager.DataRequested += OnRequested;
        interop.ShowShareUIForWindow(handle);
    }

    private static async Task Fill(DataPackage data, IReadOnlyList<string> files, DataRequestDeferral deferral)
    {
        try
        {
            var items = new List<IStorageItem>();
            foreach (var file in files)
                items.Add(Directory.Exists(file) ? await StorageFolder.GetFolderFromPathAsync(file) : await StorageFile.GetFileFromPathAsync(file));
            data.SetStorageItems(items);
        }
        finally
        {
            deferral.Complete();
        }
    }
}
