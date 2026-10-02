using CommunityToolkit.Mvvm.ComponentModel;
using VisionSupport.Wireshark.Health;

namespace VisionSupport.Wireshark.ViewModels;

public sealed partial class TargetCardViewModel : ObservableObject
{
    public TargetCardViewModel(string id, TargetKind kind)
    {
        Id = id;
        Kind = kind;
    }

    public string Id { get; }

    public TargetKind Kind { get; }

    /// <summary>The machine's own NIC and CXP board are always watched; there is nothing to pin.</summary>
    public bool CanPin => Kind is not (TargetKind.Nic or TargetKind.Cxp);

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private HealthLevel _level;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private bool _pinned;
    [ObservableProperty] private long _dropCount;

    public void Update(TargetSnapshot s)
    {
        Name = s.Name;
        Level = s.Level;
        Summary = s.Summary;
        Pinned = s.Pinned;
        DropCount = s.DropCount;
    }
}
