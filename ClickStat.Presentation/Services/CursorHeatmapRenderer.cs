using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClickStat.Infrastructure.Data.Model;

namespace ClickStat.Presentation.Services;

public static class CursorHeatmapRenderer
{
    private const int GridWidth = 80;
    private const int GridHeight = 45;
    private const int Width = 640;
    private const int Height = 360;
    private static readonly (double at, byte r, byte g, byte b)[] ColorStops =
    [
        (0.0, 24, 27, 52), (0.25, 67, 56, 142), (0.5, 146, 66, 178),
        (0.75, 236, 91, 101), (1.0, 255, 205, 88)
    ];

    public static BitmapSource Render(CursorHeatmap map)
    {
        var values = new double[GridWidth * GridHeight];
        foreach (var cell in map.Cells)
            if (cell.GridX is >= 0 and < GridWidth && cell.GridY is >= 0 and < GridHeight)
                values[cell.GridY * GridWidth + cell.GridX] += cell.Seconds * 3 + cell.Visits;

        var smoothed = new double[values.Length];
        for (int y = 0; y < GridHeight; y++)
        for (int x = 0; x < GridWidth; x++)
        {
            double weighted = 0, weight = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int xx = x + dx, yy = y + dy;
                if (xx < 0 || xx >= GridWidth || yy < 0 || yy >= GridHeight) continue;
                double w = 1.0 / (1 + dx * dx + dy * dy);
                weighted += values[yy * GridWidth + xx] * w;
                weight += w;
            }
            var value = weighted / weight;
            smoothed[y * GridWidth + x] = value;
        }

        var active = smoothed.Where(value => value > 0).OrderBy(value => value).ToArray();
        var reference = active.Length == 0 ? 0 : active[(int)Math.Floor((active.Length - 1) * 0.97)];

        var pixels = new byte[Width * Height * 4];
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            double value = smoothed[(y * GridHeight / Height) * GridWidth + x * GridWidth / Width];
            double level = reference <= 0 ? 0 : Math.Clamp(Math.Log(1 + value) / Math.Log(1 + reference), 0, 1);
            var (r, g, b) = HeatColor(level);
            int offset = (y * Width + x) * 4;
            pixels[offset] = b;
            pixels[offset + 1] = g;
            pixels[offset + 2] = r;
            pixels[offset + 3] = 255;
        }

        // Monitor outlines keep the virtual desktop layout legible.
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var bounds = screen.Bounds;
            int left = Math.Clamp((int)((long)(bounds.Left - map.Left) * Width / map.Width), 0, Width - 1);
            int top = Math.Clamp((int)((long)(bounds.Top - map.Top) * Height / map.Height), 0, Height - 1);
            int right = Math.Clamp((int)((long)(bounds.Right - map.Left) * Width / map.Width) - 1, 0, Width - 1);
            int bottom = Math.Clamp((int)((long)(bounds.Bottom - map.Top) * Height / map.Height) - 1, 0, Height - 1);
            for (int x = left; x <= right; x++)
            {
                SetOutline(pixels, x, top);
                SetOutline(pixels, x, bottom);
            }
            for (int y = top; y <= bottom; y++)
            {
                SetOutline(pixels, left, y);
                SetOutline(pixels, right, y);
            }
        }

        var bitmap = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32,
            null, pixels, Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static void SetOutline(byte[] pixels, int x, int y)
    {
        int offset = (y * Width + x) * 4;
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
