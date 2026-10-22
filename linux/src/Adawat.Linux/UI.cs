using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Adawat.Core;

namespace Adawat.Linux;

/// <summary>Small builders for the controls every window uses, styled like the Mac and Windows apps: soft grey buttons, rows
/// with the title at the start and the control at the end, segmented choices, and a violet accent.</summary>
public static class UI
{
    public static TextBlock Text(string text, double size = 13, FontWeight? weight = null, IBrush? color = null, bool wrap = false) => new()
    {
        Text = L10n.T(text), FontFamily = Look.Font, FontSize = size, FontWeight = weight ?? FontWeight.Normal, Foreground = color ?? Look.Ink,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock Caption(string text) => Text(text, 11.5, color: Look.Muted, wrap: true);

    /// <summary>An icon, named by its Segoe Fluent code point as in the shared catalog.</summary>
    public static TextBlock Icon(string glyph, double size = 13, IBrush? color = null) => new()
    {
        Text = Icons.Glyph(glyph), FontFamily = Look.Icons, FontSize = size + 2, Foreground = color ?? Look.Ink, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>The soft grey tool button; <paramref name="primary"/> fills it with the accent.</summary>
    public static SoftButton Button(string title, Action click, bool primary = false, string? icon = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (icon != null) content.Children.Add(Icon(icon, 12, primary ? Look.White : Look.Ink));
        content.Children.Add(Text(title, 13, FontWeight.Medium, primary ? Look.White : Look.Ink));
        var button = new SoftButton(primary, round: false) { Child = content };
        button.Click += click;
        return button;
    }

    public static SoftButton IconButton(string glyph, Action click, string help, double size = 26)
    {
        var button = new SoftButton(false, round: true) { Child = Icon(glyph, 11), Width = size, Height = size };
        ToolTip.SetTip(button, L10n.T(help));
        button.Click += click;
        return button;
    }

    /// <summary>A row: title at the start, control at the end.</summary>
    public static Grid Row(string title, Control control, double minHeight = 30)
    {
        var grid = new Grid { MinHeight = minHeight, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(Text(title));
        Grid.SetColumn(control, 2);
        control.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(control);
        return grid;
    }

    /// <summary>A segmented control: a row of choices, one selected.</summary>
    public static Border Segmented<T>(IReadOnlyList<(T Value, string Title)> options, T selected, Action<T> changed)
    {
        var panel = new UniformGrid { Rows = 1, Columns = options.Count };
        var cells = new List<Border>();
        void Select(int index)
        {
            for (var i = 0; i < cells.Count; i++)
            {
                cells[i].Background = i == index ? Look.White : Look.Clear;
                cells[i].BoxShadow = i == index ? Look.Shadow(3, 1, 0.15) : default;
                ((TextBlock)cells[i].Child!).FontWeight = i == index ? FontWeight.SemiBold : FontWeight.Normal;
            }
        }
        for (var index = 0; index < options.Count; index++)
        {
            var i = index;
            var cell = new Border
            {
                CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 4), Margin = new Thickness(2), Cursor = new Cursor(StandardCursorType.Hand),
                Background = Look.Clear,
                Child = new TextBlock { Text = L10n.T(options[index].Title), FontFamily = Look.Font, FontSize = 12.5, Foreground = Look.Ink, HorizontalAlignment = HorizontalAlignment.Center },
            };
            cell.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                Select(i);
                changed(options[i].Value);
            };
            cells.Add(cell);
            panel.Children.Add(cell);
        }
        Select(Math.Max(0, options.Select(o => o.Value).ToList().IndexOf(selected)));
        return new Border { Background = Look.ButtonFill, CornerRadius = new CornerRadius(8), Child = panel };
    }

    public static CheckBox Check(string title, bool value, Action<bool> changed)
    {
        var box = new CheckBox { Content = Text(title), IsChecked = value, VerticalAlignment = VerticalAlignment.Center, FontFamily = Look.Font };
        box.IsCheckedChanged += (_, _) => changed(box.IsChecked == true);
        return box;
    }

    /// <summary>A slider with its value written beside it and a reset arrow.</summary>
    public static StackPanel Slider(double min, double max, double value, Action<double> changed, Func<double, string>? format = null, double? reset = null, double width = 160, double step = 0)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Width = width, VerticalAlignment = VerticalAlignment.Center };
        if (step > 0) { slider.TickFrequency = step; slider.IsSnapToTickEnabled = true; }
        var label = Text(format?.Invoke(value) ?? Math.Round(value).ToString(), 12, color: Look.Muted);
        label.MinWidth = 48;
        label.TextAlignment = TextAlignment.Right;
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty) return;
            label.Text = format?.Invoke(slider.Value) ?? Math.Round(slider.Value).ToString();
            changed(slider.Value);
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(slider);
        row.Children.Add(label);
        if (reset is { } r) row.Children.Add(IconButton("", () => slider.Value = r, "Reset", 22));
        return row;
    }

    public static ComboBox Picker<T>(IReadOnlyList<(T Value, string Title)> options, T selected, Action<T> changed)
    {
        var box = new ComboBox { FontFamily = Look.Font, FontSize = 12.5, MinWidth = 120, VerticalAlignment = VerticalAlignment.Center };
        foreach (var option in options) box.Items.Add(L10n.T(option.Title));
        box.SelectedIndex = Math.Max(0, options.Select(o => o.Value).ToList().IndexOf(selected));
        box.SelectionChanged += (_, _) => { if (box.SelectedIndex >= 0) changed(options[box.SelectedIndex].Value); };
        return box;
    }

    public static Border Divider() => new() { Height = 1, Background = Look.Hairline, Margin = new Thickness(0, 8) };

    public static T Pad<T>(this T element, double all) where T : Control { element.Margin = new Thickness(all); return element; }
    public static T Pad<T>(this T element, double horizontal, double vertical) where T : Control { element.Margin = new Thickness(horizontal, vertical); return element; }
}

/// <summary>A rounded button that lightens on hover and darkens while pressed, like the Windows app's soft buttons.</summary>
public sealed class SoftButton : Border
{
    private readonly bool _primary;
    private bool _over, _down;

    public event Action? Click;

    public SoftButton(bool primary, bool round)
    {
        _primary = primary;
        CornerRadius = new CornerRadius(round ? 13 : 7);
        Padding = round ? new Thickness(0) : new Thickness(12, 4);
        MinHeight = 26;
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = true;
        Paint();
        PointerEntered += (_, _) => { _over = true; Paint(); };
        PointerExited += (_, _) => { _over = false; _down = false; Paint(); };
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _down = true;
            Paint();
            e.Handled = true;
        };
        PointerReleased += (_, e) =>
        {
            var fire = _down && _over && IsEnabled;
            _down = false;
            Paint();
            e.Handled = true;
            if (fire) Click?.Invoke();
        };
        KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space && IsEnabled) { e.Handled = true; Click?.Invoke(); } };
        PropertyChanged += (_, e) => { if (e.Property == IsEnabledProperty) Paint(); };
    }

    /// <summary>Changes the label of a button made by <see cref="UI.Button"/>.</summary>
    public string Title
    {
        set
        {
            if (Child is StackPanel panel && panel.Children.OfType<TextBlock>().LastOrDefault() is { } text) text.Text = L10n.T(value);
        }
    }

    private void Paint()
    {
        Opacity = IsEnabled ? 1 : 0.4;
        Background = _primary
            ? (_down ? Look.AccentPressed : _over ? Look.AccentHover : Look.Accent)
            : (_down ? Look.ButtonPressed : _over ? Look.ButtonHover : Look.ButtonFill);
        if (Child != null) Child.HorizontalAlignment = HorizontalAlignment.Center;
        if (Child != null) Child.VerticalAlignment = VerticalAlignment.Center;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ChildProperty) Paint();
    }
}

/// <summary>
/// The frame every tool window shares: a round close button and centred title on top, the tool's controls in the middle,
/// and a bar of actions at the bottom (secondary at the start, primary at the end). The window draws its own rounded
/// corners and shadow, the same on every desktop.
/// </summary>
public class ToolWindow : Window
{
    private readonly ContentControl _body = new() { Margin = new Thickness(16) };
    private readonly DockPanel _footer = new() { LastChildFill = false, Margin = new Thickness(16, 0), Height = 52 };
    private readonly StackPanel _leading = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _trailing = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private const double ShadowRoom = 14;

    public ToolWindow(string title, double width)
    {
        Title = L10n.T(title);
        Width = width + ShadowRoom * 2;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Look.Clear;
        FontFamily = Look.Font;
        FlowDirection = Look.Direction;
        Icon = AppIcon.Window;
        var header = new Grid { Height = 44, Background = Look.Clear };
        header.Children.Add(new TextBlock
        {
            Text = Title, FontFamily = Look.Font, FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Look.Ink,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
        });
        var close = UI.IconButton("", Close, "Close", 24);
        close.HorizontalAlignment = HorizontalAlignment.Left;
        close.VerticalAlignment = VerticalAlignment.Center;
        close.Margin = new Thickness(12, 0, 0, 0);
        header.Children.Add(close);
        header.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        DockPanel.SetDock(_leading, Dock.Left);
        DockPanel.SetDock(_trailing, Dock.Right);
        _footer.Children.Add(_leading);
        _footer.Children.Add(_trailing);
        var stack = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        stack.Children.Add(header);
        var line1 = new Border { Height = 1, Background = Look.Hairline };
        DockPanel.SetDock(line1, Dock.Top);
        stack.Children.Add(line1);
        DockPanel.SetDock(_footer, Dock.Bottom);
        stack.Children.Add(_footer);
        var line2 = new Border { Height = 1, Background = Look.Hairline };
        DockPanel.SetDock(line2, Dock.Bottom);
        stack.Children.Add(line2);
        stack.Children.Add(_body);
        Content = new Border
        {
            Margin = new Thickness(ShadowRoom), CornerRadius = new CornerRadius(12), Background = Look.Surface, ClipToBounds = true,
            BorderBrush = Look.Hairline, BorderThickness = new Thickness(1), BoxShadow = Look.Shadow(24, 6, 0.22), Child = stack,
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    public Control Body { set => _body.Content = value; }

    public void Leading(params Control[] items) { foreach (var item in items) { item.Margin = new Thickness(0, 0, 8, 0); _leading.Children.Add(item); } }

    public void Trailing(params Control[] items) { foreach (var item in items) { item.Margin = new Thickness(8, 0, 0, 0); _trailing.Children.Add(item); } }

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
