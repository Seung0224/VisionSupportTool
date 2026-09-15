using VisionSupport.Sam;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// The fixed set of encoding buffers the encoder thread writes and the decoder thread reads. The
/// one rule that matters: the writer is never handed a buffer that is current or still being read,
/// because overwriting it would put a new picture's pixels under an old picture's mask.
/// </summary>
public class HandoffPoolTests
{
    [Fact]
    public void Nothing_is_current_until_something_is_published()
        => Assert.Null(new HandoffPool<string>(new[] { "a", "b" }).AcquireCurrent());

    [Fact]
    public void The_writer_never_gets_the_current_item_or_one_being_read()
    {
        var pool = new HandoffPool<string>(new[] { "a", "b", "c" });

        string first = pool.RentForWriting()!;
        pool.Publish(first);
        string reading = pool.AcquireCurrent()!;
        Assert.Same(first, reading);

        string second = pool.RentForWriting()!;
        Assert.NotSame(first, second);
        pool.Publish(second);

        string third = pool.RentForWriting()!;
        Assert.NotSame(first, third);
        Assert.NotSame(second, third);
    }

    [Fact]
    public void With_every_item_current_or_being_read_the_writer_gets_nothing_until_one_is_released()
    {
        var pool = new HandoffPool<string>(new[] { "a", "b" });

        string first = pool.RentForWriting()!;
        pool.Publish(first);
        string reading = pool.AcquireCurrent()!;
        string second = pool.RentForWriting()!;
        pool.Publish(second);

        Assert.Null(pool.RentForWriting());

        pool.Release(reading);
        Assert.Same(first, pool.RentForWriting());
    }
}
