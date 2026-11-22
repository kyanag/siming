using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Siming.App;

internal sealed class GeneratedIcon : IDisposable
{
    private IntPtr _handle;

    public GeneratedIcon(Icon icon, IntPtr handle)
    {
        Icon = icon;
        _handle = handle;
    }

    public Icon Icon { get; }

    public void Dispose()
    {
        Icon.Dispose();
        if (_handle != IntPtr.Zero)
        {
            DestroyIcon(_handle);
            _handle = IntPtr.Zero;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

/// <summary>
/// 托盘图标：字形固定为「司」，空闲单色，运行中绿色。
/// </summary>
internal static class TrayIconFactory
{
    private static readonly Color RunningGreen = Color.FromArgb(0x2E, 0x9E, 0x44);

    public static GeneratedIcon Create(bool running)
    {
        var size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        var light = IsLightTaskbar();
        var foreground = running
            ? RunningGreen
            : light ? Color.FromArgb(26, 26, 26) : Color.White;
        var outline = running
            ? (light ? Color.FromArgb(16, 70, 30) : Color.White)
            : (light ? Color.White : Color.Black);

        // 先按 4 倍尺寸绘制再缩小：GDI+ 在 16px 画布上直接画汉字时，
        // 行高略大于画布会导致整字被裁掉（图标全透明）。
        const int supersample = 4;
        var canvas = size * supersample;

        using var large = new Bitmap(canvas, canvas);
        using (var graphics = Graphics.FromImage(large))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);

            using var font = new Font("Microsoft YaHei UI", canvas * 0.78f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };

            var bounds = new RectangleF(0, 0, canvas, canvas);
            var offset = Math.Max(1, canvas / 16);

            using var outlineBrush = new SolidBrush(outline);
            foreach (var (dx, dy) in new[] { (-offset, 0), (offset, 0), (0, -offset), (0, offset) })
            {
                graphics.DrawString("司", font, outlineBrush, new RectangleF(dx, dy, canvas, canvas), format);
            }

            using var foregroundBrush = new SolidBrush(foreground);
            graphics.DrawString("司", font, foregroundBrush, bounds, format);
        }

        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            graphics.DrawImage(large, new Rectangle(0, 0, size, size));
        }

        var handle = bitmap.GetHicon();
        var icon = Icon.FromHandle(handle);
        return new GeneratedIcon(icon, handle);
    }

    public static bool IsLightTaskbar()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "SystemUsesLightTheme",
                0);
            return value is int integer && integer != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

internal static class StatusImages
{
    public static ImageList Create()
    {
        var size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        var list = new ImageList
        {
            ImageSize = new Size(size, size),
            ColorDepth = ColorDepth.Depth32Bit,
            TransparentColor = Color.Transparent,
        };

        list.Images.Add("stopped", Circle(Color.FromArgb(0x8A, 0x8A, 0x8A), size));
        list.Images.Add("starting", Circle(Color.FromArgb(0xD9, 0xA4, 0x00), size));
        list.Images.Add("running", Circle(Color.FromArgb(0x2E, 0x9E, 0x44), size));
        list.Images.Add("stopping", Circle(Color.FromArgb(0xC2, 0x6A, 0x00), size));
        return list;
    }

    private static Bitmap Circle(Color color, int size)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        var padding = Math.Max(2f, size * 0.16f);
        using var brush = new SolidBrush(color);
        graphics.FillEllipse(brush, padding, padding, size - (padding * 2), size - (padding * 2));
        return bitmap;
    }
}
