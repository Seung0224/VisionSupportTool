namespace VisionSupport.Sam;

/// <summary>
/// A rectangle in physical screen pixels. Everything in this feature is kept in physical pixels -
/// the cursor, the monitors, the crops, the captures - and DIPs only appear at the moment a WPF
/// window is sized, so a mixed-DPI desk cannot put a mask a few pixels off its object.
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;
}
