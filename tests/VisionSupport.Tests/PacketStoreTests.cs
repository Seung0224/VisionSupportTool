using VisionSupport.Wireshark.Dissect;
using VisionSupport.Wireshark.Store;
using Xunit;

namespace VisionSupport.Tests;

public class PacketStoreTests
{
    private static Packet P(long n, int size = 10) => new(n, TestFrames.T0, new byte[size], size);

    private static long[] Numbers(PacketStore s) => s.Snapshot().Select(p => p.Number).ToArray();

    [Fact]
    public void The_ring_drops_the_oldest_past_its_count()
    {
        var store = new PacketStore(maxBytes: long.MaxValue, maxCount: 3);
        for (int i = 1; i <= 5; i++) store.Add(P(i));

        Assert.Equal(new long[] { 3, 4, 5 }, Numbers(store));
    }

    [Fact]
    public void The_ring_drops_the_oldest_past_its_bytes()
    {
        var store = new PacketStore(maxBytes: 25, maxCount: 100);
        for (int i = 1; i <= 4; i++) store.Add(P(i));

        Assert.Equal(new long[] { 3, 4 }, Numbers(store));
        Assert.Equal(20, store.Bytes);
    }

    [Fact]
    public void Packets_around_an_anomaly_outlive_the_ring()
    {
        var store = new PacketStore(long.MaxValue, maxCount: 3, keepBefore: 1, keepAfter: 1);
        for (int i = 1; i <= 4; i++) store.Add(P(i));
        store.KeepAround(4);
        for (int i = 5; i <= 10; i++) store.Add(P(i));

        Assert.Equal(new long[] { 3, 4, 5, 8, 9, 10 }, Numbers(store));
    }

    [Fact]
    public void Kept_events_are_capped_oldest_first()
    {
        var store = new PacketStore(long.MaxValue, maxCount: 1, keepBefore: 0, keepAfter: 0, maxKeptEvents: 2);
        for (int i = 1; i <= 3; i++)
        {
            store.Add(P(i));
            store.KeepAround(i);
        }
        store.Add(P(4));

        Assert.Equal(new long[] { 2, 3, 4 }, Numbers(store));
    }

    [Fact]
    public void A_packet_kept_by_two_events_survives_the_first_being_dropped()
    {
        var store = new PacketStore(long.MaxValue, maxCount: 2, keepBefore: 1, keepAfter: 0, maxKeptEvents: 1);
        store.Add(P(1));
        store.Add(P(2));
        store.KeepAround(2); // keeps 1, 2
        store.KeepAround(2); // keeps 1, 2 again; the first event is dropped
        store.Add(P(3));

        Assert.Equal(new long[] { 1, 2, 3 }, Numbers(store));
    }
}
