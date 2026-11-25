using Xunit;

namespace HeroParser.Tests.Memory;

/// <summary>
/// Thread-local allocation measurement helper backed by <see cref="GC.GetAllocatedBytesForCurrentThread"/>.
/// Uses warmup iterations and takes the minimum across samples so one-time JIT/tiered-PGO promotions
/// do not flake deterministic allocation checks.
/// </summary>
internal readonly struct AllocationMeter
{
    internal static long MeasureMin(Action action, int warmup = 2, int samples = 3)
    {
        ArgumentNullException.ThrowIfNull(action);

        for (int i = 0; i < warmup; i++)
            action();

        long minBytes = long.MaxValue;
        for (int i = 0; i < samples; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            action();
            long delta = GC.GetAllocatedBytesForCurrentThread() - before;
            if (delta < minBytes)
                minBytes = delta;
        }

        return minBytes;
    }

    internal static async Task<long> MeasureMinAsync(Func<Task> action, int warmup = 2, int samples = 5)
    {
        ArgumentNullException.ThrowIfNull(action);

        for (int i = 0; i < warmup; i++)
            await action();

        long minBytes = long.MaxValue;
        int sameThreadSamples = 0;

        for (int i = 0; i < samples; i++)
        {
            int startThreadId = Environment.CurrentManagedThreadId;
            long before = GC.GetAllocatedBytesForCurrentThread();
            await action();
            long after = GC.GetAllocatedBytesForCurrentThread();

            if (Environment.CurrentManagedThreadId == startThreadId)
            {
                sameThreadSamples++;
                long delta = after - before;
                if (delta < minBytes)
                    minBytes = delta;
            }
        }

        Assert.True(
            sameThreadSamples > 0,
            "Async operation never completed on the starting thread; current-thread allocation could not be observed.");

        return minBytes;
    }

    internal static void AssertZeroAlloc(Action action, string scenario, int warmup = 2, int samples = 3)
    {
        long bytes = MeasureMin(action, warmup, samples);
        Assert.True(bytes == 0, $"{scenario}: expected 0 B allocated, measured {bytes:N0} B.");
    }

    internal static async Task AssertZeroAllocAsync(Func<Task> action, string scenario, int warmup = 2, int samples = 5)
    {
        long bytes = await MeasureMinAsync(action, warmup, samples);
        Assert.True(bytes == 0, $"{scenario}: expected 0 B allocated, measured {bytes:N0} B.");
    }

    internal static void AssertZeroPerRowGrowth(
        Action smallRun,
        Action largeRun,
        int deltaRows,
        string scenario,
        int warmup = 2,
        int samples = 3)
    {
        long smallBytes = MeasureMin(smallRun, warmup, samples);
        long largeBytes = MeasureMin(largeRun, warmup, samples);
        long growth = largeBytes - smallBytes;

        Assert.True(
            growth <= 0,
            $"{scenario}: expected 0 B/row growth over +{deltaRows:N0} rows, got small={smallBytes:N0} B, large={largeBytes:N0} B (growth={growth:N0} B, {growth / (double)deltaRows:F2} B/row).");
    }

    internal static async Task AssertZeroPerRowGrowthAsync(
        Func<Task> smallRun,
        Func<Task> largeRun,
        int deltaRows,
        string scenario,
        int warmup = 2,
        int samples = 5)
    {
        long smallBytes = await MeasureMinAsync(smallRun, warmup, samples);
        long largeBytes = await MeasureMinAsync(largeRun, warmup, samples);
        long growth = largeBytes - smallBytes;

        Assert.True(
            growth <= 0,
            $"{scenario}: expected 0 B/row growth over +{deltaRows:N0} rows, got small={smallBytes:N0} B, large={largeBytes:N0} B (growth={growth:N0} B, {growth / (double)deltaRows:F2} B/row).");
    }

    internal static void AssertBoundedPerRowGrowth(
        Action smallRun,
        Action largeRun,
        int deltaRows,
        double maxBytesPerRow,
        string scenario,
        int warmup = 2,
        int samples = 3)
    {
        long smallBytes = MeasureMin(smallRun, warmup, samples);
        long largeBytes = MeasureMin(largeRun, warmup, samples);
        double bytesPerRow = (largeBytes - smallBytes) / (double)deltaRows;

        Assert.True(
            bytesPerRow <= maxBytesPerRow,
            $"{scenario}: expected <= {maxBytesPerRow:F2} B/row over +{deltaRows:N0} rows, measured {bytesPerRow:F2} B/row (small={smallBytes:N0} B, large={largeBytes:N0} B).");
    }

    internal static async Task AssertBoundedPerRowGrowthAsync(
        Func<Task> smallRun,
        Func<Task> largeRun,
        int deltaRows,
        double maxBytesPerRow,
        string scenario,
        int warmup = 2,
        int samples = 5)
    {
        long smallBytes = await MeasureMinAsync(smallRun, warmup, samples);
        long largeBytes = await MeasureMinAsync(largeRun, warmup, samples);
        double bytesPerRow = (largeBytes - smallBytes) / (double)deltaRows;

        Assert.True(
            bytesPerRow <= maxBytesPerRow,
            $"{scenario}: expected <= {maxBytesPerRow:F2} B/row over +{deltaRows:N0} rows, measured {bytesPerRow:F2} B/row (small={smallBytes:N0} B, large={largeBytes:N0} B).");
    }
}
