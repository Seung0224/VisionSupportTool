namespace VisionSupport.Sam;

/// <summary>
/// The small counter shown beside the cursor for the wheel's granularity level - the only way to
/// tell how many steps there are and which one is showing, since the mask itself does not say.
/// </summary>
internal static class LevelLabel
{
    /// <summary><paramref name="level"/> is 0-based, as <see cref="SamPostprocess.ChooseMask"/>
    /// returns it; the label reads 1-based, "1" being the largest mask.</summary>
    public static string Format(int level, int total) => $"{level + 1}/{total}";
}
