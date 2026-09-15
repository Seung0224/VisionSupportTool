namespace VisionSupport.Sam;

/// <summary>What the last encoding was made from: the crop, where the cursor was, and when.</summary>
public readonly record struct EncodeStamp(PixelRect Crop, int CursorX, int CursorY, DateTime At);

/// <summary>
/// When the encoder runs again. It is the expensive half - around 400ms on SAM 3 against the
/// decoder's 10 - so hovering reuses one encoding and only re-decodes, until that encoding has gone
/// stale. The encoder has its own thread, so a stale encoding never holds the hover up; this only
/// decides how busy the GPU is kept.
/// </summary>
public static class EncodePolicy
{
    public const int MoveThreshold = 128;

    /// <summary>
    /// The screen under the overlay is live; this is how old a picture of it may get. A second
    /// rather than less: an encoding takes about 0.4s, and refreshing any faster would leave the
    /// GPU encoding without a pause for as long as the mode is up.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(1);

    /// <summary>
    /// True when there is no encoding yet, when it is <see cref="MaxAge"/> old, when the cursor has
    /// left its crop, or when the cursor has moved more than <see cref="MoveThreshold"/> on either
    /// axis since it was made.
    ///
    /// The move is measured from where the cursor was, not from the crop's centre. Near a monitor
    /// edge the crop is pushed inwards and its centre can be 500px from the cursor, which measured
    /// from the centre would re-encode on every pass.
    /// </summary>
    public static bool NeedsEncode(EncodeStamp? last, int cursorX, int cursorY, DateTime now)
        => last is not { } stamp
           || now - stamp.At >= MaxAge
           || !stamp.Crop.Contains(cursorX, cursorY)
           || Math.Abs(cursorX - stamp.CursorX) > MoveThreshold
           || Math.Abs(cursorY - stamp.CursorY) > MoveThreshold;
}
