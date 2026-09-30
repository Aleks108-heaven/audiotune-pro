using System.Drawing;
using System.Drawing.Drawing2D;

namespace AudioTunePro.App.Assets;

/// <summary>
/// Draws AudioTune Pro's icon (a simple equalizer-bars glyph) at runtime. This is only the
/// fallback for the tray icon if the icon embedded in the EXE (Assets/icon.ico) can't be
/// extracted; the colors are the design-system tokens surface-panel and accent-teal.
/// </summary>
public static class IconFactory
{
    public static Icon CreateAppIcon(int size = 32)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var bg = new SolidBrush(Color.FromArgb(255, 0x16, 0x1A, 0x22) /* surface-panel */);
            g.FillEllipse(bg, 0, 0, size, size);

            // Four equalizer bars of varying height, teal accent.
            var barColor = Color.FromArgb(255, 0x4E, 0xCC, 0xC0) /* accent-teal */;
            using var barBrush = new SolidBrush(barColor);
            float[] heights = { 0.35f, 0.75f, 0.5f, 0.9f };
            float margin = size * 0.16f;
            float usableWidth = size - margin * 2;
            float barWidth = usableWidth / (heights.Length * 1.6f);
            float gap = barWidth * 0.6f;
            float x = margin;

            foreach (var hFrac in heights)
            {
                float barHeight = size * 0.7f * hFrac;
                float y = size - margin - barHeight;
                g.FillRectangle(barBrush, x, y, barWidth, barHeight);
                x += barWidth + gap;
            }
        }

        nint hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        NativeMethods.DestroyIcon(hIcon);
        return icon;
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool DestroyIcon(nint hIcon);
    }
}
