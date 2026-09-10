using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VisionSupport.ImageConverter.ViewModels;

public enum ItemStatus
{
    Pending,
    Converting,
    Done,
    Failed,
    Skipped,
}

/// <summary>One file in the conversion queue and how it went.</summary>
public sealed partial class ConversionItem : ObservableObject
{
    public ConversionItem(string sourcePath) => SourcePath = sourcePath;

    public string SourcePath { get; }

    public string FileName => Path.GetFileName(SourcePath);

    [ObservableProperty]
    private ItemStatus _status = ItemStatus.Pending;

    [ObservableProperty]
    private string _message = "";

    [ObservableProperty]
    private string? _outputPath;
}
