using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClickStat.Infrastructure.Data.Model;

namespace ClickStat.Presentation.Services;

public static class CursorHeatmapRenderer
{
    private const int LegacyGridWidth = 80;
    private const int LegacyGridHeight = 45;
    private static readonly (double at, byte r, byte g, byte b)[] ColorStops =
    [
        (0.0, 24, 27, 52), (0.25, 67, 56, 142), (0.5, 146, 66, 178),
        (0.75, 236, 91, 101), (1.0, 255, 205, 88)
    ];

    public static BitmapSource Render(CursorHeatmap map)
    {
        int gridWidth = map.GridWidth;
        int gridHeight = map.GridHeight;
        double scale = Math.Min(1.0, 1920.0 / Math.Max(map.Width, map.Height));
        int width = Math.Max(1, (int)Math.Round(map.Width * scale));
        int height = Math.Max(1, (int)Math.Round(map.Height * scale));
        var values = new double[gridWidth * gridHeight];
        foreach (var cell in map.Cells)
            if (cell.GridX >= 0 && cell.GridX < gridWidth && cell.GridY >= 0 && cell.GridY < gridHeight)
                values[cell.GridY * gridWidth + cell.GridX] += cell.Seconds * 3 + cell.Visits;

        // Keep the older 80 x 45 history visible without claiming pixel accuracy it never had.
        foreach (var cell in map.LegacyCells)
        {
            int x0 = cell.GridX * gridWidth / LegacyGridWidth;
            int x1 = Math.Min(gridWidth, (int)Math.Ceiling((cell.GridX + 1.0) * gridWidth / LegacyGridWidth));
            int y0 = cell.GridY * gridHeight / LegacyGridHeight;
            int y1 = Math.Min(gridHeight, (int)Math.Ceiling((cell.GridY + 1.0) * gridHeight / LegacyGridHeight));
            if (x0 < 0 || y0 < 0 || x0 >= x1 || y0 >= y1) continue;
            double share = (cell.Seconds * 3.0 + cell.Visits) / ((x1 - x0) * (y1 - y0));
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                values[y * gridWidth + x] += share;
        }

        var smoothed = new double[values.Length];
        for (int y = 0; y < gridHeight; y++)
        for (int x = 0; x < gridWidth; x++)
        {
            double weighted = 0, weight = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int xx = x + dx, yy = y + dy;
                if (xx < 0 || xx >= gridWidth || yy < 0 || yy >= gridHeight) continue;
                double w = 1.0 / (1 + dx * dx + dy * dy);
                weighted += values[yy * gridWidth + xx] * w;
                weight += w;
            }
            var value = weighted / weight;
            smoothed[y * gridWidth + x] = value;
        }

        var active = smoothed.Where(value => value > 0).OrderBy(value => value).ToArray();
        var reference = active.Length == 0 ? 0 : active[(int)Math.Floor((active.Length - 1) * 0.97)];

        var colors = smoothed.Select(value =>
        {
            double level = reference <= 0 ? 0 : Math.Clamp(Math.Log(1 + value) / Math.Log(1 + reference), 0, 1);
            return HeatColor(level);
        }).ToArray();

        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var (r, g, b) = colors[(y * gridHeight / height) * gridWidth + x * gridWidth / width];
            int offset = (y * width + x) * 4;
            pixels[offset] = b;
            pixels[offset + 1] = g;
            pixels[offset + 2] = r;
            pixels[offset + 3] = 255;
        }

        // Monitor outlines keep the virtual desktop layout legible.
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var bounds = screen.Bounds;
            int left = Math.Clamp((int)((long)(bounds.Left - map.Left) * width / map.Width), 0, width - 1);
            int top = Math.Clamp((int)((long)(bounds.Top - map.Top) * height / map.Height), 0, height - 1);
            int right = Math.Clamp((int)((long)(bounds.Right - map.Left) * width / map.Width) - 1, 0, width - 1);
            int bottom = Math.Clamp((int)((long)(bounds.Bottom - map.Top) * height / map.Height) - 1, 0, height - 1);
            for (int x = left; x <= right; x++)
            {
                SetOutline(pixels, width, x, top);
                SetOutline(pixels, width, x, bottom);
            }
            for (int y = top; y <= bottom; y++)
            {
                SetOutline(pixels, width, left, y);
                SetOutline(pixels, width, right, y);
            }
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32,
            null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static void SetOutline(byte[] pixels, int width, int x, int y)
    {
        int offset = (y * width + x) * 4;
        pixels[offset] = 155;
        pixels[offset + 1] = 160;
        pixels[offset + 2] = 190;
    }

    private static (byte r, byte g, byte b) HeatColor(double level)
    {
        if (level < 0.04) return (17, 21, 38);
        for (int i = 1; i < ColorStops.Length; i++)
        {
            if (level > ColorStops[i].at) continue;
            double t = (level - ColorStops[i - 1].at) / (ColorStops[i].at - ColorStops[i - 1].at);
            byte Blend(byte a, byte b) => (byte)Math.Round(a + (b - a) * t);
            return (Blend(ColorStops[i - 1].r, ColorStops[i].r),
                Blend(ColorStops[i - 1].g, ColorStops[i].g), Blend(ColorStops[i - 1].b, ColorStops[i].b));
        }
        return (255, 205, 88);
    }
}
