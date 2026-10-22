using System.Globalization;

namespace Adawat.Core;

public static class Format
{
    /// <summary>"1.4 MB", in the style Windows shows sizes.</summary>
    public static string Bytes(long bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1000 && unit < units.Length - 1) { value /= 1000; unit++; }
        return unit == 0 ? $"{bytes} {units[0]}" : value.ToString(value < 10 ? "0.#" : "0", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    /// <summary>"1:04.25"</summary>
    public static string Time(double seconds)
    {
        seconds = Math.Max(0, seconds);
        var minutes = (int)seconds / 60;
        return $"{minutes}:{seconds - minutes * 60:00.00}".Replace(',', '.');
    }

    public static double? ParseTime(string text)
    {
        double total = 0;
        foreach (var part in text.Trim().Split(':'))
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return null;
            total = total * 60 + value;
        }
        return total;
    }
}
