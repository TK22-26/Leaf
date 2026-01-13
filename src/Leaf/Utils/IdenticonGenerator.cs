using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace Leaf.Utils;

public static class IdenticonGenerator
{
    private const int GridSize = 5;
    private const int MirrorColumns = 3;
    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.Ordinal);
    private static readonly object CacheLock = new();

    public static ImageSource GetIdenticon(string? input, int size, Color? backgroundColor = null)
    {
        var key = NormalizeKey(input);
        var cacheKey = $"{key}|{size}";

        lock (CacheLock)
        {
            if (Cache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var color = CreateColor(hash);
        var cellSize = size / (double)GridSize;

        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            if (backgroundColor.HasValue)
            {
                var backgroundBrush = new SolidColorBrush(backgroundColor.Value);
                backgroundBrush.Freeze();
                dc.DrawRectangle(backgroundBrush, null, new Rect(0, 0, size, size));
            }

            var brush = new SolidColorBrush(color);
            brush.Freeze();

            int bitIndex = 0;
            for (int y = 0; y < GridSize; y++)
            {
                for (int x = 0; x < MirrorColumns; x++)
                {
                    if (GetBit(hash, bitIndex++))
                    {
                        DrawCell(dc, brush, x, y, cellSize);
                        int mirrorX = GridSize - 1 - x;
                        if (mirrorX != x)
                        {
                            DrawCell(dc, brush, mirrorX, y, cellSize);
                        }
                    }
                }
            }
        }

        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();

        lock (CacheLock)
        {
            Cache[cacheKey] = image;
        }

        return image;
    }

    public static Color? GetDefaultBackgroundColor()
    {
        if (Application.Current == null)
        {
            return null;
        }

        var brush = Application.Current.TryFindResource("ControlFillColorDefaultBrush") as SolidColorBrush;
        return brush?.Color;
    }

    private static string NormalizeKey(string? input)
    {
        var key = input?.Trim() ?? string.Empty;
        return string.IsNullOrEmpty(key) ? "unknown" : key.ToLowerInvariant();
    }

    private static bool GetBit(byte[] hash, int bitIndex)
    {
        int byteIndex = 3 + (bitIndex / 8);
        int offset = bitIndex % 8;
        if (byteIndex >= hash.Length)
        {
            return false;
        }

        return ((hash[byteIndex] >> offset) & 1) == 1;
    }

    private static Color CreateColor(byte[] hash)
    {
        byte r = (byte)(64 + (hash[0] % 128));
        byte g = (byte)(64 + (hash[1] % 128));
        byte b = (byte)(64 + (hash[2] % 128));
        return Color.FromRgb(r, g, b);
    }

    private static void DrawCell(DrawingContext dc, Brush brush, int x, int y, double cellSize)
    {
        var rect = new Rect(x * cellSize, y * cellSize, cellSize, cellSize);
        dc.DrawRectangle(brush, null, rect);
    }
}
