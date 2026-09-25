namespace HeroParser.Tests;

/// <summary>
/// A progress implementation that reports synchronously for use in tests.
/// </summary>
/// <remarks>
/// <see cref="Progress{T}"/> posts each report to the captured synchronization context or the
/// thread pool, so its callbacks can still be pending when the operation returns. Tests that
/// assert on reports use this type so every report has been handled by then.
/// </remarks>
/// <typeparam name="T">The progress value type.</typeparam>
internal sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
