using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VisionSupport.ImageConverter;
using VisionSupport.ImageConverter.Services;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// The catalog is the single place that knows which extensions can be read, which formats can be
/// written, and which of those written formats has a quality/compression knob. The view reads it
/// to decide whether to show a slider; the view model reads it to filter dropped files.
/// </summary>
public class FormatCatalogTests
{
    [Fact]
    public void Only_jpeg_tiff_and_jpeg_xr_expose_a_compression_control()
    {
        Assert.Equal(CompressionControl.JpegQuality, FormatCatalog.CompressionOf(ImageFormat.Jpeg));
        Assert.Equal(CompressionControl.TiffCompression, FormatCatalog.CompressionOf(ImageFormat.Tiff));
        Assert.Equal(CompressionControl.JpegXrQuality, FormatCatalog.CompressionOf(ImageFormat.JpegXr));

        Assert.Equal(CompressionControl.None, FormatCatalog.CompressionOf(ImageFormat.Png));
        Assert.Equal(CompressionControl.None, FormatCatalog.CompressionOf(ImageFormat.Bmp));
        Assert.Equal(CompressionControl.None, FormatCatalog.CompressionOf(ImageFormat.Gif));
    }

    [Fact]
    public void Input_extension_matching_ignores_case_and_rejects_non_images()
    {
        Assert.True(FormatCatalog.IsSupportedInput(".png"));
        Assert.True(FormatCatalog.IsSupportedInput(".PNG"));
        Assert.True(FormatCatalog.IsSupportedInput(".jpeg"));
        Assert.False(FormatCatalog.IsSupportedInput(".txt"));
    }

    [Fact]
    public void Every_output_format_has_a_dotted_extension()
    {
        Assert.Equal(".jpg", FormatCatalog.ExtensionOf(ImageFormat.Jpeg));
        Assert.Equal(".png", FormatCatalog.ExtensionOf(ImageFormat.Png));
        Assert.Equal(".tif", FormatCatalog.ExtensionOf(ImageFormat.Tiff));
    }
}

/// <summary>
/// Where a converted file lands and what it is named when the user chose "save straight to a
/// folder" instead of a dialog.
/// </summary>
public class OutputPathResolverTests
{
    [Fact]
    public void ResolveDirect_keeps_the_source_name_and_swaps_the_extension()
    {
        using var dir = new TempDir();

        string result = OutputPathResolver.ResolveDirect(@"C:\in\photo.bmp", ".png", dir.Path);

        Assert.Equal(Path.Combine(dir.Path, "photo.png"), result);
    }

    [Fact]
    public void ResolveDirect_appends_a_counter_when_the_target_name_is_taken()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "photo.png"), "");

        string result = OutputPathResolver.ResolveDirect(@"C:\in\photo.bmp", ".png", dir.Path);

        Assert.Equal(Path.Combine(dir.Path, "photo (1).png"), result);
    }

    [Fact]
    public void ResolveDirect_walks_the_counter_past_every_existing_name()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "photo.png"), "");
        File.WriteAllText(Path.Combine(dir.Path, "photo (1).png"), "");

        string result = OutputPathResolver.ResolveDirect(@"C:\in\photo.bmp", ".png", dir.Path);

        Assert.Equal(Path.Combine(dir.Path, "photo (2).png"), result);
    }

    [Fact]
    public void ResolveDirect_never_returns_the_source_path_itself()
    {
        using var dir = new TempDir();
        string source = Path.Combine(dir.Path, "photo.png");
        File.WriteAllText(source, "");

        string result = OutputPathResolver.ResolveDirect(source, ".png", dir.Path);

        Assert.NotEqual(source, result);
        Assert.Equal(Path.Combine(dir.Path, "photo (1).png"), result);
    }
}

/// <summary>
/// The decode -> resize -> grayscale -> bit depth -> encode pipeline, exercised end to end
/// through the real WPF codec against temp files.
/// </summary>
public class ConversionPipelineTests
{
    [Fact]
    public void Convert_from_bmp_to_png_preserves_pixel_size()
    {
        using var dir = new TempDir();
        string src = Path.Combine(dir.Path, "in.bmp");
        string dst = Path.Combine(dir.Path, "out.png");
        SaveBmp(src, width: 6, height: 3);

        ConversionPipeline.Convert(src, dst, ImageFormat.Png, new ConversionOptions(), new WpfImageCodec());

        BitmapFrame decoded = Decode(dst);
        Assert.Equal(6, decoded.PixelWidth);
        Assert.Equal(3, decoded.PixelHeight);
    }

    [Fact]
    public void Convert_with_percent_resize_halves_both_dimensions()
    {
        using var dir = new TempDir();
        string src = Path.Combine(dir.Path, "in.bmp");
        string dst = Path.Combine(dir.Path, "out.png");
        SaveBmp(src, width: 8, height: 4);

        var options = new ConversionOptions { ResizeKind = ResizeKind.Percent, ResizePercent = 50 };
        ConversionPipeline.Convert(src, dst, ImageFormat.Png, options, new WpfImageCodec());

        BitmapFrame decoded = Decode(dst);
        Assert.Equal(4, decoded.PixelWidth);
        Assert.Equal(2, decoded.PixelHeight);
    }

    [Fact]
    public void Convert_with_pixel_resize_keeping_aspect_fits_inside_the_box()
    {
        using var dir = new TempDir();
        string src = Path.Combine(dir.Path, "in.bmp");
        string dst = Path.Combine(dir.Path, "out.png");
        SaveBmp(src, width: 8, height: 4);

        var options = new ConversionOptions
        {
            ResizeKind = ResizeKind.Pixels,
            ResizeWidth = 4,
            ResizeHeight = 4,
            KeepAspect = true,
        };
        ConversionPipeline.Convert(src, dst, ImageFormat.Png, options, new WpfImageCodec());

        BitmapFrame decoded = Decode(dst);
        Assert.Equal(4, decoded.PixelWidth);
        Assert.Equal(2, decoded.PixelHeight);
    }

    [Fact]
    public void Convert_with_pixel_resize_and_no_aspect_lock_stretches_to_the_exact_size()
    {
        using var dir = new TempDir();
        string src = Path.Combine(dir.Path, "in.bmp");
        string dst = Path.Combine(dir.Path, "out.png");
        SaveBmp(src, width: 8, height: 4);

        var options = new ConversionOptions
        {
            ResizeKind = ResizeKind.Pixels,
            ResizeWidth = 6,
            ResizeHeight = 6,
            KeepAspect = false,
        };
        ConversionPipeline.Convert(src, dst, ImageFormat.Png, options, new WpfImageCodec());

        BitmapFrame decoded = Decode(dst);
        Assert.Equal(6, decoded.PixelWidth);
        Assert.Equal(6, decoded.PixelHeight);
    }

    [Fact]
    public void Convert_with_grayscale_writes_an_eight_bit_gray_image()
    {
        using var dir = new TempDir();
        string src = Path.Combine(dir.Path, "in.bmp");
        string dst = Path.Combine(dir.Path, "out.png");
        SaveBmp(src, width: 4, height: 4);

        ConversionPipeline.Convert(src, dst, ImageFormat.Png, new ConversionOptions { Grayscale = true },
            new WpfImageCodec());

        Assert.Equal(PixelFormats.Gray8, Decode(dst).Format);
    }

    private static BitmapFrame Decode(string path)
        => BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];

    private static void SaveBmp(string path, int width, int height)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 40; pixels[i + 1] = 120; pixels[i + 2] = 200; pixels[i + 3] = 255;
        }
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}

/// <summary>The real runner wired to the WPF codec: name resolution, folder creation and the
/// pipeline together, the way the view model calls it.</summary>
public class ConversionRunnerTests
{
    [Fact]
    public void Convert_writes_a_file_into_a_folder_it_creates_and_returns_that_path()
    {
        using var dir = new TempDir();
        string src = Path.Combine(dir.Path, "shot.bmp");
        WriteBmp(src, 5, 5);
        string outFolder = Path.Combine(dir.Path, "converted");
        var runner = new ConversionRunner(new WpfImageCodec());

        string result = runner.Convert(src, outFolder, new ConversionOptions { TargetFormat = ImageFormat.Jpeg });

        Assert.Equal(Path.Combine(outFolder, "shot.jpg"), result);
        Assert.True(File.Exists(result));
    }

    private static void WriteBmp(string path, int width, int height)
    {
        int stride = width * 4;
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null,
            new byte[stride * height], stride);
        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}

/// <summary>The settings file: missing or corrupt falls back to defaults; a save round-trips.</summary>
public class SettingsStoreTests
{
    [Fact]
    public void Load_returns_defaults_when_the_file_is_missing()
    {
        using var dir = new TempDir();
        var store = new SettingsStore(Path.Combine(dir.Path, "settings.json"));

        ImageConverterSettings settings = store.Load();

        Assert.Equal(ImageFormat.Png, settings.Options.TargetFormat);
        Assert.True(settings.RecurseFolders);
    }

    [Fact]
    public void Save_then_Load_round_trips_changed_values()
    {
        using var dir = new TempDir();
        var store = new SettingsStore(Path.Combine(dir.Path, "settings.json"));

        store.Save(new ImageConverterSettings
        {
            RecurseFolders = false,
            Options = new ConversionOptions { JpegQuality = 55, SaveMode = SaveMode.DirectToFolder, OutputFolder = @"D:\out" },
        });
        ImageConverterSettings reloaded = store.Load();

        Assert.False(reloaded.RecurseFolders);
        Assert.Equal(55, reloaded.Options.JpegQuality);
        Assert.Equal(SaveMode.DirectToFolder, reloaded.Options.SaveMode);
        Assert.Equal(@"D:\out", reloaded.Options.OutputFolder);
    }

    [Fact]
    public void Load_returns_defaults_when_the_file_is_corrupt()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, "{ this is not json");
        var store = new SettingsStore(path);

        ImageConverterSettings settings = store.Load();

        Assert.Equal(ImageFormat.Png, settings.Options.TargetFormat);
        Assert.True(settings.RecurseFolders);
    }
}
