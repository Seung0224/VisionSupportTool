using VisionSupport.Sam;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// The small "1/3" counter beside the cursor - the user's only way to tell which of the wheel's
/// steps they are on, since the mask itself does not say.
/// </summary>
public class LevelLabelTests
{
    [Fact]
    public void Level_is_shown_one_based_out_of_the_total_choices()
        => Assert.Equal("1/3", LevelLabel.Format(level: 0, total: 3));

    [Fact]
    public void A_deeper_level_advances_the_first_number()
        => Assert.Equal("3/3", LevelLabel.Format(level: 2, total: 3));

    /// <summary>Every mask below the trust threshold except one leaves a single choice - "1/1", not
    /// a division by a level that no longer exists.</summary>
    [Fact]
    public void A_single_available_mask_reads_one_of_one()
        => Assert.Equal("1/1", LevelLabel.Format(level: 0, total: 1));
}
