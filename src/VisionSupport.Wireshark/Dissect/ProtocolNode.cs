namespace VisionSupport.Wireshark.Dissect;

/// <summary>One line of the protocol tree, with the bytes it covers so the hex view can point at them.</summary>
public sealed class ProtocolNode
{
    public ProtocolNode(string text, int offset, int length)
    {
        Text = text;
        Offset = offset;
        Length = length;
    }

    public string Text { get; }

    public int Offset { get; }

    public int Length { get; }

    public List<ProtocolNode> Children { get; } = new();

    public ProtocolNode Add(string text, int offset, int length)
    {
        var child = new ProtocolNode(text, offset, length);
        Children.Add(child);
        return child;
    }
}
