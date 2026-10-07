using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace VisionSupport.Sam;

/// <summary>
/// A UI Automation client (a screen reader, or accessibility/security tooling that walks visible
/// windows) holds a native COM reference to whatever automation peers it touched. No managed
/// cleanup - not DataContext, not event handlers - can make .NET collect a window while that
/// reference lives, because the reference isn't managed. Every window that can be opened and
/// closed more than once must call <see cref="Disconnect"/> from its Closed handler, or it leaks
/// one whole window (and whatever it references) per open/close cycle under such a client.
/// </summary>
internal static class AutomationDisconnect
{
    private static readonly MethodInfo? ProviderFromPeer = typeof(AutomationPeer).GetMethod(
        "ProviderFromPeer", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>
    /// Forcibly severs the native COM reference on every automation peer already created for
    /// <paramref name="root"/>'s tree, using UiaDisconnectProvider - the API Windows documents for
    /// exactly this situation ("the owning window has been destroyed"). WPF has no public way to
    /// reach a peer's provider, so this reflects into the internal one; any failure is swallowed,
    /// since this is a best-effort mitigation and must never take the app down with it.
    /// </summary>
    public static void Disconnect(UIElement root)
    {
        try
        {
            if (ProviderFromPeer is null) return;

            AutomationPeer? peer = UIElementAutomationPeer.FromElement(root);
            if (peer is null) return;

            Walk(peer);
        }
        catch
        {
            // Best-effort mitigation for a native reference the managed side cannot inspect
            // reliably across .NET versions - never let this take the shell down with it.
        }
    }

    private static void Walk(AutomationPeer peer)
    {
        if (ProviderFromPeer!.Invoke(peer, new object[] { peer }) is IRawElementProviderSimple provider)
            UiaDisconnectProvider(provider);

        foreach (AutomationPeer child in peer.GetChildren() ?? new List<AutomationPeer>())
            Walk(child);
    }

    [DllImport("UIAutomationCore.dll")]
    private static extern int UiaDisconnectProvider(IRawElementProviderSimple provider);
}
