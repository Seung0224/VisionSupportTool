namespace VisionSupport.Features;

/// <summary>
/// Where one hosted feature is in the shell's release of it.
///
/// The shell does not start or stop a feature - each tool has its own buttons for that, and
/// whether it is doing anything is <see cref="IFeatureModule.IsWorking"/>. This only tracks
/// <see cref="IFeatureModule.StopAsync"/>. <see cref="Faulted"/> is how a feature reports that it
/// broke without taking the shell down with it - the shell paints it red and leaves everything
/// else running.
/// </summary>
public enum FeatureState
{
    Stopped,
    Stopping,
    Faulted,
}
