namespace VisionSupport.Sam;

/// <summary>
/// Tidies a mask before it is shown or cut. The decoder's mask, scaled up from 288 pixels, arrives
/// with stray specks away from the object and pinholes inside it; a cutout keeps every one of them.
///
/// Two rules: keep only the piece the user is pointing at, and fill holes smaller than 1% of that
/// piece. Larger holes are real - the gap between an arm and a body - and stay open.
///
/// Works in place, with work buffers the caller reuses for the whole mode: at a million pixels a
/// decode, fresh ones would be exactly the allocation churn the mode was cured of.
/// </summary>
public static class MaskCleanup
{
    /// <summary>Holes under 1/100 of the kept piece's area are filled.</summary>
    public const int HoleFraction = 100;

    private const byte Unseen = 0;
    private const byte Kept = 1;
    private const byte Seen = 2;

    /// <summary>
    /// Keeps the 4-connected piece under (<paramref name="seedX"/>, <paramref name="seedY"/>), crop
    /// coordinates - or the largest piece when the cursor is not on the mask - then fills its small
    /// holes. <paramref name="marks"/> and <paramref name="queue"/> need Width × Height elements.
    /// Returns false when the mask is empty.
    /// </summary>
    public static bool Clean(SamMask mask, int seedX, int seedY, byte[] marks, int[] queue)
    {
        int width = mask.Width;
        int height = mask.Height;
        int count = width * height;
        bool[] inside = mask.Inside;

        Array.Clear(marks, 0, count);

        int start = -1;
        if (seedX >= 0 && seedX < width && seedY >= 0 && seedY < height && inside[seedY * width + seedX])
        {
            start = seedY * width + seedX;
        }
        else
        {
            int largest = 0;
            for (int i = 0; i < count; i++)
            {
                if (!inside[i] || marks[i] != Unseen) continue;

                int size = Flood(inside, marks, queue, width, height, i, wanted: true, mark: Seen);
                if (size > largest)
                {
                    largest = size;
                    start = i;
                }
            }

            if (start < 0) return false;
            Array.Clear(marks, 0, count);
        }

        int area = Flood(inside, marks, queue, width, height, start, wanted: true, mark: Kept);
        for (int i = 0; i < count; i++) inside[i] = marks[i] == Kept;

        // Background that reaches the crop's border is outside, not a hole.
        for (int x = 0; x < width; x++)
        {
            MarkOutside(inside, marks, queue, width, height, x);
            MarkOutside(inside, marks, queue, width, height, (height - 1) * width + x);
        }
        for (int y = 0; y < height; y++)
        {
            MarkOutside(inside, marks, queue, width, height, y * width);
            MarkOutside(inside, marks, queue, width, height, y * width + width - 1);
        }

        int largestHole = Math.Max(1, area / HoleFraction);
        for (int i = 0; i < count; i++)
        {
            if (inside[i] || marks[i] != Unseen) continue;

            int size = Flood(inside, marks, queue, width, height, i, wanted: false, mark: Seen);
            if (size >= largestHole) continue;

            for (int q = 0; q < size; q++) inside[queue[q]] = true;
        }

        return true;
    }

    private static void MarkOutside(bool[] inside, byte[] marks, int[] queue, int width, int height, int index)
    {
        if (!inside[index] && marks[index] == Unseen)
        {
            Flood(inside, marks, queue, width, height, index, wanted: false, mark: Seen);
        }
    }

    /// <summary>
    /// Breadth-first over the 4-connected pixels whose inside flag is <paramref name="wanted"/> and
    /// that are unmarked, marking each. Returns how many; the queue then holds exactly those indices,
    /// so a caller can walk them.
    /// </summary>
    private static int Flood(bool[] inside, byte[] marks, int[] queue, int width, int height, int start,
                             bool wanted, byte mark)
    {
        int count = width * height;
        int head = 0;
        int tail = 0;

        marks[start] = mark;
        queue[tail++] = start;

        while (head < tail)
        {
            int i = queue[head++];
            int x = i % width;

            if (x > 0) Visit(i - 1);
            if (x < width - 1) Visit(i + 1);
            if (i >= width) Visit(i - width);
            if (i < count - width) Visit(i + width);
        }

        return tail;

        void Visit(int j)
        {
            if (marks[j] != Unseen || inside[j] != wanted) return;
            marks[j] = mark;
            queue[tail++] = j;
        }
    }
}
