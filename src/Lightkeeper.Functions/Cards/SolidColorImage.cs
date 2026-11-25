using System.Collections.Concurrent;
using System.Globalization;

namespace Lightkeeper.Functions.Cards;

/// <summary>
/// A one-pixel image of one color as a data URI, used as a card header background where the six Adaptive Card styles have no matching color.
/// A GIF needs no checksum or compression for a single pixel, so the bytes are a fixed layout with the color in its palette.
/// </summary>
public static class SolidColorImage
{
    private const string DataUriPrefix = "data:image/gif;base64,";
    private const int HexColorLength = 7;

    private static readonly byte[] Header = [.. "GIF89a"u8, 1, 0, 1, 0, 0x80, 0, 0];
    private static readonly byte[] Trailer = [0, 0, 0, (byte)',', 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x44, 1, 0, (byte)';'];

    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="hexColor">A color such as <c>#4B5563</c>.</param>
    public static string DataUri(string hexColor) => Cache.GetOrAdd(hexColor, color =>
    {
        if (color.Length != HexColorLength || color[0] != '#' || !int.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            throw new ArgumentException($"'{color}' is not a #RRGGBB color.", nameof(hexColor));
        }

        byte[] gif = [.. Header, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, .. Trailer];
        return DataUriPrefix + Convert.ToBase64String(gif);
    });
}
