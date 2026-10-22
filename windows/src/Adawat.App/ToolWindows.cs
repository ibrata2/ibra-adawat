using System.Windows;

namespace Adawat.App;

/// <summary>Every options window by the name the catalog gives it.</summary>
public static class ToolWindows
{
    private static readonly Dictionary<string, Func<IReadOnlyList<string>, Window?>> Makers = new();

    public static IEnumerable<string> Names => Makers.Keys;

    public static void Register(string name, Func<IReadOnlyList<string>, Window?> maker) => Makers[name] = maker;

    /// <summary>Opens the window for <paramref name="name"/>; false when there is none, so the one-step version runs instead.</summary>
    public static bool Open(string name, IReadOnlyList<string> files)
    {
        EnsureRegistered();
        if (!Makers.TryGetValue(name, out var make)) return false;
        try
        {
            var window = make(files);
            if (window == null) return true;
            if (window is ToolWindow tool) tool.Present(); else { window.Show(); window.Activate(); }
            return true;
        }
        catch (Core.AdawatException e)
        {
            Hud.Shared.ShowMessage(e.Message, Hud.Style.Failure);
            return true;
        }
    }

    public static Window? Make(string name, IReadOnlyList<string> files)
    {
        EnsureRegistered();
        return Makers.TryGetValue(name, out var make) ? make(files) : null;
    }

    private static bool _registered;

    private static void EnsureRegistered()
    {
        if (_registered) return;
        _registered = true;
        Windows.Catalog.RegisterAll();
    }
}
