using System.Windows;
using System.Windows.Controls;

namespace VisionSupport.Features;

/// <summary>
/// One entry in the nav rail: a tool that used to be its own program and now runs inside this
/// one. The shell only ever talks to a feature through this interface, so a feature cannot
/// reach into the shell and the shell cannot reach into a feature's internals.
///
/// The lifecycle contract that matters is the one around teardown: <see cref="StopAsync"/> must
/// release every thread, socket, ETW session and native handle the feature took, and must not
/// throw. A feature that cannot stop cleanly reports <see cref="FeatureState.Faulted"/>; it never
/// propagates the failure into the shell, because the shell is expected to stay up for days.
/// </summary>
public interface IFeatureModule
{
    /// <summary>Nav rail label. Also the command bar title.</summary>
    string Title { get; }

    /// <summary>One line under the title on the idle screen card. Static description, not status.</summary>
    string Description { get; }

    FeatureState State { get; }

    /// <summary>
    /// Live one-line summary for the idle screen and the command bar - listen address, attached
    /// pid, that sort of thing. Empty while stopped.
    /// </summary>
    string StatusLine { get; }

    /// <summary>Raised when <see cref="State"/> or <see cref="StatusLine"/> changes. May arrive on any thread.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// The feature's screen. Created on first navigation and kept while the feature lives, so
    /// switching menus does not restart anything. Stopping discards it; navigating back after a
    /// stop builds a fresh one.
    /// </summary>
    UserControl GetOrCreateView();

    /// <summary>
    /// Radial menu tile icon: the name of a Material Design icon, as spelled by
    /// MahApps.Metro.IconPacks (PackIconMaterialKind) - "Memory", "ServerNetwork" and so on.
    ///
    /// A name rather than the enum itself so a feature never has to reference the icon package.
    /// The binding resolves it; a name that does not exist leaves the tile blank rather than
    /// throwing, so LauncherIconTests checks every one of them.
    /// </summary>
    string Glyph { get; }

    /// <summary>Opening size of this feature's window.</summary>
    Size PreferredWindowSize { get; }

    /// <summary>
    /// Drops the feature's UI without touching what it is running.
    ///
    /// This is what closing a feature's window does while the feature is still up: servers, ETW
    /// sessions and sampling timers carry on, but every pixel the feature owns goes away and is
    /// rebuilt from scratch on the next visit. Without this split, closing a window would cut a
    /// VISION client off mid-test.
    /// </summary>
    void ReleaseView();

    /// <summary>
    /// Whether the tool underneath is actually doing something right now - a PLC module up, a
    /// target attached, a batch converting. Closing the feature's window keeps a working feature
    /// alive and releases one that is not.
    ///
    /// Read from the tool itself rather than from <see cref="State"/>: each tool has its own start
    /// and stop buttons, and pressing those never changes State.
    /// </summary>
    bool IsWorking { get; }

    /// <summary>
    /// Why the feature's window must not close right now, or null when it may. Set while the tool
    /// is in the middle of something that can neither carry on without its window nor be cut off
    /// safely - an image batch half written to disk. The window shows this instead of closing,
    /// and the launcher refuses to exit for the same reason.
    /// </summary>
    string? CloseBlockedReason { get; }

    /// <summary>Full teardown. Never throws.</summary>
    Task StopAsync();
}
