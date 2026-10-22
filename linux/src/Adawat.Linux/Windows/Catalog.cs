namespace Adawat.Linux.Windows;

/// <summary>Registers each tool window under the name the operation catalog uses.</summary>
public static class Catalog
{
    public static void RegisterAll()
    {
        // Images
        ToolWindows.Register("compress", CompressWindow.Make);
        ToolWindows.Register("metadata", MetadataWindow.Make);
        ToolWindows.Register("edit", EditPhotoWindow.Make);
        ToolWindows.Register("annotate", AnnotateWindow.Make);
        ToolWindows.Register("frame", FrameWindow.Make);
        ToolWindows.Register("crop", CropWindow.Make);
        ToolWindows.Register("redact", RedactWindow.Make);
        ToolWindows.Register("collage", CollageWindow.Make);

        // PDF
        ToolWindows.Register("organize", OrganizeWindow.Make);
        ToolWindows.Register("merge", OrderWindow.MergePdfs);

        // Video
        ToolWindows.Register("trim", TrimWindow.Make);
        ToolWindows.Register("crop-video", CropVideoWindow.Make);
        ToolWindows.Register("speed", SpeedWindow.Make);
        ToolWindows.Register("frames", FramesWindow.Make);
        ToolWindows.Register("split", SplitWindow.Make);
        ToolWindows.Register("redact-video", RedactVideoWindow.Make);
        ToolWindows.Register("join", OrderWindow.JoinVideos);

        // Audio
        ToolWindows.Register("normalize", NormalizeWindow.Make);
        ToolWindows.Register("visualizer", VisualizerWindow.Make);
        ToolWindows.Register("channels", ChannelsWindow.Make);
        ToolWindows.Register("bleep", BleepWindow.Make);

        // Text
        ToolWindows.Register("qr", QrWindow.Make);
    }
}
