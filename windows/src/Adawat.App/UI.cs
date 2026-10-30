using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Adawat.Core;

namespace Adawat.App;

/// <summary>Small builders for the controls every window uses, styled like the Mac app: soft grey buttons, rows with the
/// title at the start and the control at the end, segmented choices, and a violet accent.</summary>
public static class UI
{
    public static TextBlock Text(string text, double size = 13, FontWeight? weight = null, Brush? color = null, bool wrap = false) => new()
    {
        Text = L10n.T(text), FontFamily = Theme.Font, FontSize = size, FontWeight = weight ?? FontWeights.Normal, Foreground = color ?? Theme.Ink,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock Caption(string text) => Text(text, 11.5, color: Theme.Muted, wrap: true);

    public static TextBlock Icon(string glyph, double size = 13, Brush? color = null) => new()
    {
        Text = glyph, FontFamily = Theme.Icons, FontSize = size, Foreground = color ?? Theme.Ink, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>The soft grey tool button; <paramref name="primary"/> fills it with the accent.</summary>
    public static Button Button(string title, Action click, bool primary = false, string? icon = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (icon != null) { content.Children.Add(Icon(icon, 12, primary ? Brushes.White : Theme.Ink)); content.Children.Add(new Border { Width = 6 }); }
        content.Children.Add(Text(title, 13, FontWeights.Medium, primary ? Brushes.White : Theme.Ink));
        var button = new Button { Content = content, Style = ButtonStyle(primary), Cursor = Cursors.Hand };
        button.Click += (_, _) => click();
        return button;
    }

    public static Button IconButton(string glyph, Action click, string help, double size = 26)
    {
        var button = new Button
        {
            Content = Icon(glyph, 11), Width = size, Height = size, Style = ButtonStyle(false, round: true), ToolTip = L10n.T(help), Cursor = Cursors.Hand,
        };
        button.Click += (_, _) => click();
        return button;
    }

    private static readonly Dictionary<(bool, bool), Style> Styles = new();

    private static Style ButtonStyle(bool primary, bool round = false)
    {
        if (Styles.TryGetValue((primary, round), out var cached)) return cached;
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "Back");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(round ? 13 : 7));
        border.SetValue(Border.BackgroundProperty, primary ? Theme.Accent : Theme.ButtonFill);
        border.SetValue(Border.PaddingProperty, round ? new Thickness(0) : new Thickness(12, 4, 12, 4));
        border.SetValue(FrameworkElement.MinHeightProperty, 26.0);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, primary ? Theme.AccentPressed : Theme.ButtonPressed, "Back"));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, primary ? Theme.AccentHover : Theme.ButtonHover, "Back"));
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4));
        template.Triggers.Add(hover);
        template.Triggers.Add(pressed);
        template.Triggers.Add(disabled);
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        Styles[(primary, round)] = style;
        return style;
    }

    /// <summary>A row: title at the start, control at the end.</summary>
    public static Grid Row(string title, UIElement control, double minHeight = 30)
    {
        var grid = new Grid { MinHeight = minHeight };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = Text(title);
        grid.Children.Add(label);
        Grid.SetColumn((UIElement)control, 2);
        grid.Children.Add(control);
        return grid;
    }

    /// <summary>A segmented control: a row of choices, one selected.</summary>
    public static Border Segmented<T>(IReadOnlyList<(T Value, string Title)> options, T selected, Action<T> changed)
    {
        var panel = new UniformGrid { Rows = 1, Columns = options.Count };
        var buttons = new List<Border>();
        void Select(int index)
        {
            for (var i = 0; i < buttons.Count; i++)
            {
                buttons[i].Background = i == index ? Theme.Selected : Brushes.Transparent;
                buttons[i].Effect = i == index ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Direction = 270, Opacity = 0.15 } : null;
                ((TextBlock)buttons[i].Child).FontWeight = i == index ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }
        for (var index = 0; index < options.Count; index++)
        {
            var i = index;
            var cell = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(2), Cursor = Cursors.Hand,
                                    Child = new TextBlock { Text = L10n.T(options[index].Title), FontFamily = Theme.Font, FontSize = 12.5, Foreground = Theme.Ink, HorizontalAlignment = HorizontalAlignment.Center } };
            cell.MouseLeftButtonUp += (_, _) => { Select(i); changed(options[i].Value); };
            buttons.Add(cell);
            panel.Children.Add(cell);
        }
        Select(Math.Max(0, options.Select(o => o.Value).ToList().IndexOf(selected)));
        return new Border { Background = Theme.ButtonFill, CornerRadius = new CornerRadius(8), Child = panel };
    }

    public static CheckBox Check(string title, bool value, Action<bool> changed)
    {
        var box = new CheckBox { Content = Text(title), IsChecked = value, VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontFamily = Theme.Font };
        box.Checked += (_, _) => changed(true);
        box.Unchecked += (_, _) => changed(false);
        return box;
    }

    /// <summary>A slider with its value written beside it and a reset arrow.</summary>
    public static StackPanel Slider(double min, double max, double value, Action<double> changed, Func<double, string>? format = null, double? reset = null, double width = 160, double step = 0)
    {
        var slider = new System.Windows.Controls.Slider { Minimum = min, Maximum = max, Value = value, Width = width, VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true };
        if (step > 0) { slider.TickFrequency = step; slider.IsSnapToTickEnabled = true; }
        var label = Text(format?.Invoke(value) ?? Math.Round(value).ToString(), 12, color: Theme.Muted);
        label.MinWidth = 48;
        label.TextAlignment = TextAlignment.Right;
        slider.ValueChanged += (_, e) => { label.Text = format?.Invoke(e.NewValue) ?? Math.Round(e.NewValue).ToString(); changed(e.NewValue); };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(slider);
        row.Children.Add(label);
        if (reset is { } r) row.Children.Add(IconButton("", () => slider.Value = r, "Reset", 22));
        return row;
    }

    public static ComboBox Picker<T>(IReadOnlyList<(T Value, string Title)> options, T selected, Action<T> changed)
    {
        var box = new ComboBox { FontFamily = Theme.Font, FontSize = 12.5, MinWidth = 120, VerticalAlignment = VerticalAlignment.Center };
        foreach (var option in options) box.Items.Add(L10n.T(option.Title));
        box.SelectedIndex = Math.Max(0, options.Select(o => o.Value).ToList().IndexOf(selected));
        box.SelectionChanged += (_, _) => { if (box.SelectedIndex >= 0) changed(options[box.SelectedIndex].Value); };
        return box;
    }

    public static Border Divider() => new() { Height = 1, Background = Theme.Hairline, Margin = new Thickness(0, 8, 0, 8) };

    public static T Pad<T>(this T element, double all) where T : FrameworkElement { element.Margin = new Thickness(all); return element; }
    public static T Pad<T>(this T element, double horizontal, double vertical) where T : FrameworkElement { element.Margin = new Thickness(horizontal, vertical, horizontal, vertical); return element; }
}

/// <summary>
/// The frame every tool window shares: a round close button and centred title on top, the tool's controls in the middle,
/// and a bar of actions at the bottom (secondary at the start, primary at the end). Windows 11 draws the rounded corners and shadow.
/// </summary>
public class ToolWindow : Window
{
    private readonly ContentControl _body = new();
    private readonly DockPanel _footer = new() { LastChildFill = false, Margin = new Thickness(16, 0, 16, 0), Height = 52 };
    private readonly StackPanel _leading = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _trailing = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

    public ToolWindow(string title, double width)
    {
        Title = L10n.T(title);
        Width = width;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Theme.Apply(this);
        FontFamily = Theme.Font;
        // The window itself stays left-to-right so its title bar is never mirrored; the content follows the language.
        FlowDirection = FlowDirection.LeftToRight;
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/Adawat.ico"));
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 44, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = Theme.UsesBackdrop ? new Thickness(-1) : new Thickness(0, 0, 0, 1), UseAeroCaptionButtons = false });
        var header = new Grid { Height = 44, FlowDirection = FlowDirection.LeftToRight };
        header.Children.Add(new TextBlock { Text = Title, FontFamily = Theme.Font, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Theme.Ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var close = UI.IconButton("", Close, "Close", 24);
        // Close sits at the top right in Arabic and English alike.
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, 0, 12, 0);
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        header.Children.Add(close);
        DockPanel.SetDock(_leading, Dock.Left);
        DockPanel.SetDock(_trailing, Dock.Right);
        _footer.Children.Add(_leading);
        _footer.Children.Add(_trailing);
        var stack = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        stack.Children.Add(header);
        var line1 = new Border { Height = 1, Background = Theme.Hairline };
        DockPanel.SetDock(line1, Dock.Top);
        stack.Children.Add(line1);
        DockPanel.SetDock(_footer, Dock.Bottom);
        stack.Children.Add(_footer);
        var line2 = new Border { Height = 1, Background = Theme.Hairline };
        DockPanel.SetDock(line2, Dock.Bottom);
        stack.Children.Add(line2);
        _body.Margin = new Thickness(16);
        stack.Children.Add(_body);
        stack.FlowDirection = Theme.Direction;
        Content = stack;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        SourceInitialized += (_, _) => Native.RoundCorners(new WindowInteropHelper(this).Handle);
    }

    public UIElement Body { set => _body.Content = value; }

    public void Leading(params UIElement[] items) { foreach (var item in items) { if (item is FrameworkElement f) f.Margin = new Thickness(0, 0, 8, 0); _leading.Children.Add(item); } }

    public void Trailing(params UIElement[] items) { foreach (var item in items) { if (item is FrameworkElement f) f.Margin = new Thickness(8, 0, 0, 0); _trailing.Children.Add(item); } }

    /// <summary>Shows the window in front, as tool windows open from a drop while another app has focus.</summary>
    public void Present()
    {
        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }
}
