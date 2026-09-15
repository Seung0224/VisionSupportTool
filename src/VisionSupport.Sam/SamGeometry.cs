using System.Windows;

namespace VisionSupport.Sam;

public static class SamGeometry
{
    /// <summary>SAM 3's input side, from the tracker export's preprocessor_config.json. The export
    /// takes this size only.</summary>
    public const int ModelSide = 1008;

    /// <summary>
    /// The square the encoder is shown: <see cref="ModelSide"/> pixels centred on the cursor, pushed
    /// back inside the monitor so it never reaches off-screen.
    ///
    /// Native resolution rather than a shrunken monitor. The encoder costs the same whatever it is
    /// given, and a mask edge is only as sharp as the pixels behind it. The price is that an object
    /// larger than the square is cut at its border.
    ///
    /// A monitor smaller than the square gives a crop the size of the monitor on that axis.
    /// </summary>
    public static PixelRect CropAround(int cursorX, int cursorY, PixelRect monitor)
    {
        int width = Math.Min(ModelSide, monitor.Width);
        int height = Math.Min(ModelSide, monitor.Height);
        int x = Math.Clamp(cursorX - width / 2, monitor.X, monitor.Right - width);
        int y = Math.Clamp(cursorY - height / 2, monitor.Y, monitor.Bottom - height);
        return new PixelRect(x, y, width, height);
    }

    /// <summary>A screen point in the encoder's 1024 space. The crop is stretched to fill that
    /// space, so each axis scales on its own.</summary>
    public static (float X, float Y) ToModel(int screenX, int screenY, PixelRect crop)
        => ((screenX - crop.X) * (float)ModelSide / crop.Width,
            (screenY - crop.Y) * (float)ModelSide / crop.Height);

    /// <summary>A physical-pixel rectangle as DIPs inside the overlay window covering
    /// <paramref name="monitor"/>, whose scale factor is <paramref name="scale"/>.</summary>
    public static Rect ToDips(PixelRect rect, PixelRect monitor, double scale)
        => new((rect.X - monitor.X) / scale, (rect.Y - monitor.Y) / scale, rect.Width / scale, rect.Height / scale);
}
