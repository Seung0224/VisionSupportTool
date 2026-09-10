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

    /// <summary>
    /// False for features where pausing has no meaning, so the shell can disable the button
    /// rather than offer a control that does nothing.
    /// </summary>
    bool CanPause { get; }

    /// <summary>Raised when <see cref="State"/> or <see cref="StatusLine"/> changes. May arrive on any thread.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// The feature's screen. Created on first navigation and kept while the feature lives, so
    /// switching menus does not restart anything. Stopping discards it; navigating back after a
    /// stop builds a fresh one.
    /// </summary>
    UserControl GetOrCreateView();

    /// <summary>Radial menu tile icon. One Segoe MDL2 Assets character.</summary>
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

    Task StartAsync();

    Task PauseAsync();

    Task ResumeAsync();

    /// <summary>Full teardown. Never throws.</summary>
    Task StopAsync();
}
