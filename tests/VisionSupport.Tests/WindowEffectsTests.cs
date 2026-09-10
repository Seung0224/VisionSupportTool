using VisionSupport.Launcher;
using VisionSupport.Windows;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// Only the arithmetic is covered here. The P/Invoke side needs a real HWND and a pair of eyes;
/// it is checked in the smoke pass, not in xunit.
/// </summary>
public class WindowEffectsTests
{
    [Fact]
    public void Full_opacity_is_fully_opaque()
        => Assert.Equal((byte)255, WindowEffects.ToAlphaByte(1.0));

    [Fact]
    public void The_default_opacity_maps_to_the_expected_byte()
        => Assert.Equal((byte)235, WindowEffects.ToAlphaByte(LauncherSettings.DefaultOpacity));

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void Values_below_the_floor_clamp_to_the_floor(double opacity)
        => Assert.Equal((byte)77, WindowEffects.ToAlphaByte(opacity));

    [Fact]
    public void Values_above_one_clamp_to_opaque()
        => Assert.Equal((byte)255, WindowEffects.ToAlphaByte(3.0));

    [Fact]
    public void NaN_falls_back_to_the_default_rather_than_vanishing()
        => Assert.Equal((byte)235, WindowEffects.ToAlphaByte(double.NaN));
}
