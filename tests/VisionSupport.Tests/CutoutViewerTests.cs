using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VisionSupport.Sam;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// What happens to a cutout after the pick: how big the viewer shows it, and that saving and copying
/// keep what makes it a cutout - the transparent background.
/// </summary>
public class CutoutViewerTests
{
    [Theory]
    [InlineData(800, 200, 400, 100)]
    [InlineData(300, 900, 133.333, 400)]
    [InlineData(120, 80, 120, 80)]
    public void The_viewer_shrinks_a_large_cutout_to_400_and_never_enlarges_a_small_one(
        int width, int height, double expectedWidth, double expectedHeight)
    {
        (double shownWidth, double shownHeight) = CutoutViewer.DisplaySize(width, height);

        Assert.Equal(expectedWidth, shownWidth, 3);
        Assert.Equal(expectedHeight, shownHeight, 3);
    }

    [Fact]
    public void The_default_file_name_carries_the_time_it_was_cut()
        => Assert.Equal("누끼_20260915_113005.png", CutoutFiles.DefaultName(new DateTime(2026, 9, 15, 11, 30, 5)));

    [Fact]
    public void A_saved_png_keeps_the_transparency()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "cut.png");

        CutoutFiles.SavePng(RedThenClear(), path);

        using FileStream stream = File.OpenRead(path);
        byte[] pixels = DecodePng(stream, out int width, out int height);
        Assert.Equal((2, 1), (width, height));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, pixels[0..4]);
        Assert.Equal(0, pixels[7]);
    }

    /// <summary>Office and browsers read the PNG and keep the transparency; Paint and older programs
    /// read the bitmap, which has no alpha, so it is put on white rather than on black.</summary>
    [Fact]
    public void Copying_offers_a_transparent_png_and_a_bitmap_on_white()
    {
        DataObject data = CutoutClipboard.CreateData(RedThenClear());

        Assert.True(data.GetDataPresent("PNG"));
        byte[] png = DecodePng((Stream)data.GetData("PNG"), out _, out _);
        Assert.Equal(0, png[7]);

        byte[] flat = Pixels((BitmapSource)data.GetData(DataFormats.Bitmap));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, flat[0..4]);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, flat[4..8]);
    }

    /// <summary>Opaque red, then fully transparent.</summary>
    private static CutoutImage RedThenClear() => new(new byte[] { 0, 0, 255, 255, 0, 0, 0, 0 }, 2, 1);

    private static byte[] DecodePng(Stream stream, out int width, out int height)
    {
        BitmapSource frame = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        width = frame.PixelWidth;
        height = frame.PixelHeight;
        return Pixels(frame);
    }

    private static byte[] Pixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return pixels;
    }
}
