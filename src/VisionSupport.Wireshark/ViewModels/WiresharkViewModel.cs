using CommunityToolkit.Mvvm.ComponentModel;

namespace VisionSupport.Wireshark.ViewModels;

/// <summary>Skeleton so the feature can be registered; Task 15 replaces this file.</summary>
public sealed partial class WiresharkViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    private string _status = string.Empty;

    public bool IsWorking => false;

    public bool HasAlert => false;

    public void Dispose()
    {
    }
}
