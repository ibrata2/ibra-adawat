using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Adawat.Core;
using MagickImage = ImageMagick.MagickImage;
using MagickReadSettings = ImageMagick.MagickReadSettings;
using MagickColor = ImageMagick.MagickColor;
using MagickFormat = ImageMagick.MagickFormat;

namespace Adawat.Linux;

/// <summary>The four-page guide shown on first run: what Adawat does, a practice drag, preferences, and where to find it.</summary>
public static class WelcomeWindow
{
    private static ToolWindow? _open;

    public static void Present()
    {
        if (_open != null) { _open.Activate(); return; }
        _open = Make(0);
        _open.Closed += (_, _) =>
        {
            _open = null;
            Settings.Current.HasSeenWelcome = true;
            Settings.Current.Save();
        };
        _open.Present();
    }

    public static ToolWindow Make(int startPage)
    {
        var window = new ToolWindow("Welcome to Adawat", 580);
        var page = startPage;
        var host = new ContentControl { Height = 380 };
        var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var back = UI.Button("Back", () => { }, false);
        var next = UI.Button("Continue", () => { }, true);
        var keycaps = new List<(Border Cap, ModifierKey Key)>();
        void Light(ModifierKey key, bool down)
        {
            foreach (var (cap, k) in keycaps)
            {
                if (k != key) continue;
                cap.Background = down ? Look.Accent : Look.White;
                foreach (var text in ((StackPanel)cap.Child!).Children.OfType<TextBlock>()) text.Foreground = down ? Look.White : Look.Ink;
            }
        }
        static ModifierKey? KeyOf(Key key) => key switch
        {
            Key.LeftShift or Key.RightShift => ModifierKey.Shift, Key.LeftCtrl or Key.RightCtrl => ModifierKey.Ctrl, Key.LeftAlt or Key.RightAlt => ModifierKey.Alt, _ => null,
        };
        window.KeyDown += (_, e) => { if (KeyOf(e.Key) is { } k) Light(k, true); };
        window.KeyUp += (_, e) => { if (KeyOf(e.Key) is { } k) Light(k, false); };
        void Show()
        {
            keycaps.Clear();
            host.Content = page switch { 0 => Intro(), 1 => Practice(keycaps), 2 => Preferences(), _ => Finish() };
            dots.Children.Clear();
            for (var i = 0; i < 4; i++)
                dots.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Margin = new Thickness(3, 0), Background = i == page ? Look.Accent : Look.Hairline });
            back.Opacity = page == 0 ? 0 : 1;
            back.IsHitTestVisible = page != 0;
            next.Title = page == 3 ? "Get Started" : "Continue";
        }
        back.Click += () => { page = Math.Max(0, page - 1); Show(); };
        next.Click += () => { if (page == 3) window.Close(); else { page++; Show(); } };
        window.Body = host;
        window.Leading(back);
        window.Trailing(next);
        window.Leading(dots);
        Show();
        return window;
    }

    private static StackPanel Centered(params Control[] children)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var child in children)
        {
            child.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(child);
        }
        return stack;
    }

    private static Control Intro()
    {
        var icon = new Image { Source = AppIcon.Bitmap(), Width = 112, Height = 112, Margin = new Thickness(0, 0, 0, 18) };
        var title = UI.Text("Welcome to Adawat", 26, FontWeight.Bold);
        var text = UI.Text("Convert, compress and edit any file while you drag it. Everything stays on your computer.", 14, color: Look.Muted, wrap: true);
        text.TextAlignment = TextAlignment.Center;
        text.MaxWidth = 380;
        text.Margin = new Thickness(0, 10, 0, 0);
        return Centered(icon, title, text);
    }

    private static Control Practice(List<(Border, ModifierKey)> keycaps)
    {
        var title = UI.Text("Try it now", 22, FontWeight.Bold);
        title.Margin = new Thickness(0, 0, 0, 22);
        var steps = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var file = PracticeFile();
        var tile = new StackPanel { Cursor = new Cursor(StandardCursorType.Hand), Background = Look.Clear };
        tile.Children.Add(new Image { Source = new Avalonia.Media.Imaging.Bitmap(file), Width = 92, Height = 92 });
        tile.Children.Add(UI.Text(Path.GetFileName(file), 11, color: Look.Muted).Pad(0, 4));
        tile.PointerPressed += async (_, e) =>
        {
            if (!e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed || TopLevel.GetTopLevel(tile) is not { } top) return;
            if (await top.StorageProvider.TryGetFileFromPathAsync(file) is not { } item) return;
            var entry = new Avalonia.Input.DataTransferItem();
            entry.SetFile(item);
            var data = new DataTransfer();
            data.Add(entry);
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
        };
        var first = Centered(Badge("1"), UI.Text("Step 1. Drag the photo.", 13, FontWeight.SemiBold).Pad(0, 8), tile);
        var caps = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight };
        var settings = Settings.Current;
        foreach (var (key, mode) in new[] { (settings.ConvertKey, "Convert"), (settings.ToolsKey, "Tools"), (settings.ActionsKey, "Actions") })
        {
            if (key == ModifierKey.None) continue;
            var inner = new StackPanel();
            var symbol = UI.Text(key switch { ModifierKey.Shift => "⇧", ModifierKey.Ctrl => "Ctrl", _ => "Alt" }, 15, FontWeight.SemiBold);
            symbol.HorizontalAlignment = HorizontalAlignment.Center;
            inner.Children.Add(symbol);
            var label = UI.Text(Tray.Key(key), 10.5, color: Look.Muted);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            inner.Children.Add(label);
            var cap = new Border { Width = 70, Height = 60, CornerRadius = new CornerRadius(10), Background = Look.White, BorderBrush = Look.Hairline, BorderThickness = new Thickness(1), Child = inner, Margin = new Thickness(5, 0), Padding = new Thickness(0, 8, 0, 0) };
            var column = new StackPanel();
            column.Children.Add(cap);
            var caption = UI.Text(mode, 11, color: Look.Muted);
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            caption.Margin = new Thickness(0, 6, 0, 0);
            column.Children.Add(caption);
            caps.Children.Add(column);
            keycaps.Add((cap, key));
        }
        var second = Centered(Badge("2"), UI.Text("Step 2. While dragging, press a key.", 13, FontWeight.SemiBold).Pad(0, 8), caps);
        Grid.SetColumn(second, 1);
        steps.Children.Add(first);
        steps.Children.Add(second);
        var hint = new Border
        {
            Background = Look.Card, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 6), Margin = new Thickness(0, 26, 0, 0),
            Child = UI.Text("The wheel stays open until you drop.", 12, color: Look.Muted),
        };
        var stack = Centered(title, steps, hint);
        if (Desktop.DesktopSupport.Note() is { } note) { note.MaxWidth = 460; note.Margin = new Thickness(0, 12, 0, 0); stack.Children.Add(note); }
        return stack;
    }

    private static Border Badge(string number) => new()
    {
        Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Look.Accent,
        Child = new TextBlock { Text = number, Foreground = Look.White, FontWeight = FontWeight.Bold, FontFamily = Look.Font, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };

    private static Control Preferences()
    {
        var settings = Settings.Current;
        var title = UI.Text("Make it yours", 22, FontWeight.Bold);
        title.Margin = new Thickness(0, 0, 0, 18);
        var card = new StackPanel { Width = 420 };
        var restart = UI.Button("Restart Now", SettingsWindow.Restart);
        restart.IsVisible = false;
        card.Children.Add(UI.Row("Language", UI.Picker([("ar", "العربية"), ("en", "English")], settings.Language, v =>
        {
            settings.Language = v;
            settings.Save();
            restart.IsVisible = v != (L10n.IsArabic ? "ar" : "en");
        })));
        card.Children.Add(restart);
        card.Children.Add(UI.Divider());
        card.Children.Add(UI.Check("Start Adawat when you sign in", Startup.IsEnabled, Startup.Set).Pad(0, 4));
        card.Children.Add(UI.Caption("Adawat lives in the top bar, so it is ready whenever you drag a file."));
        card.Children.Add(UI.Divider());
        card.Children.Add(UI.Check("Sound and haptic feedback", settings.PlaySounds, v => { settings.PlaySounds = v; settings.Save(); }).Pad(0, 4));
        card.Children.Add(UI.Check("Show results in a Float", settings.ShowResultsInFloat, v => { settings.ShowResultsInFloat = v; settings.Save(); }).Pad(0, 4));
        var box = new Border { Background = Look.White, CornerRadius = new CornerRadius(14), Padding = new Thickness(18), BorderBrush = Look.Hairline, BorderThickness = new Thickness(1), Child = card };
        return Centered(title, box);
    }

    private static Control Finish()
    {
        var mark = UI.Icon("", 30, Look.Accent);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        var check = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(36), Background = Look.AccentSoft, Margin = new Thickness(0, 0, 0, 18), Child = mark };
        var title = UI.Text("You're all set", 24, FontWeight.Bold);
        var text = UI.Text("Find the Adawat icon in the top bar for Floats, Settings and this guide.", 14, color: Look.Muted, wrap: true);
        text.TextAlignment = TextAlignment.Center;
        text.MaxWidth = 380;
        text.Margin = new Thickness(0, 10, 0, 0);
        return Centered(check, title, text);
    }

    /// <summary>A small colourful photo to practise on, kept in ~/.config/Adawat so results land beside it.</summary>
    private static string PracticeFile()
    {
        var folder = Path.Combine(Settings.Folder, "Practice");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "Practice Photo.png");
        if (File.Exists(path)) return path;
        var settings = new MagickReadSettings { Width = 600, Height = 600 };
        settings.SetDefine("gradient:angle", "135");
        using var image = new MagickImage("gradient:#7F70FF-#3B2AA8", settings);
        new ImageMagick.Drawing.Drawables().FillColor(new MagickColor("#7CF2C8")).Circle(300, 300, 300, 170).Draw(image);
        image.Write(path, MagickFormat.Png);
        return path;
    }
}
