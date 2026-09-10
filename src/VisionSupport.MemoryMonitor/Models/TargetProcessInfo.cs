namespace MemMon.Models;

/// <summary>A running process that looks like a .NET program, as offered in the target picker.</summary>
public sealed record TargetProcessInfo(
    int Pid,
    string Name,
    string RuntimeLabel,
    bool Is32Bit,
    bool NeedsElevation)
{
    public bool CanAttach => !Is32Bit && !NeedsElevation;

    public string Display => this switch
    {
        { Is32Bit: true } => $"{Name} ({Pid})  ·  {RuntimeLabel}  ·  x86 — 이 빌드로는 attach 불가",
        { NeedsElevation: true } => $"{Name} ({Pid})  ·  {RuntimeLabel}  ·  관리자 권한 필요",
        _ => $"{Name} ({Pid})  ·  {RuntimeLabel}",
    };

    /// <summary>
    /// A ComboBox's closed-state box falls back to ToString() whenever it cannot resolve a
    /// template, so the readable form has to be ToString() itself - not only DisplayMemberPath.
    /// </summary>
    public override string ToString() => Display;
}
