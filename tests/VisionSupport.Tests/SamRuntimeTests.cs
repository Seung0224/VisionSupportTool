using System.IO;
using VisionSupport.Sam;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// The two decisions that made the difference between DirectML working and failing on this PC:
/// which DirectML.dll gets loaded, and which GPU the session is created on.
/// </summary>
public class SamRuntimeTests
{
    [Fact]
    public void The_redistributable_DirectML_is_taken_from_the_first_folder_that_has_it()
    {
        using var empty = new TempDir();
        using var first = new TempDir();
        using var second = new TempDir();
        File.WriteAllBytes(Path.Combine(first.Path, "DirectML.dll"), new byte[1]);
        File.WriteAllBytes(Path.Combine(second.Path, "DirectML.dll"), new byte[1]);

        Assert.Equal(Path.Combine(first.Path, "DirectML.dll"),
                     DirectMlLoader.FindRedistributable(new[] { empty.Path, first.Path, second.Path }));
    }

    [Fact]
    public void No_folder_with_DirectML_gives_nothing_to_load()
    {
        using var empty = new TempDir();

        Assert.Null(DirectMlLoader.FindRedistributable(new[] { empty.Path }));
    }

    /// <summary>This PC's own adapters. DXGI lists the iGPU and the software renderer too.</summary>
    [Fact]
    public void The_hardware_GPU_with_the_most_dedicated_memory_is_chosen()
    {
        var adapters = new[]
        {
            new GpuAdapter(0, "Intel(R) UHD Graphics", 128L << 20, IsSoftware: false),
            new GpuAdapter(1, "NVIDIA GeForce RTX 4070 Laptop GPU", 7948L << 20, IsSoftware: false),
            new GpuAdapter(2, "Microsoft Basic Render Driver", 0, IsSoftware: true),
        };

        Assert.Equal(1, GpuAdapters.Pick(adapters)?.Index);
    }

    [Fact]
    public void A_software_adapter_alone_means_there_is_no_GPU()
        => Assert.Null(GpuAdapters.Pick(new[] { new GpuAdapter(0, "Microsoft Basic Render Driver", 1L << 30, IsSoftware: true) }));
}
