using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MemMon.Models;

namespace MemMon;

/// <summary>Plain-language glossary for the enum values shown in the GC tab.</summary>
public partial class GcReferenceWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public GcReferenceWindow()
    {
        InitializeComponent();
        DataContext = this;
        SourceInitialized += (_, _) =>
        {
            int enabled = 1;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle,
                DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
        };
    }

    public IReadOnlyList<GcTermRow> Reasons => GcReference.Reasons;

    public IReadOnlyList<GcTermRow> Kinds => GcReference.Kinds;

    public string HowToRead => GcReference.HowToRead;
}
