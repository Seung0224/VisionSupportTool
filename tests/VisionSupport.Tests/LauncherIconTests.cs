using MahApps.Metro.IconPacks;
using VisionSupport.Features;
using VisionSupport.Launcher;
using VisionSupport.Shell;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// Every tile names its icon with a string, so a typo cannot fail the build - the binding just
/// resolves to nothing and the tile comes up blank. This is the check that would otherwise only
/// happen by someone running the app and squinting at it.
///
/// It covers the shipped link tile too - that one's icon is a string in a settings file, which is
/// even easier to get wrong than a string in code - and the tools box, whose icons are not on the
/// ring at all.
/// </summary>
public class LauncherIconTests
{
    [Fact]
    public void Every_launcher_tile_names_an_icon_that_exists()
    {
        var settings = new LauncherSettings();
        var launcher = new LauncherViewModel(
            new IFeatureModule[] { new ImageConverterFeature() },
            new IFeatureModule[] { new MemoryMonitorFeature(), new PlcServerFeature(), new WiresharkFeature() },
            new ActivityLog(), settings, new LauncherAppearance(settings), DateTime.Now);

        Assert.NotEmpty(launcher.Items);

        foreach (LauncherItem item in launcher.Items.Concat(launcher.Tools))
        {
            Assert.True(Enum.TryParse(item.Glyph, out PackIconMaterialKind _),
                $"'{item.Title}' 타일의 아이콘 이름 '{item.Glyph}' 이(가) PackIconMaterialKind에 없습니다.");
        }
    }

    /// <summary>
    /// The base class's default has to resolve too. A feature that never sets one should show an
    /// empty tile, not a broken binding.
    /// </summary>
    [Fact]
    public void The_default_glyph_resolves()
        => Assert.True(Enum.TryParse("None", out PackIconMaterialKind _));

    /// <summary>
    /// The icons the links editor hands to a tile the user has just made. A name that does not
    /// resolve leaves the tile blank, and with the captions gone a blank tile says nothing at all.
    /// </summary>
    [Theory]
    [InlineData("OpenInNew")]
    [InlineData("FolderOutline")]
    [InlineData("EmailEditOutline")]
    [InlineData("ViewDashboardOutline")]
    public void Icons_the_editor_assigns_exist(string glyph)
        => Assert.True(Enum.TryParse(glyph, out PackIconMaterialKind _),
            $"'{glyph}' 이(가) PackIconMaterialKind에 없습니다.");
}
