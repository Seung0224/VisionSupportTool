namespace VisionSupport.Features;

/// <summary>
/// The run state of one hosted feature.
///
/// Stopped/Running/Paused are the three the user drives with the command bar; the rest are
/// transient or terminal states the shell shows but never asks for. <see cref="Faulted"/> is
/// how a feature reports that it broke without taking the shell down with it - the shell paints
/// it red and leaves everything else running.
/// </summary>
public enum FeatureState
{
    Stopped,
    Starting,
    Running,
    Paused,
    Stopping,
    Faulted,
}
