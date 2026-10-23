using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Adawat.Core;
using H.NotifyIcon;

namespace Adawat.App;

/// <summary>The icon next to the clock and its menu, the counterpart of the Mac app's menu bar item.</summary>
public sealed class Tray : IDisposable
{
    private readonly TaskbarIcon _icon;

    public Tray()
    {
        var settings = Settings.Current;
        _icon = new TaskbarIcon
        {
            ToolTipText = L10n.T($"Drag files, then press {Key(settings.ConvertKey)}, {Key(settings.ToolsKey)} or {Key(settings.ActionsKey)}"),
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/Adawat.ico")),
            ContextMenu = Menu(),
            NoLeftClickDelay = true,
        };
        _icon.TrayLeftMouseUp += (_, _) => HomeWindow.Present();
        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    public static string Key(ModifierKey key) => key switch { ModifierKey.Shift => "Shift", ModifierKey.Ctrl => "Ctrl", ModifierKey.Alt => "Alt", _ => "—" };

    private static ContextMenu Menu()
    {
        var menu = new ContextMenu { FlowDirection = Theme.Direction, FontFamily = Theme.Font };
        MenuItem Item(string title, Action action, string? icon = null)
        {
            var item = new MenuItem { Header = L10n.T(title) };
            if (icon != null) item.Icon = new TextBlock { Text = icon, FontFamily = Theme.Icons, FontSize = 14 };
            item.Click += (_, _) => action();
            return item;
        }
        var hint = new MenuItem { Header = L10n.T("Right-click any file and choose IBMC"), IsEnabled = false };
        menu.Items.Add(hint);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Open IBMC", () => HomeWindow.Present(), "\uE8A7"));
        menu.Items.Add(Item("New Float", () => Floats.Create([]), ""));
        var closeFloats = Item("Close All Floats", Floats.CloseAll, "");
        menu.Items.Add(closeFloats);
        menu.Items.Add(Item("Create QR Code…", () => ToolWindows.Open("qr", []), ""));
        menu.Items.Add(Item("Download from Link…", Windows.IbraWindows.DownloadLink, "\uE896"));
        menu.Items.Add(new Separator());
        var drag = new MenuItem { Header = L10n.T("Also open the wheel when dragging + a key"), IsCheckable = true, IsChecked = Settings.Current.UseDragWheel };
        drag.Click += (_, _) =>
        {
            Settings.Current.UseDragWheel = drag.IsChecked;
            Settings.Current.Save();
            if (drag.IsChecked && Application.Current is AdawatApp app) app.StartDrags();
            else if (!drag.IsChecked) Hud.Shared.ShowMessage(L10n.T("Turned off after the app restarts"), Hud.Style.Success);
        };
        menu.Items.Add(drag);
        if (RightClick.IsWindows11)
        {
            var classic = new MenuItem { Header = L10n.T("Show IBMC on the first right-click (classic menu)"), IsCheckable = true, IsChecked = RightClick.ClassicMenu };
            classic.Click += (_, _) => RightClick.ClassicMenu = classic.IsChecked;
            menu.Items.Add(classic);
        }
        var style = new MenuItem { Header = L10n.T("Style") };
        foreach (var (id, title) in new[] { ("glass", "Glass"), ("light", "Light"), ("dark", "Dark") })
        {
            var option = new MenuItem { Header = L10n.T(title), IsCheckable = true, IsChecked = Theme.Style == id };
            option.Click += (_, _) =>
            {
                if (id == Theme.Style) { option.IsChecked = true; return; }
                Settings.Current.Style = id;
                Settings.Current.Save();
                AdawatApp.Restart();
            };
            style.Items.Add(option);
        }
        menu.Items.Add(style);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Welcome Guide…", WelcomeWindow.Present, ""));
        menu.Items.Add(Item("Settings…", SettingsWindow.Present, ""));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Exit Adawat", () => { HomeWindow.Quit(); Application.Current.Shutdown(); }, ""));
        menu.Opened += (_, _) => closeFloats.IsEnabled = Floats.Any;
        return menu;
    }

    public void Dispose() => _icon.Dispose();
}
