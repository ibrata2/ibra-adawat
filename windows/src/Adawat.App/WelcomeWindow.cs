using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Adawat.Core;
using MagickImage = ImageMagick.MagickImage;
using MagickReadSettings = ImageMagick.MagickReadSettings;
using MagickColor = ImageMagick.MagickColor;
using MagickFormat = ImageMagick.MagickFormat;

namespace Adawat.App;

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
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        var keycaps = new List<(Border Cap, int Key)>();
        timer.Tick += (_, _) =>
        {
            foreach (var (cap, key) in keycaps)
            {
                var down = Native.IsDown(key);
                cap.Background = down ? Theme.Accent : Brushes.White;
                foreach (var text in ((StackPanel)cap.Child).Children.OfType<TextBlock>()) text.Foreground = down ? Brushes.White : Theme.Ink;
            }
        };
        void Show()
        {
            keycaps.Clear();
            host.Content = page switch { 0 => Intro(), 1 => Practice(keycaps), 2 => Preferences(), _ => Finish() };
            dots.Children.Clear();
            for (var i = 0; i < 4; i++)
                dots.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Margin = new Thickness(3, 0, 3, 0), Background = i == page ? Theme.Accent : Theme.Hairline });
            back.Visibility = page == 0 ? Visibility.Hidden : Visibility.Visible;
            ((TextBlock)((StackPanel)next.Content).Children[0]).Text = L10n.T(page == 3 ? "Get Started" : "Continue");
            if (page == 1) timer.Start(); else timer.Stop();
        }
        back.Click += (_, _) => { page = Math.Max(0, page - 1); Show(); };
        next.Click += (_, _) => { if (page == 3) window.Close(); else { page++; Show(); } };
        window.Closed += (_, _) => timer.Stop();
        window.Body = host;
        window.Leading(back);
        var footer = new DockPanel { LastChildFill = false };
        window.Trailing(next);
        window.Leading(dots);
        Show();
        return window;
    }

    private static StackPanel Centered(params UIElement[] children)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var child in children)
        {
            if (child is FrameworkElement f) f.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(child);
        }
        return stack;
    }

    private static UIElement Intro()
    {
        var icon = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/AppIcon-256.png")), Width = 112, Height = 112, Margin = new Thickness(0, 0, 0, 18) };
        var title = UI.Text("Welcome to Adawat", 26, FontWeights.Bold);
        title.FontFamily = Theme.Display;
        var text = UI.Text("Convert, compress and edit any file while you drag it. Everything stays on your PC.", 14, color: Theme.Muted, wrap: true);
        text.TextAlignment = TextAlignment.Center;
        text.MaxWidth = 380;
        text.Margin = new Thickness(0, 10, 0, 0);
        return Centered(icon, title, text);
    }

    private static UIElement Practice(List<(Border, int)> keycaps)
    {
        var title = UI.Text("Try it now", 22, FontWeights.Bold);
        title.Margin = new Thickness(0, 0, 0, 22);
        var steps = new Grid();
        steps.ColumnDefinitions.Add(new ColumnDefinition());
        steps.ColumnDefinitions.Add(new ColumnDefinition());
        var file = PracticeFile();
        var tile = new StackPanel { Cursor = System.Windows.Input.Cursors.Hand };
        tile.Children.Add(new Image { Source = ShellThumbnail.For(file, 160) ?? new BitmapImage(new Uri(file)), Width = 92, Height = 92 });
        tile.Children.Add(UI.Text(Path.GetFileName(file), 11, color: Theme.Muted).Pad(0, 4));
        tile.MouseMove += (_, e) =>
        {
            if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
            DragDrop.DoDragDrop(tile, new DataObject(DataFormats.FileDrop, new[] { file }), DragDropEffects.Copy);
        };
        var first = Centered(Badge("1"), UI.Text("Step 1. Drag the photo.", 13, FontWeights.SemiBold).Pad(0, 8), tile);
        var caps = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight };
        var settings = Settings.Current;
        foreach (var (key, mode) in new[] { (settings.ConvertKey, "Convert"), (settings.ToolsKey, "Tools"), (settings.ActionsKey, "Actions") })
        {
            if (key == ModifierKey.None) continue;
            var inner = new StackPanel();
            inner.Children.Add(UI.Text(key switch { ModifierKey.Shift => "⇧", ModifierKey.Ctrl => "Ctrl", _ => "Alt" }, 15, FontWeights.SemiBold).Pad(0, 0));
            ((TextBlock)inner.Children[0]).HorizontalAlignment = HorizontalAlignment.Center;
            var label = UI.Text(Tray.Key(key), 10.5, color: Theme.Muted);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            inner.Children.Add(label);
            var cap = new Border { Width = 70, Height = 60, CornerRadius = new CornerRadius(10), Background = Brushes.White, BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1), Child = inner, Margin = new Thickness(5, 0, 5, 0), Padding = new Thickness(0, 8, 0, 0) };
            var column = new StackPanel();
            column.Children.Add(cap);
            var caption = UI.Text(mode, 11, color: Theme.Muted);
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            caption.Margin = new Thickness(0, 6, 0, 0);
            column.Children.Add(caption);
            caps.Children.Add(column);
            keycaps.Add((cap, key switch { ModifierKey.Shift => Native.VK_SHIFT, ModifierKey.Ctrl => Native.VK_CONTROL, _ => Native.VK_MENU }));
        }
        var second = Centered(Badge("2"), UI.Text("Step 2. While dragging, press a key.", 13, FontWeights.SemiBold).Pad(0, 8), caps);
        Grid.SetColumn(second, 1);
        steps.Children.Add(first);
        steps.Children.Add(second);
        var hint = new Border { Background = Theme.Card, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 26, 0, 0),
                                Child = UI.Text("The wheel stays open until you drop.", 12, color: Theme.Muted) };
        return Centered(title, steps, hint);
    }

    private static Border Badge(string number) => new()
    {
        Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Theme.Accent,
        Child = new TextBlock { Text = number, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontFamily = Theme.Font, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };

    private static UIElement Preferences()
    {
        var settings = Settings.Current;
        var title = UI.Text("Make it yours", 22, FontWeights.Bold);
        title.Margin = new Thickness(0, 0, 0, 18);
        var card = new StackPanel { Width = 420 };
        var restart = UI.Button("Restart Now", () =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
            Application.Current.Shutdown();
        });
        restart.Visibility = Visibility.Collapsed;
        card.Children.Add(UI.Row("Language", UI.Picker([("ar", "العربية"), ("en", "English")], settings.Language, v =>
        {
            settings.Language = v;
            settings.Save();
            restart.Visibility = v != (L10n.IsArabic ? "ar" : "en") ? Visibility.Visible : Visibility.Collapsed;
        })));
        card.Children.Add(restart);
        card.Children.Add(UI.Divider());
        card.Children.Add(UI.Check("Start Adawat when Windows starts", Startup.IsEnabled, Startup.Set).Pad(0, 4));
        card.Children.Add(UI.Caption("Adawat lives next to the clock, so it is ready whenever you drag a file."));
        card.Children.Add(UI.Divider());
        card.Children.Add(UI.Check("Sound and haptic feedback", settings.PlaySounds, v => { settings.PlaySounds = v; settings.Save(); }).Pad(0, 4));
        card.Children.Add(UI.Check("Show results in a Float", settings.ShowResultsInFloat, v => { settings.ShowResultsInFloat = v; settings.Save(); }).Pad(0, 4));
        var box = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(14), Padding = new Thickness(18), BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1), Child = card };
        return Centered(title, box);
    }

    private static UIElement Finish()
    {
        var check = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(36), Background = Theme.AccentSoft, Margin = new Thickness(0, 0, 0, 18),
                                 Child = UI.Icon("", 30, Theme.Accent) };
        ((TextBlock)check.Child).HorizontalAlignment = HorizontalAlignment.Center;
        var title = UI.Text("You're all set", 24, FontWeights.Bold);
        var text = UI.Text("Find the Adawat icon next to the clock for Floats, Settings and this guide.", 14, color: Theme.Muted, wrap: true);
        text.TextAlignment = TextAlignment.Center;
        text.MaxWidth = 380;
        text.Margin = new Thickness(0, 10, 0, 0);
        return Centered(check, title, text);
    }

    /// <summary>A small colourful photo to practise on, kept in %APPDATA% so results land beside it.</summary>
    private static string PracticeFile()
    {
        var folder = Path.Combine(Settings.Folder, "Practice");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "Practice Photo.png");
        if (File.Exists(path)) return path;
        var settings = new MagickReadSettings { Width = 600, Height = 600 };
        settings.SetDefine("gradient:angle", "135");
        using var image = new MagickImage("gradient:#7F70FF-#3B2AA8", settings);
        new ImageMagick.Drawing.Drawables().FillColor(new MagickColor("#E3192B")).Circle(300, 300, 300, 170).Draw(image);
        image.Write(path, MagickFormat.Png);
        return path;
    }
}
